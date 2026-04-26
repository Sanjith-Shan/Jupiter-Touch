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
    ///   reset_subway    → destroy everything spawned, ready for next demo
    /// </summary>
    public class SubwaySceneController : MonoBehaviour
    {
        [Header("Spawn anchors")]
        [Tooltip("If empty, positions are computed relative to the main camera at scene start.")]
        public Transform monitorAnchor;
        public Transform keyboardAnchor;

        [Header("Defaults (used when anchors are empty)")]
        public float monitorDistance = 0.85f;
        public float monitorHeight   = 0.05f;   // relative to head
        public float monitorSpacing  = 0.50f;   // metres apart
        public float keyboardDistance = 0.45f;
        public float keyboardDrop     = -0.30f; // below head, on lap

        readonly List<GameObject> _spawned = new List<GameObject>();

        // Cached at scene-load so spawns are stable even if the user looks around
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
        }

        void CaptureSeatFrame()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                _seatPos   = cam.transform.position;
                _seatFwd   = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                if (_seatFwd.sqrMagnitude < 0.01f) _seatFwd = Vector3.forward;
                _seatRight = Vector3.Cross(Vector3.up, _seatFwd).normalized * -1f;
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
                case "reset_subway":   DespawnAll();    break;
                default:
                    Debug.Log($"[SubwaySceneController] Unhandled event: {msg.id}");
                    break;
            }
        }

        // ── Spawners ─────────────────────────────────────────────────────

        void SpawnMonitors()
        {
            if (HasSpawned("Monitor_L") || HasSpawned("Monitor_R")) return;

            CaptureSeatFrame();

            Vector3 center = _seatPos + _seatFwd * monitorDistance + Vector3.up * monitorHeight;

            var left  = SpawnMonitor("Monitor_L", center - _seatRight * (monitorSpacing * 0.5f), 5f);
            var right = SpawnMonitor("Monitor_R", center + _seatRight * (monitorSpacing * 0.5f), -5f);

            // Right monitor: prefilled with a static "code editor" placeholder
            var vm = right.GetComponent<VirtualMonitor>();
            vm.staticText =
                "// jupiter_touch — main.cs\n" +
                "void Start() {\n" +
                "    HandTracker.Instance.Begin();\n" +
                "    EMS.Connect(\"COM3\");\n" +
                "}";
        }

        GameObject SpawnMonitor(string name, Vector3 worldPos, float yawDegrees)
        {
            var go = new GameObject(name);
            go.transform.position = worldPos;
            // Face the user
            Vector3 toUser = (_seatPos - worldPos);
            toUser.y = 0;
            if (toUser.sqrMagnitude > 0.001f)
                go.transform.rotation = Quaternion.LookRotation(-toUser.normalized) * Quaternion.Euler(0, yawDegrees, 0);

            go.AddComponent<VirtualMonitor>();
            _spawned.Add(go);

            Debug.Log($"[SubwaySceneController] Spawned {name} @ {worldPos}");
            return go;
        }

        void SpawnKeyboard()
        {
            if (HasSpawned("Keyboard")) return;

            CaptureSeatFrame();

            Vector3 pos = _seatPos + _seatFwd * keyboardDistance + Vector3.up * keyboardDrop;

            var go = new GameObject("Keyboard");
            go.transform.position = pos;
            // Tilt slightly toward the user
            go.transform.rotation = Quaternion.LookRotation(_seatFwd, Vector3.up) * Quaternion.Euler(15f, 0f, 0f);

            go.AddComponent<VirtualKeyboard>();
            _spawned.Add(go);

            Debug.Log($"[SubwaySceneController] Spawned Keyboard @ {pos}");
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
    }
}
