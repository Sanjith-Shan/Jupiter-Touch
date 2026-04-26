using TMPro;
using UnityEngine;

namespace JupiterBridge.Subway
{
    /// <summary>
    /// A floating monitor that displays the current text from KeyboardController.
    ///
    /// Structure:
    ///   • This GameObject (positioned + rotated to face the user)
    ///     ├── Bezel  — flat dark cube; sits BEHIND the screen face. Layer 6 if
    ///     │            emsTouchableBezel is on so brushing it fires EMS.
    ///     ├── Screen — thin emissive panel IN FRONT of the bezel, visible to user.
    ///     └── Text   — TMP element rendered just in front of the screen.
    ///
    /// Subscription: in case VirtualKeyboard's KeyboardController doesn't exist
    /// at this Start (e.g. monitor spawned before keyboard), the subscription
    /// is retried each Update until it connects.
    /// </summary>
    public class VirtualMonitor : MonoBehaviour
    {
        [Header("Geometry (metres)")]
        public float screenWidth    = 0.45f;
        public float screenHeight   = 0.28f;
        public float screenDepth    = 0.020f;
        public float bezelThickness = 0.012f;

        [Header("Display")]
        [Tooltip("If empty, subscribes to KeyboardController.Instance text.")]
        [TextArea(3, 8)] public string staticText = "";
        public Color textColor   = new Color(0.85f, 1.00f, 0.95f);
        public Color screenColor = new Color(0.04f, 0.05f, 0.06f);
        public Color bezelColor  = new Color(0.12f, 0.12f, 0.14f);

        [Header("Text sizing")]
        [Tooltip("Transform scale for the text element. 0.001 → 1 TMP unit = 1 mm world.")]
        public float labelScale     = 0.001f;
        [Tooltip("TMP fontSize (point units). With labelScale=0.001, fontSize 36 ≈ 20 mm character height.")]
        public float labelPointSize = 36f;

        [Header("EMS")]
        public bool emsTouchableBezel = true;
        public int  contactLayer      = 6;

        [Header("Auto-anchor (camera-relative)")]
        public bool    autoAnchorToCamera = true;
        public Vector3 cameraAnchorOffset = new Vector3(0.00f, 0.10f, 0.85f);

        TextMeshPro _tmp;
        bool        _subscribed;

        // ──────────────────────────────────────────────────────────────────

        void Start()
        {
            if (autoAnchorToCamera) AnchorToCamera();
            BuildMonitor();
            TrySubscribe();
        }

        void Update()
        {
            // Retry subscription if we missed it on Start (e.g. keyboard
            // spawned after this monitor)
            if (!_subscribed) TrySubscribe();
        }

        void TrySubscribe()
        {
            if (_subscribed) return;
            if (!string.IsNullOrEmpty(staticText)) { OnTextChanged(staticText); _subscribed = true; return; }
            if (KeyboardController.Instance == null) return;

            KeyboardController.Instance.TextChanged += OnTextChanged;
            OnTextChanged(KeyboardController.Instance.Text);
            _subscribed = true;
            Debug.Log("[VirtualMonitor] Subscribed to KeyboardController");
        }

        void OnDestroy()
        {
            if (KeyboardController.Instance != null && _subscribed)
                KeyboardController.Instance.TextChanged -= OnTextChanged;
        }

        void OnTextChanged(string text)
        {
            if (_tmp != null) _tmp.text = text;
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

            // Monitor's local +Z faces the user (LookRotation aligns local +Z
            // with the supplied forward; we pass -fwd so local +Z = -fwd =
            // backward along the user's gaze = TOWARD them.)
            transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
        }

        void BuildMonitor()
        {
            // Local +Z direction faces the user. Bezel sits at the back,
            // screen face is in front of it.

            // ── Bezel (slightly larger backing, behind the screen) ──────
            var bezel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bezel.name  = "Bezel";
            bezel.layer = emsTouchableBezel ? contactLayer : 0;
            bezel.transform.SetParent(transform, false);
            bezel.transform.localPosition = new Vector3(0f, 0f, -screenDepth * 0.5f);
            bezel.transform.localScale    = new Vector3(
                screenWidth + bezelThickness * 2f,
                screenHeight + bezelThickness * 2f,
                screenDepth);
            bezel.GetComponent<Renderer>().material = MakeMaterial(bezelColor);
            bezel.GetComponent<BoxCollider>().isTrigger = true;

            // ── Screen face (thin panel IN FRONT of bezel — visible to user) ──
            var screen = GameObject.CreatePrimitive(PrimitiveType.Cube);
            screen.name  = "Screen";
            screen.layer = 0;
            Destroy(screen.GetComponent<BoxCollider>());
            screen.transform.SetParent(transform, false);
            // Position so the front face of the screen sits AT local origin
            screen.transform.localPosition = new Vector3(0f, 0f, -0.0010f);
            screen.transform.localScale    = new Vector3(screenWidth, screenHeight, 0.002f);
            screen.GetComponent<Renderer>().material = MakeMaterial(screenColor);

            // ── Text (just in front of screen face, facing the user) ────────
            var textGo = new GameObject("MonitorText");
            textGo.transform.SetParent(transform, false);
            // 1 mm in front of local origin = in front of the screen face
            textGo.transform.localPosition = new Vector3(0f, 0f, 0.0015f);
            // Default TMP front = local -Z. Monitor's local +Z faces user.
            // To make TMP front face local +Z, rotate 180° around Y.
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

            // sizeDelta in TMP-units. With labelScale=0.001, 1 unit = 1 mm.
            float marginM = 0.012f;
            _tmp.rectTransform.sizeDelta = new Vector2(
                (screenWidth  - marginM * 2f) / labelScale,
                (screenHeight - marginM * 2f) / labelScale);

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
