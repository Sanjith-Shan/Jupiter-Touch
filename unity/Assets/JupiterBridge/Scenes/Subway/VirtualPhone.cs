using System.Collections;
using TMPro;
using UnityEngine;
using JupiterBridge.Tests;

namespace JupiterBridge.Subway
{
    /// <summary>
    /// A held virtual phone. Spawned by SubwaySceneController on the
    /// "spawn_phone" director event; finds the user's hand via the
    /// matching JupiterTester instance, parents itself to the palm bone,
    /// and renders a slab body + emissive screen.
    ///
    /// EMS feedback is automatic via the existing layer-6 pipeline: the
    /// phone body is on the EMS contact layer, so when the user's
    /// fingertips wrap around the phone they overlap its BoxCollider and
    /// FingerContactDetector fires per-finger UDP events as usual.
    /// pc_bridge.py routes by hand, so the right hand's grip on the phone
    /// drives the right Arduino's EMS channels.
    /// </summary>
    public class VirtualPhone : MonoBehaviour
    {
        [Header("Which hand")]
        [Tooltip("The phone parents itself to whichever JupiterTester " +
                 "instance has matching handedness.")]
        public JupiterTester.Handedness targetHand = JupiterTester.Handedness.Right;

        [Header("Visual")]
        public Color bodyColor   = new Color(0.04f, 0.04f, 0.05f);
        public Color screenColor = new Color(0.05f, 0.30f, 0.55f);
        public Color screenTextColor = new Color(0.85f, 1.00f, 1.00f);

        [Tooltip("Static text rendered on the phone screen. Empty = no text.")]
        [TextArea(1, 4)] public string staticText = "JUPITER\nTOUCH";

        [Header("Behavior")]
        [Tooltip("Phone body collider is on the EMS contact layer so " +
                 "fingertip detectors fire EMS while the user grips it.")]
        public bool emsTouchableBody = true;

        [Tooltip("Seconds to keep polling for the palm bone before giving " +
                 "up. JupiterTester binds bones in a coroutine that takes " +
                 "~1 s on cold start, so the phone has to wait for it.")]
        public float palmBoneFindTimeoutS = 5f;

        // ── runtime ───────────────────────────────────────────────────────
        Transform _palmBone;
        TextMeshPro _tmp;

        IEnumerator Start()
        {
            // Build the geometry first so the phone is visible somewhere
            // even if the palm-bone lookup takes a beat. Once the bone is
            // found, parent ourselves to it.
            BuildPhone();

            float waited = 0f;
            while (_palmBone == null && waited < palmBoneFindTimeoutS)
            {
                FindPalmBone();
                if (_palmBone != null) break;
                waited += Time.deltaTime;
                yield return null;
            }

            if (_palmBone == null)
            {
                Debug.LogError($"[VirtualPhone] Timed out waiting for {targetHand} palm bone — " +
                               $"is HandTracker_{targetHand} in the scene with handSkeleton wired?");
                yield break;
            }

            AttachToPalm();
            Debug.Log($"[VirtualPhone] Attached to {targetHand} palm bone");
        }

        void FindPalmBone()
        {
            // FindObjectsOfType is cheap relative to scene complexity here
            // (a couple of JupiterTester instances at most). Only runs until
            // we get a hit or timeout.
            foreach (var t in FindObjectsByType<JupiterTester>(FindObjectsSortMode.None))
            {
                if (t.handedness != targetHand) continue;
                Transform palm = t.PalmBone;
                if (palm != null) { _palmBone = palm; return; }
            }
        }

        void AttachToPalm()
        {
            transform.SetParent(_palmBone, worldPositionStays: false);
            transform.localPosition = JupiterTouchSizing.PhonePalmLocalOffset;
            transform.localRotation = Quaternion.Euler(JupiterTouchSizing.PhonePalmLocalEuler);
        }

        void BuildPhone()
        {
            float w = JupiterTouchSizing.PhoneWidthM;
            float l = JupiterTouchSizing.PhoneLengthM;
            float t = JupiterTouchSizing.PhoneThicknessM;
            float inset = JupiterTouchSizing.PhoneScreenInsetM;

            // ── Body slab — this is the EMS-contact layer collider that
            //    makes the phone "feel real" when the user grips it.
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name  = "PhoneBody";
            body.layer = emsTouchableBody ? JupiterTouchSizing.EmsContactLayer : 0;
            body.transform.SetParent(transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale    = new Vector3(w, l, t);
            body.GetComponent<Renderer>().material = MakeMaterial(bodyColor);
            body.GetComponent<BoxCollider>().isTrigger = true;

            // ── Screen face — thin slab pushed proud of the body's +Z face
            //    so it's visible without z-fighting.
            var screen = GameObject.CreatePrimitive(PrimitiveType.Cube);
            screen.name  = "PhoneScreen";
            screen.layer = 0;
            Destroy(screen.GetComponent<BoxCollider>());
            screen.transform.SetParent(transform, false);
            screen.transform.localPosition = new Vector3(0f, 0f, t * 0.5f + 0.0006f);
            screen.transform.localScale    = new Vector3(w - inset * 2f, l - inset * 2f, 0.001f);
            screen.GetComponent<Renderer>().material = MakeMaterial(screenColor);

            // ── Optional static label on the screen.
            if (!string.IsNullOrEmpty(staticText))
            {
                var textGo = new GameObject("PhoneText");
                textGo.transform.SetParent(transform, false);
                textGo.transform.localPosition = new Vector3(0f, 0f, t * 0.5f + 0.0015f);
                // TMP front face is local -Z; rotate 180° about Y so the
                // readable face is along the screen's +Z normal (out of the
                // body).
                textGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

                float parentScale = Mathf.Max(0.0001f, transform.lossyScale.x);
                textGo.transform.localScale = Vector3.one * (JupiterTouchSizing.LabelScale / parentScale);

                _tmp = textGo.AddComponent<TextMeshPro>();
                _tmp.text             = staticText;
                _tmp.alignment        = TextAlignmentOptions.Center;
                _tmp.color            = screenTextColor;
                _tmp.fontStyle        = FontStyles.Bold;
                _tmp.enableAutoSizing = false;
                // Cap height ~ 8 mm for the phone screen — readable but
                // doesn't dominate.
                _tmp.fontSize         = JupiterTouchSizing.HeightMmToFontSize(8f);
                _tmp.enableWordWrapping = true;
                _tmp.overflowMode       = TextOverflowModes.Truncate;
                _tmp.richText           = true;

                _tmp.rectTransform.sizeDelta = new Vector2(
                    (w - inset * 4f) / JupiterTouchSizing.LabelScale,
                    (l - inset * 4f) / JupiterTouchSizing.LabelScale);
                _tmp.ForceMeshUpdate();
            }

            Debug.Log(
                $"[VirtualPhone] Built body {w*1000:F0}×{l*1000:F0}×{t*1000:F0} mm; " +
                $"screen-side EMS={(emsTouchableBody ? "on" : "off")}; " +
                $"text=\"{staticText.Replace("\n", "\\n")}\"");
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
