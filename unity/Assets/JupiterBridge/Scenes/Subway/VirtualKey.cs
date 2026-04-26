using System;
using System.Collections.Generic;
using UnityEngine;

namespace JupiterBridge.Subway
{
    /// <summary>
    /// One key on the virtual keyboard. Press detection uses TWO layers:
    ///
    /// 1. Per-(finger,key) "primed → press" gating. A press only fires when a
    ///    finger that ENTERED the key's collider FROM ABOVE (its world Y was
    ///    above the key's top surface at OnTriggerEnter time) descends through
    ///    the press threshold. Resting/curled fingers below the keyboard plane
    ///    enter from below or from the side, are not primed, and so never fire.
    ///    This is what stops "press one key with index, get 5 letters typed
    ///    because ring/pinky/palm were also below the surface".
    ///
    /// 2. Shared arbitration so each finger only counts toward ONE key per
    ///    frame (the deepest one). Prevents one fingertip whose collider
    ///    overlaps two adjacent keys from firing both.
    ///
    /// EMS firing happens automatically via the existing layer-6 →
    /// FingerContactDetector → UDPSender pipeline. This component does no
    /// networking.
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

        [Tooltip("Legacy field — release is now driven by OnTriggerExit, not depth. " +
                 "Kept for serialization compatibility.")]
        [Range(0f, 1f)]    public float releaseThreshold = 0.08f;

        [Header("Visual feedback")]
        public Color restColor   = new Color(0.10f, 0.10f, 0.12f);
        public Color hoverColor  = new Color(0.18f, 0.20f, 0.30f);
        public Color pressColor  = new Color(0.30f, 0.65f, 1.00f);

        [Tooltip("Y-axis scale factor at full press (1 = no squash).")]
        [Range(0.7f, 1f)] public float pressedYScale = 0.85f;

        // ── runtime ────────────────────────────────────────────────────────

        /// <summary>
        /// Per-(finger,key) entry record. Created on OnTriggerEnter; deleted
        /// on OnTriggerExit. `primed` is locked at entry time; `pressed` flips
        /// to true the first time the finger crosses the press threshold,
        /// guaranteeing exactly one fire per entry.
        /// </summary>
        struct EntryState
        {
            public bool primed;   // entered from above the key's top surface
            public bool pressed;  // already fired OnKeyPressed during this entry
        }

        readonly Dictionary<FingerContactDetector, EntryState> _contacting
            = new Dictionary<FingerContactDetector, EntryState>();

        BoxCollider _box;
        Material    _mat;
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
            var det = other.GetComponentInParent<FingerContactDetector>();
            if (det == null || _contacting.ContainsKey(det)) return;

            // Decide whether this finger is "primed" — i.e. did it enter
            // through the top of the key from above. We check the finger's
            // world Y against the key's top-face world Y. SlideTolerance
            // forgives a glancing entry that just barely dips into the side
            // of the key from above so fast taps that come in at an angle
            // still count.
            float keyTopY = transform.position.y + (transform.lossyScale.y * 0.5f);
            float fingerY = det.transform.position.y;
            bool primed   = fingerY >= keyTopY - JupiterTouchSizing.KeyPressSlideToleranceM;

            _contacting[det] = new EntryState { primed = primed, pressed = false };
        }

        void OnTriggerExit(Collider other)
        {
            var det = other.GetComponentInParent<FingerContactDetector>();
            if (det == null) return;
            _contacting.Remove(det);
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
            PruneDestroyedFingers();

            // Compute max depth across fingers we OWN (per arbitration). This
            // drives the visual state (color/scale) so the user gets feedback
            // for ANY contacting finger, primed or not.
            float maxDepth = 0f;
            FingerContactDetector firingFinger = null;
            float firingDepth = 0f;
            foreach (var kv in _contacting)
            {
                var det = kv.Key;
                if (!OwnsFinger(det)) continue;
                float d = det.ContactDepth;
                if (d > maxDepth) maxDepth = d;

                // Track the deepest PRIMED finger that hasn't fired yet —
                // that's the press candidate for this frame.
                if (kv.Value.primed && !kv.Value.pressed && d >= pressThreshold && d > firingDepth)
                {
                    firingFinger = det;
                    firingDepth  = d;
                }
            }

            // ── Visual: colour
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

            // ── Fire one press per primed entry, ever. After firing, the
            //    entry's pressed flag stays true until the finger leaves
            //    the trigger (OnTriggerExit). To get a second press of this
            //    same key the finger has to lift off and come back down —
            //    the natural "double-tap" motion.
            if (firingFinger != null)
            {
                var entry = _contacting[firingFinger];
                entry.pressed = true;
                _contacting[firingFinger] = entry;
                Fire(firingFinger);
            }
        }

        bool OwnsFinger(FingerContactDetector finger)
        {
            return _winnerForFinger.TryGetValue(finger, out var w) && w == this;
        }

        void PruneDestroyedFingers()
        {
            // Dictionary doesn't have RemoveWhere; collect-then-remove.
            List<FingerContactDetector> stale = null;
            foreach (var k in _contacting.Keys)
            {
                if (k == null) (stale ??= new List<FingerContactDetector>()).Add(k);
            }
            if (stale != null) foreach (var k in stale) _contacting.Remove(k);
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
                key.PruneDestroyedFingers();
                foreach (var f in key._contacting.Keys)
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
                    if (key == null || !key._contacting.ContainsKey(finger)) continue;
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

        void Fire(FingerContactDetector finger)
        {
            OnKeyPressed?.Invoke(keyChar);
            if (KeyboardController.Instance != null)
            {
                bool leftHand = finger != null && finger.isLeftHand;
                KeyboardController.Instance.HandleKeyPress(keyChar, leftHand);
            }
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
