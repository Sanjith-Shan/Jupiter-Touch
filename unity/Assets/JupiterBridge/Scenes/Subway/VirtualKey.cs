using System;
using System.Collections.Generic;
using UnityEngine;

namespace JupiterBridge.Subway
{
    /// <summary>
    /// One key on the virtual keyboard. Lives on the cube body GameObject
    /// (so OnTrigger callbacks fire here) and tracks finger penetration via
    /// FingerContactDetectors that JupiterTester spawns on the user's fingertips.
    ///
    /// Press semantics:
    ///   • Fires when ANY contacting finger's depth crosses pressThreshold.
    ///   • Hysteresis: re-arms only after depth drops below releaseThreshold.
    ///   • Multi-finger: chord typing allowed — first finger across the line wins.
    ///
    /// Visual feedback:
    ///   • Rest → Hover (light contact) → Press (deep contact) color lerp.
    ///   • Y-axis scale-down at full press (mechanical "click" feel).
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class VirtualKey : MonoBehaviour
    {
        [Tooltip("Character emitted when the key fires. Use '\\b' for backspace, '\\n' for enter.")]
        public char keyChar = ' ';

        [Tooltip("Optional human-readable label (used for logging only).")]
        public string label = "";

        [Header("Press tuning (depth normalized 0..1; FingerContactDetector.maxDepthMetres = 0.03 m)")]
        [Tooltip("Depth at which the key fires. ~0.30 ≈ 9 mm penetration.")]
        [Range(0.05f, 1f)] public float pressThreshold   = 0.30f;

        [Tooltip("Depth at which the key re-arms after release. Must be < pressThreshold.")]
        [Range(0f, 1f)]    public float releaseThreshold = 0.08f;

        [Header("Visual feedback")]
        public Color restColor   = new Color(0.10f, 0.10f, 0.12f);
        public Color hoverColor  = new Color(0.18f, 0.20f, 0.30f);
        public Color pressColor  = new Color(0.30f, 0.65f, 1.00f);

        [Tooltip("Y-axis scale factor at full press (1 = no squash).")]
        [Range(0.7f, 1f)] public float pressedYScale = 0.85f;

        // ── runtime ────────────────────────────────────────────────────────
        readonly HashSet<FingerContactDetector> _contacting = new HashSet<FingerContactDetector>();
        Material _mat;
        bool     _pressed;
        Vector3  _restScale;

        public event Action<char> OnKeyPressed;

        // ──────────────────────────────────────────────────────────────────

        void Awake()
        {
            // Trigger collider so finger trackers slide through smoothly
            var col = GetComponent<BoxCollider>();
            col.isTrigger = true;

            // Cache material instance so each key flashes independently
            var rend = GetComponent<Renderer>();
            if (rend != null)
            {
                _mat = rend.material;
                ApplyColor(restColor);
            }

            _restScale = transform.localScale;
        }

        void OnTriggerEnter(Collider other)
        {
            // The Rigidbody might be on a parent of the collider, so search up.
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
            // Defensive: prune any detectors that were destroyed
            _contacting.RemoveWhere(d => d == null);

            float maxDepth = 0f;
            foreach (var det in _contacting)
                if (det.ContactDepth > maxDepth) maxDepth = det.ContactDepth;

            // ── Color: Rest → Hover → Press
            Color target;
            if (_contacting.Count == 0) target = restColor;
            else if (maxDepth < releaseThreshold * 0.5f) target = hoverColor;
            else
            {
                float t = Mathf.InverseLerp(0f, pressThreshold, maxDepth);
                target = Color.Lerp(hoverColor, pressColor, t);
            }
            ApplyColor(target);

            // ── Scale: directly drive Y by press fraction (snappier than easing)
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
