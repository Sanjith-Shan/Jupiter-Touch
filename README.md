# Jupiter Bridge

End-to-end system for per-finger EMS haptic feedback on Meta Quest 3.

```
[Meta Quest 3]  ──UDP/WiFi──▶  [PC Bridge]  ──USB Serial──▶  [Jupiter EMS Device]
  Hand tracking                  pc_bridge.py                   Arduino Nano
  Per-finger contact                                             6-ch EMS
```

## Repository layout

```
firmware/jupiter_bridge/   Arduino firmware (6-channel EMS controller)
bridge/pc_bridge.py        Python UDP→Serial bridge
unity/Assets/JupiterBridge Unity C# scripts (Meta XR SDK, hand tracking)
```

---

## 1 — Arduino Firmware

### Hardware

| Channel | Finger | Relay pins | AD5252 chip | Wiper |
|---------|--------|------------|-------------|-------|
| 1 | Thumb  | D2 / D3  | 0x2C | RDAC1 |
| 2 | Index  | D4 / D5  | 0x2C | RDAC3 |
| 3 | Middle | D6 / D7  | 0x2D | RDAC1 |
| 4 | Ring   | D8 / D9  | 0x2D | RDAC3 |
| 5 | Pinky  | D10 / D11| 0x2E | RDAC1 |
| 6 | Palm   | D12 / D13| 0x2E | RDAC3 |

### Serial protocol (19200 baud, `\n` terminated)

| Command | Effect |
|---------|--------|
| `C1I128\n` | Set Channel 1 (Thumb) to pot value 128 and activate relay |
| `C3I0\n`   | Set Channel 3 (Middle) to maximum stimulation and activate |
| `C2OFF\n`  | Deactivate Channel 2 (Index) — safe ramp before relay opens |
| `ALLOFF\n` | Emergency stop: deactivate all 6 channels |

Pot values: **0 = maximum stimulation**, **255 = minimum/off**.

Legacy single-char commands (`1`, `2`, `q`, `a`, `w`, `s`) still work for bench testing.

### Flash

Open `firmware/jupiter_bridge/jupiter_bridge.ino` in Arduino IDE, select **Arduino Nano**, flash.

---

## 2 — PC Bridge

```bash
cd bridge
pip install -r requirements.txt
python pc_bridge.py --port COM3          # Windows
python pc_bridge.py --port /dev/ttyUSB0  # Linux / macOS
```

The bridge listens on **UDP port 8053** and forwards contact events to the Arduino.

### UDP message format

```json
{"finger": "Index", "active": true,  "depth": 0.72}
{"finger": "Index", "active": false, "depth": 0.0}
```

`depth` is 0.0–1.0 (contact penetration normalised to `maxDepthMetres` in Unity).

---

## 3 — Unity (Meta Quest 3)

### Requirements

- Unity 2022.3 LTS or newer
- Meta XR SDK v50+ (install via Package Manager → My Asset Store)
- Build target: Android, XR Plug-in Management → Oculus

### Setup

1. Create a new Unity project and import Meta XR SDK.
2. Copy `unity/Assets/JupiterBridge/` into your project's `Assets/` folder.
3. Create a new layer called **EMS Contact** (Project Settings → Tags and Layers, slot 6).
4. Open a blank scene, add **OVRCameraRig** from the Meta XR SDK prefabs.
5. Create an empty GameObject `Jupiter`, add:
   - `UDPSender` — set **Bridge IP** to your PC's local IP, port 8053
   - `JupiterTestScene` — drag the right-hand `OVRSkeleton` from OVRCameraRig into the field
6. Build and deploy to Quest 3 (File → Build Settings → Android → Build and Run).

### Scripts

| Script | Purpose |
|--------|---------|
| `UDPSender.cs` | Singleton UDP client — sends JSON to PC bridge |
| `FingerContactDetector.cs` | Per-fingertip sphere trigger + penetration depth |
| `HandContactManager.cs` | Tracks all 6 fingers, drives UDP at 90 Hz |
| `JupiterTestScene.cs` | Procedurally builds the test scene (sphere + floor) |

---

## Data flow

```
OVRSkeleton bones
      │  (every frame, ~90 Hz)
      ▼
FingerContactDetector × 6
  Physics.ComputePenetration → depth 0.0–1.0
      │
      ▼
HandContactManager
  state change  →  immediate UDP packet   (low-latency relay on/off)
  60 Hz timer   →  continuous UDP packets (smooth intensity updates)
      │
      ▼  UDP JSON  (WiFi, LAN)
PC bridge (pc_bridge.py)
  depth → pot value via: pot = MAX_POT - depth × (MAX_POT - MIN_POT)
      │
      ▼  Serial "CxIy\n" / "CxOFF\n"  (19200 baud USB)
Arduino Nano
  AD5252 I2C + photo-relay
      │
      ▼
Jupiter EMS Device → fingers
```

---

## Safety notes

- The firmware always sets the pot to 255 (minimum output) **before** opening the relay on deactivation.
- `ALLOFF` can be sent at any time as an emergency stop.
- `MIN_POT_VALUE` in `pc_bridge.py` (default 30) limits maximum stimulation. Raise it for initial testing.
- Never flash new firmware while the device is energised.
