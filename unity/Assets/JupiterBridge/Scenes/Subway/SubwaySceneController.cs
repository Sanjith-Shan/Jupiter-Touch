using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using JupiterBridge.Director;

namespace JupiterBridge.Subway
{
    /// <summary>
    /// Lives in the DemoRoom_Subway scene root. Subscribes to director event
    /// triggers and spawns / despawns the keyboard and monitors on cue.
    ///
    /// Event IDs (sent from the dashboard as {"type":"event.trigger","id":"..."}):
    ///   spawn_monitors  → two VirtualMonitors materialise in front of the user
    ///   spawn_keyboard  → one VirtualKeyboard materialises at typing height
    ///   reset_subway    → destroy everything spawned, clear text buffer
    ///
    /// All physical placement and animation tuning lives in
    /// <see cref="JupiterTouchSizing"/> per the centralized-sizing rule.
    /// </summary>
    public class SubwaySceneController : MonoBehaviour
    {
        readonly List<GameObject> _spawned = new List<GameObject>();

        // Cached at scene-load and re-captured before every spawn so layout
        // tracks where the user actually is when the presenter clicks.
        Vector3 _seatPos;
        Vector3 _seatFwd;
        Vector3 _seatRight;

        void OnEnable()
        {
            DirectorRouter.OnEventTrigger += HandleEvent;
        }

        void OnDisable()
        {
            DirectorRouter.OnEventTrigger -= HandleEvent;
            DespawnAll();
        }

        void Start()
        {
            CaptureSeatFrame();

            // Ensure KeyboardController exists immediately so monitors spawned
            // BEFORE the keyboard can still subscribe and display incoming text.
            if (KeyboardController.Instance == null)
                gameObject.AddComponent<KeyboardController>();
        }

