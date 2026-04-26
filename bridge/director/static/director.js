/**
 * Jupiter Touch — Director Console JS
 *
 * Connects to the local director_server.py via WebSocket and provides:
 *   - Scene switching buttons
 *   - Quest connection status
 *   - Live log of sent/received messages
 *   - ws-scrcpy iframe embed
 */

// ── Scene list ─────────────────────────────────────────────────────────────
// Add new demo scenes here — no server changes required.
const SCENES = [
  { name: "DemoRoom_Red",    label: "Red Room",        fade: 0.4 },
  { name: "DemoRoom_Blue",   label: "Blue Room",       fade: 0.4 },
  { name: "DemoRoom_Subway", label: "Subway",          fade: 0.6 },
];

// ── Event triggers (for in-scene events: spawn objects, advance script, etc.)
// Each event sends {"type":"event.trigger","id":"<id>"} to the Quest.
const EVENTS = [
  { id: "spawn_monitors", label: "Spawn Monitors",  scope: "DemoRoom_Subway" },
  { id: "spawn_keyboard", label: "Spawn Keyboard",  scope: "DemoRoom_Subway" },
  { id: "reset_subway",   label: "Reset Subway",    scope: "DemoRoom_Subway", danger: true },
];

// ── State ──────────────────────────────────────────────────────────────────
let ws = null;
let questConnected = false;
let activeScene    = null;
let reconnectDelay = 1000;
const WS_URL = `ws://${location.hostname}:${location.port}/ws/director`;

// ── DOM refs ───────────────────────────────────────────────────────────────
const statusDot   = document.getElementById("status-dot");
const statusLabel = document.getElementById("status-label");
const logEl       = document.getElementById("log");
const videoPaneEl = document.getElementById("video-pane");
const scrcpyInput = document.getElementById("scrcpy-url");
const btnMirror   = document.getElementById("btn-load-mirror");
const sceneBtns   = document.getElementById("scene-buttons");
const eventBtns   = document.getElementById("event-buttons");

// ── Logger ─────────────────────────────────────────────────────────────────
function appendLog(text, cls = "system") {
  const now = new Date().toLocaleTimeString("en-US", { hour12: false });
  const el  = document.createElement("div");
  el.className = `log-entry ${cls}`;
  el.textContent = `${now}  ${text}`;
  logEl.appendChild(el);
  logEl.scrollTop = logEl.scrollHeight;

  // Keep at most 200 entries
  while (logEl.childElementCount > 200) logEl.firstChild.remove();
}

// ── WebSocket ──────────────────────────────────────────────────────────────
function connect() {
  setStatus("connecting");
  ws = new WebSocket(WS_URL);

  ws.onopen = () => {
    reconnectDelay = 1000;
    appendLog("Connected to director server", "system");
    setStatus("server-only");
  };

  ws.onmessage = (ev) => {
    let msg;
    try { msg = JSON.parse(ev.data); } catch { return; }

    switch (msg.type) {
      case "server.status":
        questConnected = msg.quest_connected;
        setStatus(questConnected ? "full" : "server-only");
        appendLog(`Server status: Quest ${questConnected ? "online" : "offline"}`, "system");
        break;

      case "quest.connected":
        questConnected = true;
        setStatus("full");
        appendLog("Quest connected", "system");
        updateSceneButtons();
        updateEventButtons();
        break;

      case "quest.disconnected":
        questConnected = false;
        setStatus("server-only");
        appendLog("Quest disconnected", "system");
        updateSceneButtons();
        updateEventButtons();
        break;

      case "ack":
        appendLog(`ack  ← ${msg.of}  ok=${msg.ok}  t=${msg.t_ms}ms`, "recv");
        if (msg.of === "scene.load" && msg.ok) {
          // server already tracks activeScene, just update the UI
        }
        break;

      case "telemetry":
        appendLog(`telemetry ← fps=${msg.fps?.toFixed(1)}  scene=${msg.scene}`, "recv");
        break;

      default:
        appendLog(`recv ← ${JSON.stringify(msg)}`, "recv");
    }
  };

  ws.onclose = () => {
    questConnected = false;
    setStatus("disconnected");
    appendLog(`Server connection lost — retrying in ${reconnectDelay / 1000}s`, "error");
    setTimeout(connect, reconnectDelay);
    reconnectDelay = Math.min(reconnectDelay * 2, 16000);
    updateSceneButtons();
    updateEventButtons();
  };

  ws.onerror = () => {
    appendLog("WebSocket error", "error");
  };
}

function send(msg) {
  if (!ws || ws.readyState !== WebSocket.OPEN) {
    appendLog("Cannot send — not connected to server", "error");
    return;
  }
  ws.send(JSON.stringify(msg));
  appendLog(`send → ${JSON.stringify(msg)}`, "sent");
}

