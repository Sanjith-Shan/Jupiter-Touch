"""
Jupiter Touch — PC Serial Bridge (dual-hand)

Receives UDP contact events from the Meta Quest 3 (Unity) and translates them
to serial commands for one or two Arduino-Nano-driven EMS PCBs — one per hand.

Each Arduino is identical hardware running identical firmware and exposes 6
EMS channels (Thumb / Index / Middle / Ring / Pinky / Palm). The bridge
multiplexes incoming UDP events to the correct serial port based on the
"hand" field in each payload.

Usage:
    # Both hands (typical)
    python pc_bridge.py --right-port /dev/cu.usbserial-RIGHT \\
                        --left-port  /dev/cu.usbserial-LEFT

    # Right hand only (single PCB)
    python pc_bridge.py --right-port /dev/cu.usbserial-RIGHT

    # Legacy single-port form (treated as right hand)
    python pc_bridge.py --port COM3

UDP message format (JSON, sent from Unity on Quest):
    {"hand": "right", "finger": "Index", "active": true,  "depth": 0.72}
    {"hand": "left",  "finger": "Index", "active": false, "depth": 0.0}

Backwards compatibility:
    Payloads missing the "hand" field are routed to the right Arduino, so
    older Unity builds keep working unchanged.

Finger names: Thumb, Index, Middle, Ring, Pinky, Palm
Depth: 0.0–1.0 (0 = just touching, 1 = maximum penetration depth)

Serial commands sent to each Arduino:
    CxIy\\n  — activate channel x at raw pot intensity y (0=max, 255=min)
    CxOFF\\n — deactivate channel x
    ALLOFF\\n — emergency stop, all channels off
"""

import argparse
import json
import math
import socket
import sys
import time
import threading

try:
    import serial
except ImportError:
    print("Missing dependency: pip install pyserial")
    sys.exit(1)

# ── Config ────────────────────────────────────────────────────────────────────
UDP_LISTEN_IP   = "0.0.0.0"
UDP_PORT        = 8053
BAUD_RATE       = 19200
SERIAL_TIMEOUT  = 1.0

# Map finger name → EMS channel number (1-indexed, matches firmware).
# Same mapping for both hands — each Arduino's firmware controls its own
# 6 channels and is unaware of left-vs-right.
FINGER_CHANNEL = {
    "Thumb":  1,
    "Index":  2,
    "Middle": 3,
    "Ring":   4,
    "Pinky":  5,
    "Palm":   6,
}

VALID_HANDS = ("right", "left")

# Safety clamp: never send a pot value below this (0 = absolute max stim).
# Raise this during initial testing to limit maximum intensity.
MIN_POT_VALUE = 30   # corresponds to near-maximum stimulation
MAX_POT_VALUE = 220  # near-off (but relay still open)


def depth_to_pot(depth: float) -> int:
    """
    Convert normalised contact depth (0.0–1.0) to a raw AD5252 pot value.

    Pot semantics: 0 = maximum EMS, 255 = minimum EMS.
    At depth 0 (just touching) we want low stimulation (high pot value).
    At depth 1 (full contact) we want high stimulation (low pot value).

    The mapping uses a sqrt curve, NOT a linear one. Realistic "holding"
    gestures (e.g. wrapping fingers around a phone body 8 mm thick) only
    produce contact depths around 0.2–0.4. Linear mapping in that range
    yields pot values 145–180 — felt as a barely-perceptible buzz. The
    sqrt curve boosts low-mid depths into the 110–135 range (clearly
    present) without changing the maximum: deep presses still saturate
    at MIN_POT_VALUE.
    """
    depth = max(0.0, min(1.0, depth))
    curved = math.sqrt(depth)
    pot = MAX_POT_VALUE - int(curved * (MAX_POT_VALUE - MIN_POT_VALUE))
    return pot


def build_command(finger: str, active: bool, depth: float):
    """Pure helper — turns a contact event into a serial command string,
    or None if the finger name is unknown. Side-effect-free so the unit
    tests can exercise it without touching real serial ports."""
    channel = FINGER_CHANNEL.get(finger)
    if channel is None:
        return None
    if active:
        pot = depth_to_pot(depth)
        return f"C{channel}I{pot}"
    return f"C{channel}OFF"


# ── Per-Arduino serial endpoint ──────────────────────────────────────────────

