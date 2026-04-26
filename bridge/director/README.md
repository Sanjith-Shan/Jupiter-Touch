# Jupiter Bridge — Director System (Milestone 1)

Gives a presenter on a PC:
1. A **live mirror** of the Quest 3 headset view embedded in a browser tab.
2. **Scene-switching control** — click a button, the headset user's world changes.

```
[Browser tab on PC]
   ├─ left panel: Quest live view (via ws-scrcpy iframe)
   └─ right panel: scene buttons + log
         │  WebSocket ws://localhost:8765/ws/director
         ▼
[director_server.py] ←──────────────────────────────── /ws/quest ── [Unity app on Quest]
```

---

## Prerequisites

| What | Install |
|------|---------|
| Python 3.10+ | `brew install python` / python.org |
| Node.js 18+ | `brew install node` / nodejs.org |
| ADB | `brew install android-platform-tools` |
| scrcpy | `brew install scrcpy` |
| ws-scrcpy | `git clone https://github.com/NetrisTV/ws-scrcpy && cd ws-scrcpy && npm install` |

---

## One-time device setup

1. **Enable Developer Mode** on Quest 3 (Meta Quest mobile app → Devices → Developer Mode).
2. Connect Quest via USB-C. Accept the "Allow USB debugging" prompt in the headset.
3. Verify ADB sees it:
   ```
   adb devices
   ```
4. Switch ADB to Wi-Fi (do this once per session, or after every Quest reboot):
   ```
   adb tcpip 5555
   ```
   Unplug the USB cable. Find the Quest's IP in: **Settings → Wi-Fi → (tap the connected network)**.
5. Connect wirelessly:
   ```
   adb connect <quest-ip>:5555
   adb devices   # should show <quest-ip>:5555 device
   ```

---

## Running the director

### Terminal 1 — director server
```bash
cd bridge/director
pip install -r requirements.txt
python director_server.py
# → http://localhost:8765
```

### Terminal 2 — ws-scrcpy (video mirror)
```bash
cd ws-scrcpy
npm start
# → http://localhost:8000
```

### Terminal 3 — verify Quest connection (optional)
```bash
adb logcat -s Unity
# look for: [DirectorClient] Connected to ws://...
```

### Browser
Open **http://localhost:8765** in Chrome.

In the **ws-scrcpy URL** field at the top-right, enter `http://localhost:8000` and click **Connect**.
The Quest mirror appears on the left.

---

## Unity setup (one-time)

### 1. Install NativeWebSocket
In Unity → **Window → Package Manager → + → Add package from git URL**:
```
https://github.com/endel/NativeWebSocket.git#upm
```

### 2. Create a DirectorConfig asset
In **Project window → right-click → Create → JupiterBridge → DirectorConfig**.
Set **PC IP** to your laptop's LAN IP (find it with `ipconfig getifaddr en0` on Mac).

### 3. Build the Bootstrap scene
Create a new scene called `Bootstrap`. Add:
- **OVRCameraRig** (from Meta XR SDK prefabs)
- Empty GameObject `Director` with three components:
  - `DirectorClient` — assign the `DirectorConfig` asset
  - `DirectorRouter` — (no fields needed)
  - `SceneLoader` — assign the fade panel (see step 4)

### 4. Fade panel
Inside `Bootstrap`:
- Add a **Canvas** (Render Mode: Screen Space - Overlay, Sort Order: 999)
- Add a full-screen **Image** (color: black, stretch to fill)
- Add a **CanvasGroup** to the Image
- Assign that CanvasGroup to `SceneLoader → Fade Panel`
- Disable the Image object by default (SceneLoader enables/disables it during transitions)

### 5. Stub scenes
Create two scenes: `DemoRoom_Red` and `DemoRoom_Blue`.
Each needs only a camera (inherited from Bootstrap via additive loading) and some colored geometry or skybox so they're visually distinct. Add a **TextMeshPro** label ("RED ROOM" / "BLUE ROOM") floating in front so it's obvious which scene is loaded.

### 6. Build settings
**File → Build Settings → Scenes in Build**:
```
Bootstrap       (index 0)
DemoRoom_Red    (index 1)
DemoRoom_Blue   (index 2)
```
**Player Settings → Android → Other Settings → Internet Access = Require**

Build and deploy to Quest 3.

---

## How it works

| You do | What happens |
|--------|-------------|
| Click "Red Room" in the browser | Browser → WS → `director_server.py` → WS → `DirectorClient.cs` on Quest |
| | `DirectorRouter` dispatches `scene.load` message |
| | `SceneLoader` fades to black, unloads old scene, loads `DemoRoom_Red` additively, fades back in |
| | Quest sends `{"type":"ack","of":"scene.load","ok":true}` back to browser |
| | Browser log shows the ack |

---

## Adding new scenes (milestone 2+)

1. Create the scene in Unity, add it to Build Settings.
2. In `bridge/director/static/director.js`, add an entry to the `SCENES` array:
   ```js
   { name: "Sidewalk", label: "Sidewalk — grab the phone", fade: 0.6 },
   ```
3. Reload the browser tab. No server restart, no APK rebuild needed for just adding a scene.

---

## Keyboard shortcuts (browser)

| Key | Action |
|-----|--------|
| `1` | Load first scene |
| `2` | Load second scene |
| `3`–`9` | Additional scenes |

---

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| `adb devices` shows nothing | Re-run `adb connect <quest-ip>:5555`. Quest IP changes if it re-joined Wi-Fi. |
| ws-scrcpy shows blank / "device not found" | Confirm `adb devices` shows the Quest. Restart ws-scrcpy. |
| Quest app not connecting to server | Check PC IP in `DirectorConfig`. Check firewall allows port 8765. Check Unity Player Settings → Internet Access = Require. |
| Black flash instead of fade | Ensure the fade panel CanvasGroup is assigned in SceneLoader inspector. |
| scrcpy broken after Horizon OS update | Temporarily use MQDH Cast (separate window) while scrcpy compatibility is patched; change the iframe src to point at MQDH's window via OBS Virtual Camera. |