// ── Status indicator ───────────────────────────────────────────────────────
function setStatus(state) {
  statusDot.className = "";
  switch (state) {
    case "connecting":
      statusDot.classList.add("connecting");
      statusLabel.textContent = "Connecting to server…";
      break;
    case "server-only":
      statusDot.classList.add("connecting");
      statusLabel.textContent = "Server connected — waiting for Quest…";
      break;
    case "full":
      statusDot.classList.add("connected");
      statusLabel.textContent = "Quest connected ✓";
      break;
    case "disconnected":
      statusLabel.textContent = "Disconnected";
      break;
  }
}

// ── Scene buttons ──────────────────────────────────────────────────────────
function buildSceneButtons() {
  sceneBtns.innerHTML = "";
  SCENES.forEach((scene, idx) => {
    const btn = document.createElement("button");
    btn.className = "scene-btn";
    btn.dataset.name = scene.name;
    btn.innerHTML = `
      <span>${scene.label}</span>
      <span class="badge">${idx + 1}</span>
    `;
    btn.addEventListener("click", () => loadScene(scene));
    sceneBtns.appendChild(btn);
  });
}

function updateSceneButtons() {
  const btns = sceneBtns.querySelectorAll(".scene-btn");
  btns.forEach(btn => {
    // CSS class only — never set the HTML `disabled` attribute. Disabled
    // buttons swallow click events silently, which makes "nothing happens
    // when I click" impossible to diagnose. Click handler does the gating
    // and logs a reason so the user always sees feedback.
    btn.classList.toggle("is-disabled", !questConnected);
    btn.classList.toggle("active", btn.dataset.name === activeScene);
  });
}

function loadScene(scene) {
  if (!questConnected) {
    appendLog(`Cannot load ${scene.name} — Quest not connected (status dot must be green)`, "error");
    return;
  }
  send({ type: "scene.load", name: scene.name, fade: scene.fade });
  activeScene = scene.name;
  updateSceneButtons();
  updateEventButtons();
}

// ── Event buttons ──────────────────────────────────────────────────────────
function buildEventButtons() {
  eventBtns.innerHTML = "";
  EVENTS.forEach(ev => {
    const btn = document.createElement("button");
    btn.className = "event-btn" + (ev.danger ? " danger" : "");
    btn.dataset.id = ev.id;
    btn.dataset.scope = ev.scope || "";
    btn.textContent = ev.label;
    btn.addEventListener("click", () => triggerEvent(ev));
    eventBtns.appendChild(btn);
  });
}

function updateEventButtons() {
  const btns = eventBtns.querySelectorAll(".event-btn");
  btns.forEach(btn => {
    const scope = btn.dataset.scope;
    const inScope = !scope || scope === activeScene;
    // Same as scene buttons — class only, no HTML `disabled` attribute.
    btn.classList.toggle("is-disabled", !questConnected || !inScope);
  });
}

function triggerEvent(ev) {
  if (!questConnected) {
    appendLog(`Cannot trigger ${ev.id} — Quest not connected`, "error");
    return;
  }
  if (ev.scope && ev.scope !== activeScene) {
    appendLog(`Cannot trigger ${ev.id} — current scene is ${activeScene || "(none)"}, requires ${ev.scope}. Click ${ev.scope} first.`, "error");
    return;
  }
  send({ type: "event.trigger", id: ev.id });
}

// ── Mirror (ws-scrcpy embed) ───────────────────────────────────────────────
function loadMirror(url) {
  const trimmed = url.trim();
  if (!trimmed) return;

  // Remove existing iframe or placeholder
  const existing = videoPaneEl.querySelector("iframe");
  const placeholder = videoPaneEl.querySelector("#video-placeholder");
  if (existing)    existing.remove();
  if (placeholder) placeholder.remove();

  const iframe = document.createElement("iframe");
  iframe.src = trimmed;
  iframe.allow = "autoplay; fullscreen";
  videoPaneEl.appendChild(iframe);
  appendLog(`Mirror iframe → ${trimmed}`, "system");

  // Persist across page reloads
  localStorage.setItem("scrcpy-url", trimmed);
}

btnMirror.addEventListener("click", () => loadMirror(scrcpyInput.value));
scrcpyInput.addEventListener("keydown", (e) => { if (e.key === "Enter") loadMirror(scrcpyInput.value); });

// Restore saved URL
const savedUrl = localStorage.getItem("scrcpy-url");
if (savedUrl) {
  scrcpyInput.value = savedUrl;
  loadMirror(savedUrl);
}

// ── Keyboard shortcuts ─────────────────────────────────────────────────────
// Press 1–9 to trigger the corresponding scene
document.addEventListener("keydown", (e) => {
  if (e.target.tagName === "INPUT") return;
  const idx = parseInt(e.key) - 1;
  if (idx >= 0 && idx < SCENES.length) loadScene(SCENES[idx]);
});

// ── Boot ───────────────────────────────────────────────────────────────────
buildSceneButtons();
buildEventButtons();
updateSceneButtons();
updateEventButtons();
connect();
