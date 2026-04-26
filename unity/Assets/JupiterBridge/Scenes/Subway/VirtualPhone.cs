using System.Collections.Generic;
using TMPro;
using UnityEngine;
using JupiterBridge.Tests;

namespace JupiterBridge.Subway
{
    /// <summary>
    /// A held virtual phone with grab interaction. Spawned by
    /// SubwaySceneController; appears floating in world space in front of
    /// the user. The user reaches out and wraps their fingers around it to
    /// pick it up — when ≥PhoneGrabFingerCount fingers from one hand are
    /// inside the phone's body collider simultaneously, the phone parents
    /// itself to that hand's palm bone (preserving the current world pose
    /// so it doesn't teleport). When the user opens that hand and the
    /// finger count drops below PhoneReleaseFingerCount, the phone
    /// un-parents and stays floating at its last position so it can be
    /// grabbed again.
    ///
    /// EMS feedback runs through the existing layer-6 pipeline: the root
    /// GameObject's BoxCollider is on EmsContactLayer, so any fingertip
    /// detector wrapping the phone fires per-finger UDP events to the
    /// bridge, which routes by hand to the correct Arduino. No phone-
    /// specific EMS code needed.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class VirtualPhone : MonoBehaviour
    {
        enum State { Floating, Held }

        [Header("Visual")]
        public Color bodyColor   = new Color(0.04f, 0.04f, 0.05f);
        public Color screenColor = new Color(0.05f, 0.30f, 0.55f);
        public Color screenTextColor = new Color(0.85f, 1.00f, 1.00f);
        [TextArea(1, 4)] public string staticText = "JUPITER\nTOUCH";

        [Header("Behavior")]
        [Tooltip("Phone body collider on the EMS-contact layer so fingertips " +
                 "fire EMS while the user grips.")]
        public bool emsTouchableBody = true;

        // ── runtime ───────────────────────────────────────────────────────
        State _state = State.Floating;
        JupiterTester.Handedness _heldBy;
        readonly HashSet<FingerContactDetector> _contacting = new HashSet<FingerContactDetector>();
        BoxCollider _collider;
        TextMeshPro _tmp;

        // ── lifecycle ─────────────────────────────────────────────────────

        void Awake()
        {
            // Configure the root collider IMMEDIATELY at component-add time —
            // RequireComponent's default BoxCollider is 1×1×1 and not a
            // trigger, which would catch unintended overlaps in the brief
            // window between Awake and Start. Sizing it now and flipping
            // isTrigger before any physics step closes that window.
            gameObject.layer = emsTouchableBody ? JupiterTouchSizing.EmsContactLayer : 0;
            _collider = GetComponent<BoxCollider>();
            _collider.isTrigger = true;
            _collider.center    = Vector3.zero;
            _collider.size      = new Vector3(
                JupiterTouchSizing.PhoneWidthM,
                JupiterTouchSizing.PhoneLengthM,
                JupiterTouchSizing.PhoneThicknessM);
        }

        void Start()
        {
            BuildVisuals();
        }

        // ── grab / release state machine ──────────────────────────────────

        void OnTriggerEnter(Collider other)
        {
            var det = other.GetComponentInParent<FingerContactDetector>();
            if (det != null && _contacting.Add(det)) EvaluateGrabState();
        }

        void OnTriggerExit(Collider other)
        {
            var det = other.GetComponentInParent<FingerContactDetector>();
            if (det != null && _contacting.Remove(det)) EvaluateGrabState();
        }

        void EvaluateGrabState()
        {
            int rightCount = 0, leftCount = 0;
            foreach (var det in _contacting)
            {
                if (det == null) continue;
                if (det.isLeftHand) leftCount++; else rightCount++;
            }

            if (_state == State.Floating)
            {
                if (rightCount >= JupiterTouchSizing.PhoneGrabFingerCount)
                    Grab(JupiterTester.Handedness.Right);
                else if (leftCount >= JupiterTouchSizing.PhoneGrabFingerCount)
                    Grab(JupiterTester.Handedness.Left);
            }
            else
            {
                int holdingCount = (_heldBy == JupiterTester.Handedness.Right) ? rightCount : leftCount;
                if (holdingCount < JupiterTouchSizing.PhoneReleaseFingerCount)
                    Release();
            }
        }

        void Grab(JupiterTester.Handedness hand)
        {
            Transform palmBone = FindPalmBone(hand);
            if (palmBone == null)
            {
                Debug.LogWarning($"[VirtualPhone] Cannot grab — no {hand} palm bone " +
                                 "(is HandTracker_" + hand + " in the scene?)");
                return;
            }
            // worldPositionStays preserves the phone's world pose at the
            // moment of grab so it doesn't teleport out of the user's grip.
            transform.SetParent(palmBone, worldPositionStays: true);
            _state = State.Held;
            _heldBy = hand;
            Debug.Log($"[VirtualPhone] Grabbed by {hand} hand " +
                      $"(R={CountHand(false)} L={CountHand(true)})");
        }

        void Release()
        {
            transform.SetParent(null, worldPositionStays: true);
            _state = State.Floating;
            Debug.Log("[VirtualPhone] Released — floating in world space");
        }

        int CountHand(bool isLeft)
        {
            int c = 0;
            foreach (var det in _contacting)
                if (det != null && det.isLeftHand == isLeft) c++;
            return c;
        }

        Transform FindPalmBone(JupiterTester.Handedness hand)
        {
            foreach (var t in FindObjectsByType<JupiterTester>(FindObjectsSortMode.None))
                if (t.handedness == hand) return t.PalmBone;
            return null;
        }

        // ── visuals ───────────────────────────────────────────────────────

        void BuildVisuals()
        {
            float w = JupiterTouchSizing.PhoneWidthM;
            float l = JupiterTouchSizing.PhoneLengthM;
            float t = JupiterTouchSizing.PhoneThicknessM;
            float inset = JupiterTouchSizing.PhoneScreenInsetM;

            // ── Body slab visual (no collider — root has it)
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name  = "PhoneBody";
            body.layer = 0;
            Destroy(body.GetComponent<BoxCollider>());
            body.transform.SetParent(transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale    = new Vector3(w, l, t);
            body.GetComponent<Renderer>().material = MakeMaterial(bodyColor);

            // ── Screen face — pushed proud of the body's +Z face
            var screen = GameObject.CreatePrimitive(PrimitiveType.Cube);
            screen.name  = "PhoneScreen";
            screen.layer = 0;
            Destroy(screen.GetComponent<BoxCollider>());
            screen.transform.SetParent(transform, false);
            screen.transform.localPosition = new Vector3(0f, 0f, t * 0.5f + 0.0006f);
            screen.transform.localScale    = new Vector3(w - inset * 2f, l - inset * 2f, 0.001f);
            screen.GetComponent<Renderer>().material = MakeMaterial(screenColor);

            // ── Optional static label
            if (!string.IsNullOrEmpty(staticText))
            {
                var textGo = new GameObject("PhoneText");
                textGo.transform.SetParent(transform, false);
                textGo.transform.localPosition = new Vector3(0f, 0f, t * 0.5f + 0.0015f);
                // TMP front face is local -Z; flip 180° around Y so the
                // readable face points along the screen normal (+Z).
                textGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

                float parentScale = Mathf.Max(0.0001f, transform.lossyScale.x);
                textGo.transform.localScale = Vector3.one * (JupiterTouchSizing.LabelScale / parentScale);

                _tmp = textGo.AddComponent<TextMeshPro>();
                _tmp.text             = staticText;
                _tmp.alignment        = TextAlignmentOptions.Center;
                _tmp.color            = screenTextColor;
                _tmp.fontStyle        = FontStyles.Bold;
                _tmp.enableAutoSizing = false;
                _tmp.fontSize         = JupiterTouchSizing.HeightMmToFontSize(8f);
                _tmp.enableWordWrapping = true;
                _tmp.overflowMode       = TextOverflowModes.Truncate;
                _tmp.richText           = true;

                _tmp.rectTransform.sizeDelta = new Vector2(
                    (w - inset * 4f) / JupiterTouchSizing.LabelScale,
                    (l - inset * 4f) / JupiterTouchSizing.LabelScale);
                _tmp.ForceMeshUpdate();
            }

            Debug.Log($"[VirtualPhone] Built {w*1000:F0}×{l*1000:F0}×{t*1000:F0} mm; " +
                      $"emsLayer={(emsTouchableBody ? "yes" : "no")}");
        }

        Material MakeMaterial(Color c)
        {
            string[] candidates = {
                "Universal Render Pipeline/Lit", "Standard", "Mobile/Diffuse", "Unlit/Color"
            };
            Shader sh = null;
            foreach (var n in candidates) { sh = Shader.Find(n); if (sh != null) break; }
            var mat = new Material(sh);
            mat.color = c;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            return mat;
        }
    }
}
