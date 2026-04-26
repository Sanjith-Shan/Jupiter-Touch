using System;
using System.Collections.Generic;
using UnityEngine;

namespace JupiterBridge.Subway
{
    /// <summary>
    /// One key on the virtual keyboard. Detects finger penetration via
    /// Unity trigger events, fires OnKeyPressed once per press cycle (debounced),
    /// and animates a color flash. EMS feedback is handled automatically by the
    /// existing FingerContactDetector pipeline because this GameObject lives
    /// on layer 6 ("EMS Contact").
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class VirtualKey : MonoBehaviour
    {
        [Tooltip("Character emitted when the key fires. Use '\\b' for backspace, '\\n' for enter.")]
        public char keyChar = ' ';

        [Tooltip("Optional label shown on the key face. Defaults to keyChar.")]
        public string label = "";

        [Header("Press tuning")]
        [Tooltip("Depth (0..1, normalized to FingerContactDetector.maxDepthMetres) at which the key fires.")]
        [Range(0.05f, 1f)] public float pressThreshold = 0.30f;

        [Tooltip("Depth at which the key re-arms after release.")]
        [Range(0f, 1f)]    public float releaseThreshold = 0.10f;

        [Header("Visual")]
        public Color restColor    = new Color(0.10f, 0.10f, 0.12f);
        public Color pressColor   = new Color(0.30f, 0.65f, 1.00f);
        public Color labelColor   = new Color(0.92f, 0.92f, 0.95f);

        // ── runtime ────────────────────────────────────────────────────────
        readonly HashSet<FingerContactDetector> _contacting = new HashSet<FingerContactDetector>();
        Material _mat;
        bool _pressed;

        public event Action<char> OnKeyPressed;

        // ──────────────────────────────────────────────────────────────────

        void Awake()
        {
            // Make the box collider a trigger so finger trackers slide through
            var col = GetComponent<BoxCollider>();
            col.isTrigger = true;

            // Cache material instance so each key flashes independently
            var rend = GetComponent<Renderer>();
            if (rend != null)
            {
                _mat = rend.material;
                ApplyColor(restColor);
            }
        }

        void OnTriggerEnter(Collider other)
        {
            var det = other.GetComponent<FingerContactDetector>();
            if (det != null) _contacting.Add(det);
        }

        void OnTriggerExit(Collider other)
        {
            var det = other.GetComponent<FingerContactDetector>();
            if (det != null) _contacting.Remove(det);
        }

        void Update()
        {
            if (_contacting.Count == 0)
            {
                if (_pressed) _pressed = false;
                ApplyColor(restColor);
                return;
            }

            // Use the deepest contact across all touching fingers
            float maxDepth = 0f;
            foreach (var det in _contacting)
            {
                if (det == null) continue;
                if (det.ContactDepth > maxDepth) maxDepth = det.ContactDepth;
            }

            // Lerp visual color smoothly with depth
            float t = Mathf.Clamp01(maxDepth / Mathf.Max(0.01f, pressThreshold));
            ApplyColor(Color.Lerp(restColor, pressColor, t));

            // Press / re-arm
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
