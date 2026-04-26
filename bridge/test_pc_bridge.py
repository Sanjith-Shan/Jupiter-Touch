"""
Unit tests for pc_bridge.py — verifies the dual-hand routing logic without
requiring real Arduinos. Run with:

    python bridge/test_pc_bridge.py
"""

import sys
import unittest
from pathlib import Path

# Make `pc_bridge` importable regardless of where the test is run from.
sys.path.insert(0, str(Path(__file__).parent))

import pc_bridge


# ── Mock serial endpoint ─────────────────────────────────────────────────────

class FakeHandBridge(pc_bridge.HandBridge):
    """A HandBridge that records every command instead of opening a real
    serial port. Construction skips the serial_open path; sends append to
    a per-instance list."""

    def __init__(self, name: str):
        # Skip parent's serial setup entirely.
        self.name = name
        self.serial_port = f"FAKE_{name}"
        self._ser = None
        self._lock = None
        self.sent = []

    def send_command(self, command: str):
        self.sent.append(command)

    def all_off(self):
        self.sent.append("ALLOFF")

    def close(self):
        pass


# ── depth_to_pot ─────────────────────────────────────────────────────────────

class DepthToPotTests(unittest.TestCase):
    def test_min_depth_gives_max_pot(self):
        # depth 0 = just touching = lowest stim = highest pot value
        self.assertEqual(pc_bridge.depth_to_pot(0.0), pc_bridge.MAX_POT_VALUE)

    def test_max_depth_gives_min_pot(self):
        # depth 1 = max press = highest stim = lowest pot value
        self.assertEqual(pc_bridge.depth_to_pot(1.0), pc_bridge.MIN_POT_VALUE)

    def test_clamps_below_zero(self):
        self.assertEqual(pc_bridge.depth_to_pot(-0.5), pc_bridge.MAX_POT_VALUE)

    def test_clamps_above_one(self):
        self.assertEqual(pc_bridge.depth_to_pot(2.0), pc_bridge.MIN_POT_VALUE)

    def test_monotonic(self):
        prev = pc_bridge.depth_to_pot(0.0)
        for d in [0.1, 0.25, 0.5, 0.75, 0.9, 1.0]:
            cur = pc_bridge.depth_to_pot(d)
            self.assertLessEqual(cur, prev, f"non-monotonic at depth={d}")
            prev = cur


# ── build_command ────────────────────────────────────────────────────────────

class BuildCommandTests(unittest.TestCase):
    def test_active_thumb_full(self):
        self.assertEqual(pc_bridge.build_command("Thumb", True, 1.0),
                         f"C1I{pc_bridge.MIN_POT_VALUE}")

    def test_active_thumb_zero(self):
        self.assertEqual(pc_bridge.build_command("Thumb", True, 0.0),
                         f"C1I{pc_bridge.MAX_POT_VALUE}")

    def test_inactive_thumb(self):
        self.assertEqual(pc_bridge.build_command("Thumb", False, 0.5), "C1OFF")

    def test_inactive_palm(self):
        self.assertEqual(pc_bridge.build_command("Palm", False, 0.0), "C6OFF")

    def test_unknown_finger_returns_none(self):
        self.assertIsNone(pc_bridge.build_command("Pinkie", True, 0.5))
        self.assertIsNone(pc_bridge.build_command("", True, 0.5))

    def test_all_six_channels(self):
        expected = {"Thumb": 1, "Index": 2, "Middle": 3,
                    "Ring": 4, "Pinky": 5, "Palm": 6}
        for finger, channel in expected.items():
            cmd = pc_bridge.build_command(finger, False, 0.0)
            self.assertEqual(cmd, f"C{channel}OFF")


# ── JupiterRouter — both hands configured ───────────────────────────────────

class RouterBothHandsTests(unittest.TestCase):
    def setUp(self):
        self.right = FakeHandBridge("right")
        self.left  = FakeHandBridge("left")
        self.router = pc_bridge.JupiterRouter({"right": self.right, "left": self.left})

    def test_right_hand_routes_to_right(self):
        result = self.router.handle_contact({
            "hand": "right", "finger": "Index", "active": True, "depth": 0.5
        })
        self.assertEqual(result[0], "right")
        self.assertEqual(self.right.sent, ["C2I125"])
        self.assertEqual(self.left.sent,  [])

    def test_left_hand_routes_to_left(self):
        result = self.router.handle_contact({
            "hand": "left", "finger": "Thumb", "active": True, "depth": 1.0
        })
        self.assertEqual(result[0], "left")
        self.assertEqual(self.left.sent,  [f"C1I{pc_bridge.MIN_POT_VALUE}"])
        self.assertEqual(self.right.sent, [])

    def test_simultaneous_independence(self):
        """A burst of mixed-hand events lands cleanly on the correct ports."""
        events = [
            {"hand": "right", "finger": "Thumb",  "active": True, "depth": 0.3},
            {"hand": "left",  "finger": "Index",  "active": True, "depth": 0.6},
            {"hand": "right", "finger": "Thumb",  "active": False, "depth": 0.0},
            {"hand": "left",  "finger": "Middle", "active": True, "depth": 0.9},
            {"hand": "left",  "finger": "Index",  "active": False, "depth": 0.0},
        ]
        for ev in events:
            self.router.handle_contact(ev)

        # Right should have seen only Thumb events
        self.assertEqual(self.right.sent, ["C1I163", "C1OFF"])
        # Left should have seen Index + Middle events
        self.assertEqual(self.left.sent,  ["C2I106", "C3I49", "C2OFF"])

    def test_default_hand_when_missing_is_right(self):
        # Backwards compatibility: payload from an older Unity build with
        # no "hand" field should land on the right Arduino.
        result = self.router.handle_contact({
            "finger": "Index", "active": True, "depth": 0.5
        })
        self.assertEqual(result[0], "right")
        self.assertEqual(len(self.right.sent), 1)
        self.assertEqual(self.left.sent, [])

    def test_unknown_hand_dropped(self):
        result = self.router.handle_contact({
            "hand": "foot", "finger": "Index", "active": True, "depth": 0.5
        })
        self.assertIsNone(result)
        self.assertEqual(self.right.sent, [])
        self.assertEqual(self.left.sent,  [])

    def test_unknown_finger_dropped(self):
        result = self.router.handle_contact({
            "hand": "right", "finger": "Wrist", "active": True, "depth": 0.5
        })
        self.assertIsNone(result)
        self.assertEqual(self.right.sent, [])

    def test_all_off_hits_both(self):
        self.router.all_off()
        self.assertEqual(self.right.sent, ["ALLOFF"])
        self.assertEqual(self.left.sent,  ["ALLOFF"])


