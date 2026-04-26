using TMPro;
using UnityEngine;

namespace JupiterBridge.Subway
{
    /// <summary>
    /// A floating monitor that displays the current text from KeyboardController.
    /// Built procedurally on Start: a thin black panel + bezel + a TextMeshPro
    /// element on the front face.
    ///
    /// Optionally puts the bezel on layer 6 so touching the monitor's frame
    /// fires a mild EMS pulse.
    ///
    /// Auto-anchors itself in front of the camera at scene start (similar to
    /// VirtualKeyboard) so you don't have to manually position it.
    /// </summary>
    public class VirtualMonitor : MonoBehaviour
    {
        [Header("Geometry (metres)")]
        public float screenWidth    = 0.45f;
        public float screenHeight   = 0.28f;   // ~16:10
        public float screenDepth    = 0.020f;
        public float bezelThickness = 0.012f;

        [Header("Display")]
        [Tooltip("If empty, subscribes to KeyboardController.Instance text.")]
        [TextArea(3, 8)] public string staticText = "";
        public Color textColor    = new Color(0.85f, 1.00f, 0.95f);
        public Color screenColor  = new Color(0.04f, 0.05f, 0.06f);
        public Color bezelColor   = new Color(0.12f, 0.12f, 0.14f);

        [Header("Text sizing")]
        [Tooltip("Transform scale for the text element (1 TMP unit = N metres). 0.001 → 1 mm per unit.")]
        public float labelScale = 0.001f;
        [Tooltip("TMP fontSize (point units). With labelScale=0.001, fontSize 32 ≈ 18 mm character height.")]
        public float labelPointSize = 32f;

        [Header("EMS")]
        [Tooltip("Put the bezel on layer 6 so touching the monitor's frame fires EMS.")]
        public bool emsTouchableBezel = true;
        public int  contactLayer      = 6;

        [Header("Auto-anchor (camera-relative)")]
        public bool    autoAnchorToCamera = true;
        [Tooltip("Offset from camera: x = right, y = up, z = forward (metres).")]
        public Vector3 cameraAnchorOffset = new Vector3(0.00f, 0.10f, 0.85f);

        TextMeshPro _tmp;

        void Start()
        {
            if (autoAnchorToCamera) AnchorToCamera();
            BuildMonitor();

            if (string.IsNullOrEmpty(staticText) && KeyboardController.Instance != null)
            {
                KeyboardController.Instance.TextChanged += OnTextChanged;
                OnTextChanged(KeyboardController.Instance.Text);
            }
            else
            {
                OnTextChanged(staticText);
            }
        }

        void AnchorToCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

            transform.position = cam.transform.position
                + right * cameraAnchorOffset.x
                + Vector3.up * cameraAnchorOffset.y
                + fwd * cameraAnchorOffset.z;

            // Face the user
            transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
        }

        void OnDestroy()
        {
            if (KeyboardController.Instance != null)
                KeyboardController.Instance.TextChanged -= OnTextChanged;
        }

        void OnTextChanged(string text)
        {
            if (_tmp != null) _tmp.text = text;
        }

        void BuildMonitor()
        {
            // ── Bezel (slightly larger box behind the screen) ────────────
            var bezel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bezel.name = "Bezel";
            bezel.layer = emsTouchableBezel ? contactLayer : 0;
            bezel.transform.SetParent(transform, false);
            bezel.transform.localScale = new Vector3(
                screenWidth + bezelThickness * 2f,
                screenHeight + bezelThickness * 2f,
                screenDepth);
            bezel.GetComponent<Renderer>().material = MakeMaterial(bezelColor);
            bezel.GetComponent<BoxCollider>().isTrigger = true;

            // ── Screen face ──────────────────────────────────────────────
            var screen = GameObject.CreatePrimitive(PrimitiveType.Cube);
            screen.name = "Screen";
            screen.layer = 0;
            Destroy(screen.GetComponent<BoxCollider>());
            screen.transform.SetParent(transform, false);
            // Front face of screen sits slightly forward of bezel (toward viewer)
            screen.transform.localPosition = new Vector3(0, 0, -screenDepth * 0.30f);
            screen.transform.localScale    = new Vector3(screenWidth, screenHeight, screenDepth * 0.5f);
            screen.GetComponent<Renderer>().material = MakeMaterial(screenColor);

            // ── Text element on front face ───────────────────────────────
            var textGo = new GameObject("MonitorText");
            textGo.transform.SetParent(transform, false);
            // Place text just in front of the screen face so it doesn't z-fight
            textGo.transform.localPosition = new Vector3(0f, 0f, -screenDepth * 0.55f - 0.0010f);
            // Local rotation identity: text reads with +X = world right, +Y = world up,
            // facing -Z (which after the LookRotation(-fwd, up) on the parent points
            // back toward the user). Apply a 180° flip around Y so the front face is
            // toward the user.
            textGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            // Compensate for any inherited parent scale
            float parentScale = Mathf.Max(0.0001f, transform.lossyScale.x);
            textGo.transform.localScale = Vector3.one * (labelScale / parentScale);

            _tmp = textGo.AddComponent<TextMeshPro>();
            _tmp.text             = "";
            _tmp.alignment        = TextAlignmentOptions.TopLeft;
            _tmp.color            = textColor;
            _tmp.fontStyle        = FontStyles.Normal;
            _tmp.enableAutoSizing = false;
            _tmp.fontSize         = labelPointSize;
            _tmp.enableWordWrapping = true;
            _tmp.overflowMode     = TextOverflowModes.Truncate;

            float marginM = 0.012f;
            _tmp.rectTransform.sizeDelta = new Vector2(
                ((screenWidth  - marginM * 2f) / labelScale),
                ((screenHeight - marginM * 2f) / labelScale));

            _tmp.ForceMeshUpdate();
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
