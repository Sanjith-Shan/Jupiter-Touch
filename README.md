# Jupiter Touch

**Per-finger haptic feedback for VR, through electrical muscle stimulation.**

Type on a virtual keyboard floating in mid-air, and feel every keystroke on the correct finger.
Reach out, grip a virtual phone, and feel its weight in your palm for as long as you hold it.
Both hands tracked. Both hands wired. Driven from a presenter's browser dashboard.

Built in 48 hours.

```
   ┌─────────────────────┐    UDP/WiFi     ┌──────────────────┐    USB Serial    ┌────────────────────┐
   │   Meta Quest 3 VR   │ ──────────────▶ │   PC bridge.py   │ ───────────────▶ │  EMS PCB ×2 hands  │
   │  hand tracking 90Hz │                 │ routes by "hand" │                  │ Arduino + AD5252×3 │
   │ per-finger contact  │                 │     to port      │                  │   6 channels each  │
   └─────────────────────┘                 └──────────────────┘                  └────────────────────┘
                                                                                          │
                                                                                          ▼
                                                                                   12 EMS channels
                                                                                   on the wearer's
                                                                                   fingers + palms
```

---

## The demo arc

A presenter at a laptop drives the headset wearer through the experience by clicking buttons on a browser dashboard. The wearer doesn't touch a keyboard, controller, or screen.

| Presenter clicks       | Wearer experiences                                                                    |
| ---------------------- | ------------------------------------------------------------------------------------- |
| **Subway**             | Fade to black, scene loads. Hand outlines render in passthrough.                      |
| **Spawn Monitors**     | Two floating monitors slide up in front of the wearer.                                |
| **Spawn Keyboard**     | A QWERTY keyboard rises to lap height, tilted toward the user.                        |
| _types "hello world"_  | Every keystroke fires EMS on the corresponding finger. Text appears on left monitor.  |
| **Spawn Phone**        | A virtual phone materializes 35 cm in front of the wearer's chest, screen toward them.|
| _reaches out, grips it_| 3+ fingers wrapping around the body → phone parents to that hand → continuous EMS.    |
| **Reset Subway**       | Everything fades out. Ready for the next person.                                      |

---

## What's special

**Per-finger differentiation.** Not one buzz on a controller. Five distinct EMS channels per hand — thumb, index, middle, ring, pinky — plus a palm channel. Press the `Q` key with your left index finger and it lights up channel 2 on the left PCB only.

**Both hands, fully symmetric.** Two identical PCBs, two USB cables, one bridge that routes commands to the correct Arduino by inspecting the `"hand"` field on each UDP packet. No configuration mismatch possible: the firmware itself is hand-agnostic.

**Mid-air typing that actually works.** Meta-style "primed-to-press" gating: a key only fires when a finger crosses its top surface from above with downward motion. Resting fingers below the keyboard plane never accidentally fire. Plus a hand-aware cooldown (80 ms same-hand, 25 ms cross-hand) that hard-caps to one keystroke at a time without limiting real typing speed.

**Continuous grip feedback.** When you wrap your fingers around the phone, every finger that's touching the body fires its own EMS channel at intensity proportional to grip depth. Steady grip → steady stim. Squeeze tighter → stronger stim. Open your hand → it stops.

**Director architecture for live demo control.** A presenter at a laptop runs the show in real time via a browser dashboard. Bootstrap scene stays loaded forever (so hand tracking and EMS pipeline never die); demo scenes load additively over the top with smooth fades.

---

## How a single keystroke flows through the system

```
  Wearer's finger pushes through the top surface of the 'A' key
                                │
                                ▼
   OVRSkeleton bone positions update (90 Hz)
                                │
                                ▼
   FingerContactDetector — Physics.ComputePenetration()
                                │
                                │ depth = penetration / 30 mm  (normalised 0..1)
                                ▼
   VirtualKey — primed-to-press gate + per-hand cooldown
                                │
                                │ if (entered from above) and (depth ≥ pressThreshold)
                                │ and (cooldown clear): FIRE
                                ▼
   KeyboardController — text buffer, broadcasts to monitors
                                │
                                ▼
   JupiterTester EMS loop (separate path, runs at 90 Hz independently)
                                │
                                │ {"hand":"left","finger":"Index","active":true,"depth":0.65}
                                ▼ UDP
   pc_bridge.py — routes to LEFT Arduino's serial port by "hand" field
                                │
                                │ pot = 220 − √depth × 190        ← sqrt curve for haptic feel
                                │ "C2I86\n"
                                ▼ 19200 baud serial
   Arduino firmware — parses CxIy, writes I²C to AD5252, closes photo-relay
                                │
                                ▼
   Wearer's left index finger feels it.
```

End-to-end latency is in the tens of milliseconds.

---

## Hardware

- **Headset**: Meta Quest 3, hand tracking enabled (no controllers needed)
- **Per hand**: 1× Arduino Nano, 3× AD5252 digital potentiometers on I²C, 6× opto-isolated photo-relay pairs, custom EMS driver PCB
- **Total**: 12 EMS channels across both hands, individually addressable
- **Power**: 9 V DC per PCB
- **Connections**: 2× USB cables to the host laptop (one per Arduino)

### Channel map (identical on both PCBs)

| Channel | Finger  | Relay pins | AD5252 | Wiper |
| :-----: | :------ | :--------- | :----- | :---- |
| 1       | Thumb   | D2 / D3    | 0x2C   | RDAC1 |
| 2       | Index   | D4 / D5    | 0x2C   | RDAC3 |
| 3       | Middle  | D6 / D7    | 0x2D   | RDAC1 |
| 4       | Ring    | D8 / D9    | 0x2D   | RDAC3 |
| 5       | Pinky   | D10 / D11  | 0x2E   | RDAC1 |
| 6       | Palm    | D12 / D13  | 0x2E   | RDAC3 |