        void CaptureSeatFrame()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                _seatPos = cam.transform.position;
                _seatFwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                if (_seatFwd.sqrMagnitude < 0.01f) _seatFwd = Vector3.forward;
                // Cross(up, fwd) is the user's right in Unity's left-handed coords.
                _seatRight = Vector3.Cross(Vector3.up, _seatFwd).normalized;
            }
            else
            {
                _seatPos   = new Vector3(0, 1.4f, 0);
                _seatFwd   = Vector3.forward;
                _seatRight = Vector3.right;
            }
        }

        void HandleEvent(DirectorRouter.EventTriggerMsg msg)
        {
            switch (msg.id)
            {
                case "spawn_monitors": SpawnMonitors(); break;
                case "spawn_keyboard": SpawnKeyboard(); break;
                case "spawn_phone":    SpawnPhone();    break;
                case "reset_subway":   ResetSubway();   break;
                default:
                    Debug.Log($"[SubwaySceneController] Unhandled event: {msg.id}");
                    break;
            }
        }

        // ── Spawners ─────────────────────────────────────────────────────

        void SpawnMonitors()
        {
            if (HasSpawned("Monitor_L") || HasSpawned("Monitor_R"))
            {
                Debug.Log("[SubwaySceneController] Monitors already spawned");
                return;
            }

            CaptureSeatFrame();

            Vector3 center = _seatPos
                + _seatFwd * JupiterTouchSizing.MonitorSpawnDistanceM
                + Vector3.up * JupiterTouchSizing.MonitorSpawnHeightM;

            float halfSpacing = JupiterTouchSizing.MonitorSpawnSpacingM * 0.5f;

            var left  = SpawnMonitor("Monitor_L", center - _seatRight * halfSpacing);
            var right = SpawnMonitor("Monitor_R", center + _seatRight * halfSpacing);

            // Right monitor: prefilled with a static "code editor" placeholder
            // so the demo reads as "I'm coding on the subway".
            var vm = right.GetComponent<VirtualMonitor>();
            vm.staticText =
                "// jupiter_touch — main.cs\n" +
                "void Start() {\n" +
                "    HandTracker.Instance.Begin();\n" +
                "    EMS.Connect(\"COM3\");\n" +
                "}";
        }

        GameObject SpawnMonitor(string name, Vector3 worldPos)
        {
            var go = new GameObject(name);
            go.transform.position = worldPos;

            // Face the user. VirtualMonitor's screen face is local +Z (verified
            // in VirtualMonitor.AnchorToCamera which uses LookRotation(-camFwd)).
            // toUser = seatPos - worldPos points from monitor to user.
            // LookRotation(toUser) makes local +Z point at the user. ✓
            Vector3 toUser = _seatPos - worldPos;
            toUser.y = 0;
            if (toUser.sqrMagnitude > 0.001f)
                go.transform.rotation = Quaternion.LookRotation(toUser.normalized, Vector3.up);

            var vm = go.AddComponent<VirtualMonitor>();
            // Critical: prevent VirtualMonitor.Start from overwriting our placement
            // with its own AnchorToCamera call.
            vm.autoAnchorToCamera = false;

            _spawned.Add(go);
            StartCoroutine(SlideInFade(go.transform, worldPos));

            Debug.Log($"[SubwaySceneController] Spawned {name} @ {worldPos}");
            return go;
        }

        void SpawnKeyboard()
        {
            if (HasSpawned("Keyboard"))
            {
                Debug.Log("[SubwaySceneController] Keyboard already spawned");
                return;
            }

            CaptureSeatFrame();

            Vector3 pos = _seatPos
                + _seatFwd * JupiterTouchSizing.KeyboardSpawnDistanceM
                + Vector3.up * JupiterTouchSizing.KeyboardSpawnDropM;

            var go = new GameObject("Keyboard");
            go.transform.position = pos;
            // Keyboard front edge tilts toward the user. KeyboardTiltDegrees is
            // positive in JupiterTouchSizing; the convention (matching
            // VirtualKeyboard.AnchorToCamera) is to apply it as -Euler.x.
            go.transform.rotation = Quaternion.LookRotation(_seatFwd, Vector3.up)
                                  * Quaternion.Euler(-JupiterTouchSizing.KeyboardTiltDegrees, 0f, 0f);

            var kbd = go.AddComponent<VirtualKeyboard>();
            kbd.autoAnchorToCamera = false;

            _spawned.Add(go);
            StartCoroutine(SlideInFade(go.transform, pos));

            Debug.Log($"[SubwaySceneController] Spawned Keyboard @ {pos}");
        }

        void SpawnPhone()
        {
            if (HasSpawned("Phone"))
            {
                Debug.Log("[SubwaySceneController] Phone already spawned");
                return;
            }

            CaptureSeatFrame();

            // Phone spawns FLOATING in front of the user. They reach out and
            // physically grab it with ≥3 fingers wrapping around the body.
            // Position is set before AddComponent so the BoxCollider that
            // VirtualPhone configures in Awake is already in the right place
            // — avoids spurious triggers at world origin.
            Vector3 pos = _seatPos
                + _seatFwd * JupiterTouchSizing.PhoneSpawnDistanceM
                + Vector3.up * JupiterTouchSizing.PhoneSpawnDropM;

            var go = new GameObject("Phone");
            go.transform.position = pos;
            // Screen faces the user. VirtualPhone builds its screen on local
            // +Z, so local +Z must point toward the user — i.e. opposite the
            // user's forward.
            go.transform.rotation = Quaternion.LookRotation(-_seatFwd, Vector3.up);

            go.AddComponent<VirtualPhone>();
            _spawned.Add(go);

            Debug.Log($"[SubwaySceneController] Spawned Phone @ {pos} (floating; reach out to grab)");
        }

        void ResetSubway()
        {
            DespawnAll();
            if (KeyboardController.Instance != null)
                KeyboardController.Instance.Clear();
            Debug.Log("[SubwaySceneController] Reset complete");
        }

        bool HasSpawned(string name)
        {
            foreach (var go in _spawned)
                if (go != null && go.name == name) return true;
            return false;
        }

        void DespawnAll()
        {
            foreach (var go in _spawned)
                if (go != null) Destroy(go);
            _spawned.Clear();
            Debug.Log("[SubwaySceneController] Despawned all");
        }

        // ── Fade-in animation ────────────────────────────────────────────

        /// <summary>
        /// Slides the transform from <paramref name="finalPos"/> + downward offset
        /// up to <paramref name="finalPos"/> over SpawnFadeDurationS with an
        /// ease-out cubic curve. Translation-only — root scale stays 1.0 so the
        /// keyboard's lossyScale-based label compensation stays correct.
        /// </summary>
        IEnumerator SlideInFade(Transform t, Vector3 finalPos)
        {
            if (t == null) yield break;

            Vector3 startPos = finalPos - Vector3.up * JupiterTouchSizing.SpawnRiseM;
            t.position = startPos;

            float duration = JupiterTouchSizing.SpawnFadeDurationS;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                if (t == null) yield break;
                elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(elapsed / duration);
                // Ease-out cubic: 1 - (1-u)^3
                float k = 1f - Mathf.Pow(1f - u, 3f);
                t.position = Vector3.LerpUnclamped(startPos, finalPos, k);
                yield return null;
            }

            if (t != null) t.position = finalPos;
        }
    }
}
