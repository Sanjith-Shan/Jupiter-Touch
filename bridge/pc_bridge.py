"""
Jupiter Touch — PC Serial Bridge
Receives UDP contact events from the Meta Quest 3 and translates them to
serial commands for the Jupiter Touch EMS device (Arduino Nano, 19200 baud).

Usage:
    python pc_bridge.py --port COM3          # Windows
    python pc_bridge.py --port /dev/ttyUSB0  # Linux
    python pc_bridge.py --port /dev/tty.usbserial-XXXX  # macOS

UDP message format (JSON, sent from Unity on Quest):
    {"finger": "Index", "active": true,  "depth": 0.72}
    {"finger": "Index", "active": false, "depth": 0.0}

Finger names: Thumb, Index, Middle, Ring, Pinky, Palm
Depth: 0.0–1.0 (0 = just touching, 1 = maximum penetration depth)

Serial commands sent to Arduino:
    CxIy\\n  — activate channel x at raw pot intensity y (0=max, 255=min)
    CxOFF\\n — deactivate channel x
"""

import argparse
import json
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

# Map finger name → EMS channel number (1-indexed, matches firmware)
FINGER_CHANNEL = {
    "Thumb":  1,
    "Index":  2,
    "Middle": 3,
    "Ring":   4,
    "Pinky":  5,
    "Palm":   6,
}

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
    """
    depth = max(0.0, min(1.0, depth))
    # Linear mapping from [0,1] → [MAX_POT_VALUE, MIN_POT_VALUE]
    pot = MAX_POT_VALUE - int(depth * (MAX_POT_VALUE - MIN_POT_VALUE))
    return pot


class JupiterTouch:
    def __init__(self, serial_port: str):
        self.serial_port = serial_port
        self._ser = None
        self._lock = threading.Lock()
        self._connect()

    def _connect(self):
        try:
            self._ser = serial.Serial(
                self.serial_port, BAUD_RATE, timeout=SERIAL_TIMEOUT
            )
            # Give the Arduino time to reset after DTR asserted
            time.sleep(2.0)
            # Flush any startup text from firmware
            self._ser.reset_input_buffer()
            print(f"[bridge] Connected to {self.serial_port} at {BAUD_RATE} baud")
        except serial.SerialException as e:
            print(f"[bridge] Serial error: {e}")
            self._ser = None

    def _send(self, command: str):
        """Send a newline-terminated command to the Arduino."""
        if self._ser is None or not self._ser.is_open:
            print(f"[bridge] Serial not available, dropping: {command!r}")
            return
        try:
            with self._lock:
                self._ser.write((command + "\n").encode("ascii"))
        except serial.SerialException as e:
            print(f"[bridge] Write error: {e}")

    def handle_contact(self, msg: dict):
        """Process one contact event from Unity."""
        finger = msg.get("finger", "")
        active = bool(msg.get("active", False))
        depth  = float(msg.get("depth", 0.0))

        channel = FINGER_CHANNEL.get(finger)
        if channel is None:
            print(f"[bridge] Unknown finger: {finger!r}")
            return

        if active:
            pot = depth_to_pot(depth)
            cmd = f"C{channel}I{pot}"
        else:
            cmd = f"C{channel}OFF"

        print(f"[bridge] {finger:6s} {'ON ' if active else 'OFF'} depth={depth:.2f} → {cmd}")
        self._send(cmd)

    def all_off(self):
        self._send("ALLOFF")

    def run_udp(self, host: str = UDP_LISTEN_IP, port: int = UDP_PORT):
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        sock.bind((host, port))
        print(f"[bridge] Listening on UDP {host}:{port}")

        try:
            while True:
                data, addr = sock.recvfrom(4096)
                try:
                    msg = json.loads(data.decode("utf-8"))
                    self.handle_contact(msg)
                except (json.JSONDecodeError, UnicodeDecodeError) as e:
                    print(f"[bridge] Bad packet from {addr}: {e}")
        except KeyboardInterrupt:
            print("\n[bridge] Shutting down — all channels off")
            self.all_off()
        finally:
            sock.close()
            if self._ser and self._ser.is_open:
                self._ser.close()


def main():
    parser = argparse.ArgumentParser(description="Jupiter Touch PC serial bridge")
    parser.add_argument("--port", required=True, help="Arduino serial port (e.g. COM3 or /dev/ttyUSB0)")
    parser.add_argument("--udp-port", type=int, default=UDP_PORT, help=f"UDP listen port (default {UDP_PORT})")
    args = parser.parse_args()

    bridge = JupiterTouch(args.port)
    bridge.run_udp(port=args.udp_port)


if __name__ == "__main__":
    main()