class HandBridge:
    """One serial connection to one Arduino. Stateless beyond the open port."""

    def __init__(self, name: str, serial_port: str, *, open_serial: bool = True):
        self.name = name           # "right" or "left"
        self.serial_port = serial_port
        self._ser = None
        self._lock = threading.Lock()
        if open_serial:
            self._connect()

    def _connect(self):
        try:
            self._ser = serial.Serial(
                self.serial_port, BAUD_RATE, timeout=SERIAL_TIMEOUT
            )
            # Give the Arduino time to reset after DTR asserted
            time.sleep(2.0)
            self._ser.reset_input_buffer()
            print(f"[bridge:{self.name}] Connected to {self.serial_port} at {BAUD_RATE} baud")
        except serial.SerialException as e:
            print(f"[bridge:{self.name}] Serial error on {self.serial_port}: {e}")
            self._ser = None

    def send_command(self, command: str):
        """Send a newline-terminated command. Drops if serial isn't open."""
        if self._ser is None or not self._ser.is_open:
            print(f"[bridge:{self.name}] Serial not available, dropping: {command!r}")
            return
        try:
            with self._lock:
                self._ser.write((command + "\n").encode("ascii"))
        except serial.SerialException as e:
            print(f"[bridge:{self.name}] Write error: {e}")

    def all_off(self):
        self.send_command("ALLOFF")

    def close(self):
        try:
            if self._ser and self._ser.is_open:
                self._ser.close()
        except Exception:
            pass


# ── Multi-hand router ────────────────────────────────────────────────────────

class JupiterRouter:
    """Owns one HandBridge per configured hand and dispatches incoming UDP
    contact events to the matching Arduino based on the "hand" field."""

    def __init__(self, bridges: dict):
        # bridges: {"right": HandBridge | None, "left": HandBridge | None}
        # Either or both may be present. Drop any None values for clarity.
        self.bridges = {h: b for h, b in bridges.items() if b is not None}
        if not self.bridges:
            raise ValueError("JupiterRouter requires at least one HandBridge")

    def handle_contact(self, msg: dict):
        """Process one contact event from Unity. Returns the (bridge_name, cmd)
        tuple it routed to, or None if the message couldn't be routed.
        Returning a value (instead of just logging + acting) keeps the unit
        tests simple."""
        # Default to "right" for backwards compatibility with old Unity builds
        # that don't include the "hand" field.
        hand = msg.get("hand", "right")
        if hand not in VALID_HANDS:
            print(f"[router] Unknown hand: {hand!r} — dropping {msg}")
            return None

        bridge = self.bridges.get(hand)
        if bridge is None:
            print(f"[router] No Arduino configured for hand={hand!r} — dropping {msg.get('finger')}")
            return None

        finger = msg.get("finger", "")
        active = bool(msg.get("active", False))
        depth  = float(msg.get("depth", 0.0))

        cmd = build_command(finger, active, depth)
        if cmd is None:
            print(f"[router] Unknown finger: {finger!r}")
            return None

        print(f"[router:{hand:5s}] {finger:6s} {'ON ' if active else 'OFF'} depth={depth:.2f} → {cmd}")
        bridge.send_command(cmd)
        return (hand, cmd)

    def all_off(self):
        for b in self.bridges.values():
            b.all_off()

    def close_all(self):
        for b in self.bridges.values():
            b.close()


# ── UDP server loop ──────────────────────────────────────────────────────────

def run_udp(router: JupiterRouter, host: str = UDP_LISTEN_IP, port: int = UDP_PORT):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind((host, port))
    print(f"[bridge] Listening on UDP {host}:{port}")
    print(f"[bridge] Configured hands: {sorted(router.bridges.keys())}")

    try:
        while True:
            data, addr = sock.recvfrom(4096)
            try:
                msg = json.loads(data.decode("utf-8"))
                router.handle_contact(msg)
            except (json.JSONDecodeError, UnicodeDecodeError) as e:
                print(f"[bridge] Bad packet from {addr}: {e}")
    except KeyboardInterrupt:
        print("\n[bridge] Shutting down — all channels off")
        router.all_off()
    finally:
        sock.close()
        router.close_all()


# ── CLI ──────────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="Jupiter Touch PC serial bridge (dual-hand)")
    parser.add_argument("--right-port", help="Serial port for the RIGHT hand Arduino")
    parser.add_argument("--left-port",  help="Serial port for the LEFT hand Arduino")
    parser.add_argument("--port",       help="(Legacy) single Arduino serial port — treated as right hand")
    parser.add_argument("--udp-port",   type=int, default=UDP_PORT,
                        help=f"UDP listen port (default {UDP_PORT})")
    args = parser.parse_args()

    right_port = args.right_port or args.port
    left_port  = args.left_port

    if not right_port and not left_port:
        parser.error("Provide at least one of --right-port / --left-port (or legacy --port)")

    bridges = {
        "right": HandBridge("right", right_port) if right_port else None,
        "left":  HandBridge("left",  left_port)  if left_port  else None,
    }
    router = JupiterRouter(bridges)
    run_udp(router, port=args.udp_port)


if __name__ == "__main__":
    main()