Each Arduino is unaware of left vs. right — hand identity lives only at the bridge routing layer.

---

## Software stack

| Layer                      | Where it lives                                          | What it does                                              |
| :------------------------- | :------------------------------------------------------ | :-------------------------------------------------------- |
| Unity app on Quest 3       | `unity/Assets/JupiterBridge/`                           | Hand tracking, virtual widgets, director WebSocket client |
| Director server            | `bridge/director/director_server.py`                    | aiohttp WebSocket relay between dashboard and headset     |
| Browser dashboard          | `bridge/director/static/{index.html,director.js}`       | Presenter UI: scene buttons, event triggers, live log     |
| EMS bridge (UDP → serial)  | `bridge/pc_bridge.py`                                   | Routes per-hand JSON events to the correct Arduino port   |
| Firmware (×2 Arduinos)     | `firmware/jupiter_touch/jupiter_touch.ino`              | 6-channel EMS controller with serial protocol + safety    |

### Serial protocol (19200 baud, `\n` terminated)

| Command   | Effect                                                          |
| :-------- | :-------------------------------------------------------------- |
| `C1I128\n`| Set channel 1 to pot value 128, close relay                     |
| `C2OFF\n` | Deactivate channel 2 — ramp pot to 255 before opening relay     |
| `ALLOFF\n`| Emergency stop — all 6 channels off                             |
| `STATUS\n`| Query state of all channels                                     |

Pot semantics: **0 = max stim, 255 = min/off**. Bridge clamps to a safe range (default 30..220).

### UDP message format (Quest → bridge)

```json
{"hand": "right", "finger": "Index", "active": true,  "depth": 0.72}
{"hand": "left",  "finger": "Index", "active": false, "depth": 0.0}
```

`depth` is normalised 0..1 (penetration in metres ÷ 30 mm). The bridge applies a sqrt curve so light grip feels present, not weak. Missing `hand` field defaults to right for backwards compatibility.

---

## Safety

The system is designed to fail off, not on:

- **Firmware silence timeout**: if no command is received for 15 seconds while any channel is active, all channels auto-deactivate. Catches Quest crashes, cable disconnects, bridge crashes.
- **HMD unmount**: when the headset is lifted off, Unity dispatches `OnApplicationPause(true)` and the EMS layer immediately fires off events for all 12 channels — both Arduinos go quiet within ~50 ms.
- **Deactivation always ramps** the pot to 255 before opening the relay, so there's never an abrupt edge in stimulation.
- **Pot range clamp** in the bridge prevents the hardware from ever receiving a pot value below `MIN_POT_VALUE` (default 30, raise during initial testing).
- **All channels off on power-up**, every time.

---

## Quick start

### 1. Flash both Arduinos

```bash
# Open in Arduino IDE, select Arduino Nano, flash:
firmware/jupiter_touch/jupiter_touch.ino
```

The same firmware on both — no per-hand configuration.

### 2. Set up Unity

- Unity 2022.3 LTS or Unity 6
- Meta XR SDK installed via the Asset Store
- Build target: Android, XR Plug-in Management → Oculus / OpenXR
- Create layer 6 named **EMS Contact**
- Bootstrap scene with `OVRCameraRig`, `OVRHandPrefab_Right`, `OVRHandPrefab_Left`, plus a `HandTracker_Right` GameObject (with `JupiterTester`, handedness=Right, wired to right OVRSkeleton) and a `HandTracker_Left` (handedness=Left, wired to left OVRSkeleton)
- Add `DemoRoom_Subway.unity` with a `SubwaySceneController` GameObject
- File → Build And Run

### 3. Start the PC services

```bash
# Director server (presenter dashboard backend)
python3 bridge/director/director_server.py

# EMS bridge — both hands
python3 bridge/pc_bridge.py \
    --right-port /dev/cu.usbserial-XXXX \
    --left-port  /dev/cu.usbserial-YYYY
```

Open `http://localhost:8765` in Chrome → wait for green dot → click Subway → start spawning things.

### 4. Run the test suite

```bash
python3 bridge/test_pc_bridge.py
```

29 tests covering routing, hand isolation, command format vs. firmware parser, ALLOFF safety, HMD unmount path. Run anytime after touching either side of the wire.

---

## Roadmap

- **Subway environment** — the demo currently runs in passthrough; an Asset Store subway-car interior is the next visual layer.
- **Live phone screen content** — wire the phone display to the keyboard buffer so you can "text" while gripping.
- **Pickup gestures with smoothing** — Meta XR Interaction SDK PokeInteractor for grab/release with motion-based easing.
- **Mouse** — V2 stretch goal. 3D-to-2D cursor mapping with click gesture detection.
- **Per-Arduino hand-identity probe** — firmware-side `HAND` query so the bridge can verify physical port labelling at startup.

---

## Repository layout

```
firmware/jupiter_touch/        Arduino firmware (6-channel EMS controller, hand-agnostic)
bridge/director/               aiohttp server + browser dashboard
bridge/pc_bridge.py            UDP → serial router with per-hand dispatch
bridge/test_pc_bridge.py       29-test integration suite
unity/Assets/JupiterBridge/    Unity scripts (Meta XR SDK, hand tracking, widgets, director client)
```

The Unity *project* (scene files, settings, packages) is stored separately on the developer's Desktop for hackathon reasons. Scripts in this repo are kept in sync with that project; commit messages reference both.

---

## License

MIT.
