using System;
using System.Collections.Generic;
using UnityEngine;

namespace JupiterBridge.Subway
{
    /// <summary>
    /// One key on the virtual keyboard. Press detection uses a SHARED arbitration
    /// step so each finger can only be "claimed" by ONE key at any moment — the
    /// key it has penetrated deepest. Other keys ignore that finger entirely,
    /// which prevents one fingertip from triggering multiple adjacent keys at once.
    ///
    /// Hysteresis (press at pressThreshold, release at releaseThreshold) prevents
    /// jitter retrigger.
    ///
    /// EMS firing happens automatically via the existing layer-6 → FingerContactDetector
    /// → UDPSender pipeline. This component does no networking.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class VirtualKey : MonoBehaviour
    {
        [Tooltip("Character emitted when the key fires. Use '\\b' for backspace, '\\n' for enter.")]
        public char keyChar = ' ';

        [Tooltip("Optional human-readable label (used for logging only).")]
        public string label = "";

        [Header("Press tuning (depth normalized 0..1; FingerContactDetector.maxDepthMetres = 0.03 m)")]
        [Range(0.05f, 1f)] public float pressThreshold   = 0.30f;
        [Range(0f, 1f)]    public float releaseThreshold = 0.08f;

        [Header("Visual feedback")]
        public Color restColor   = new Color(0.10f, 0.10f, 0.12f);
        public Color hoverColor  = new Color(0.18f, 0.20f, 0.30f);
        public Color pressColor  = new Color(0.30f, 0.65f, 1.00f);

        [Tooltip("Y-axis scale factor at full press (1 = no squash).")]
        [Range(0.7f, 1f)] public float pressedYScale = 0.85f;

        // ── runtime ────────────────────────────────────────────────────────
        readonly HashSet<FingerContactDetector> _contacting = new HashSet<FingerContactDetector>();
        BoxCollider _box;
        Material    _mat;
        bool        _pressed;
        Vector3     _restScale;

        public event Action<char> OnKeyPressed;

        // ── shared arbitration ─────────────────────────────────────────────
        // Static state shared across ALL VirtualKey instances. Once per frame,
        // for each finger touching ANY key, find the key it penetrates deepest.
        // That key "wins" the finger; other keys ignore it.
        static readonly HashSet<VirtualKey> AllInstances = new HashSet<VirtualKey>();
        static readonly Dictionary<FingerContactDetector, VirtualKey> _winnerForFinger
            = new Dictionary<FingerContactDetector, VirtualKey>();
        static int _lastArbitrationFrame = -1;

        // ──────────────────────────────────────────────────────────────────

        void Awake()
        {
            _box = GetComponent<BoxCollider>();
            _box.isTrigger = true;

            var rend = GetComponent<Renderer>();
            if (rend != null)
            {
                _mat = rend.material;
                ApplyColor(restColor);
            }

            _restScale = transform.localScale;
        }

        void OnEnable()  => AllInstances.Add(this);
        void OnDisable() => AllInstances.Remove(this);

        void OnTriggerEnter(Collider other)
        {
            // The Rigidbody might be on a parent of the collider, search up.
            var det = other.GetComponentInParent<FingerContactDetector>();
            if (det != null) _contacting.Add(det);
        }

        void OnTriggerExit(Collider other)
        {
            var det = other.GetComponentInParent<FingerContactDetector>();
            if (det != null) _contacting.Remove(det);
        }

        void Update()
        {
            // Run shared arbitration once per frame (first key to call wins the race)
            if (_lastArbitrationFrame != Time.frameCount)
            {
                _lastArbitrationFrame = Time.frameCount;
                Arbitrate();
            }

            // Defensive: prune destroyed detectors
            _contacting.RemoveWhere(d => d == null);

            // Compute max depth across fingers we OWN (per arbitration)
            float maxDepth = 0f;
            foreach (var det in _contacting)
            {
                if (!OwnsFinger(det)) continue;
                if (det.ContactDepth > maxDepth) maxDepth = det.ContactDepth;
            }

            // ── Visual: color
            Color target;
            if (maxDepth <= 0f)                      target = restColor;
            else if (maxDepth < releaseThreshold)    target = hoverColor;
            else
            {
                float t = Mathf.InverseLerp(0f, pressThreshold, maxDepth);
                target = Color.Lerp(hoverColor, pressColor, t);
            }
            ApplyColor(target);

            // ── Visual: Y-axis scale-down at full press
            float pressFraction = Mathf.Clamp01(maxDepth / pressThreshold);
            float yTarget = Mathf.Lerp(_restScale.y, _restScale.y * pressedYScale, pressFraction);
            transform.localScale = new Vector3(_restScale.x, yTarget, _restScale.z);

            // ── Press / re-arm with hysteresis
            if (!_pressed && maxDepth >= pressThreshold)
            {
                _pressed = true;
                Fire();
            }
            else if (_pressed && maxDepth < releaseThreshold)
            {
                _pressed = false;
            }
        }

        bool OwnsFinger(FingerContactDetector finger)
        {
            return _winnerForFinger.TryGetValue(finger, out var w) && w == this;
        }

        // ── Arbitration: assign each contacting finger to its deepest key ──
        static void Arbitrate()
        {
            _winnerForFinger.Clear();

            // Collect fingers touching any key
            var contestedFingers = new HashSet<FingerContactDetector>();
            foreach (var key in AllInstances)
            {
                if (key == null) continue;
                key._contacting.RemoveWhere(d => d == null);
                foreach (var f in key._contacting)
                    contestedFingers.Add(f);
            }

            // For each finger, find the deepest key
            foreach (var finger in contestedFingers)
            {
                if (finger == null) continue;
                VirtualKey deepestKey = null;
                float deepestDist = -1f;

                foreach (var key in AllInstances)
                {
                    if (key == null || !key._contacting.Contains(finger)) continue;
                    float d = key.ComputeRawPenetration(finger);
                    if (d > deepestDist) { deepestDist = d; deepestKey = key; }
                }

                if (deepestKey != null)
                    _winnerForFinger[finger] = deepestKey;
            }
        }

        float ComputeRawPenetration(FingerContactDetector finger)
        {
            if (_box == null || finger == null) return 0f;
            var fingerCol = finger.GetComponent<Collider>();
            if (fingerCol == null) return 0f;

            bool overlap = Physics.ComputePenetration(
                _box,      transform.position,           transform.rotation,
                fingerCol, fingerCol.transform.position, fingerCol.transform.rotation,
                out _, out float distance);
            return overlap ? distance : 0f;
        }

        // ──────────────────────────────────────────────────────────────────

        void Fire()
        {
            OnKeyPressed?.Invoke(keyChar);
            if (KeyboardController.Instance != null)
                KeyboardController.Instance.HandleKeyPress(keyChar);
        }

        void ApplyColor(Color c)
        {
            if (_mat == null) return;
            if (_mat.HasProperty("_Color"))     _mat.SetColor("_Color", c);
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", c);
            _mat.color = c;
        }

        void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }
}