# ── JupiterRouter — single hand only ────────────────────────────────────────

class RouterRightOnlyTests(unittest.TestCase):
    """Single-PCB user has only right hand wired."""

    def setUp(self):
        self.right = FakeHandBridge("right")
        # Pass left=None to simulate "left port not configured at CLI"
        self.router = pc_bridge.JupiterRouter({"right": self.right, "left": None})

    def test_right_event_routes(self):
        self.router.handle_contact({
            "hand": "right", "finger": "Pinky", "active": True, "depth": 0.5
        })
        self.assertEqual(len(self.right.sent), 1)
        self.assertTrue(self.right.sent[0].startswith("C5I"))

    def test_left_event_dropped_silently_on_right(self):
        result = self.router.handle_contact({
            "hand": "left", "finger": "Index", "active": True, "depth": 0.5
        })
        self.assertIsNone(result)
        self.assertEqual(self.right.sent, [])  # nothing leaked to right

    def test_legacy_payload_still_works(self):
        self.router.handle_contact({
            "finger": "Thumb", "active": False, "depth": 0.0
        })
        self.assertEqual(self.right.sent, ["C1OFF"])


class RouterLeftOnlyTests(unittest.TestCase):
    """Setup with only the left PCB wired — verify symmetry."""

    def setUp(self):
        self.left = FakeHandBridge("left")
        self.router = pc_bridge.JupiterRouter({"right": None, "left": self.left})

    def test_left_event_routes(self):
        self.router.handle_contact({
            "hand": "left", "finger": "Index", "active": True, "depth": 1.0
        })
        self.assertEqual(self.left.sent, [f"C2I{pc_bridge.MIN_POT_VALUE}"])

    def test_right_event_dropped(self):
        # In this setup the legacy default of "right" means missing-hand
        # payloads ALSO get dropped — caller knows what they configured.
        self.router.handle_contact({
            "finger": "Thumb", "active": True, "depth": 0.5
        })
        self.assertEqual(self.left.sent, [])

    def test_no_bridges_raises(self):
        with self.assertRaises(ValueError):
            pc_bridge.JupiterRouter({"right": None, "left": None})


# ── End-to-end: simulated typing sequence ────────────────────────────────────

class TypingSequenceTest(unittest.TestCase):
    """Simulate a realistic typing burst. Both hands press several keys with
    overlapping timing; verify the per-Arduino command stream is exactly what
    you'd expect."""

    def test_two_handed_typing(self):
        right = FakeHandBridge("right")
        left  = FakeHandBridge("left")
        router = pc_bridge.JupiterRouter({"right": right, "left": left})

        # left index taps Q, right index taps P, simultaneously then released
        router.handle_contact({"hand": "left",  "finger": "Index", "active": True,  "depth": 0.4})
        router.handle_contact({"hand": "right", "finger": "Index", "active": True,  "depth": 0.4})
        router.handle_contact({"hand": "left",  "finger": "Index", "active": False, "depth": 0.0})
        router.handle_contact({"hand": "right", "finger": "Index", "active": False, "depth": 0.0})

        # left middle taps W (deeper), right thumb taps space
        router.handle_contact({"hand": "left",  "finger": "Middle", "active": True, "depth": 0.8})
        router.handle_contact({"hand": "right", "finger": "Thumb",  "active": True, "depth": 0.7})
        router.handle_contact({"hand": "left",  "finger": "Middle", "active": False, "depth": 0.0})
        router.handle_contact({"hand": "right", "finger": "Thumb",  "active": False, "depth": 0.0})

        # Each hand should have an independent, ordered command stream
        # consisting only of its own events.
        for cmd in right.sent:
            self.assertTrue(cmd.startswith("C1") or cmd.startswith("C2"),
                            f"right got unexpected channel: {cmd}")
        for cmd in left.sent:
            self.assertTrue(cmd.startswith("C2") or cmd.startswith("C3"),
                            f"left got unexpected channel: {cmd}")

        # Exact stream check.
        # depth_to_pot: 220 − int(d × 190).
        # 0.4 → 144,  0.7 → 87,  0.8 → 68.
        self.assertEqual(right.sent, [
            "C2I144",  # Index ON depth=0.4
            "C2OFF",
            "C1I87",   # Thumb ON depth=0.7
            "C1OFF",
        ])
        self.assertEqual(left.sent, [
            "C2I144",  # Index ON depth=0.4
            "C2OFF",
            "C3I68",   # Middle ON depth=0.8
            "C3OFF",
        ])


if __name__ == "__main__":
    unittest.main(verbosity=2)
