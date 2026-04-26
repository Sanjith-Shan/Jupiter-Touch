using TMPro;
using UnityEngine;

namespace JupiterBridge.Subway
{
    /// <summary>
    /// Floating monitor that shows the current text from KeyboardController.
    /// Every dimension comes from <see cref="JupiterTouchSizing"/> — there are
    /// no public sizing fields, so changes apply universally.
    ///
    /// Z layout (in this transform's LOCAL space, where local +Z faces the user):
    ///   Bezel  at z = -0.010   ─── back panel, behind everything
    ///   Screen at z = -0.001   ─── thin emissive face, visible to user
    ///   Text   at z = +0.0015  ─── 1.5 mm in front of the screen face
    /// </summary>
    public class VirtualMonitor : MonoBehaviour
    {
        // ── Things that can sensibly vary per instance stay public ────────
        [Header("Display")]
        [Tooltip("If non-empty, shows this text statically and ignores the keyboard.")]
        [TextArea(3, 8)] public string staticText = "";
        public Color textColor   = new Color(0.85f, 1.00f, 0.95f);
        public Color screenColor = new Color(0.04f, 0.05f, 0.06f);
        public Color bezelColor  = new Color(0.12f, 0.12f, 0.14f);

        [Header("Behaviour")]
        public bool emsTouchableBezel = true;
        public bool autoAnchorToCamera = true;

        // ── Internal ──────────────────────────────────────────────────────
        TextMeshPro _tmp;
        bool        _subscribed;

        void Start()
        {
            if (autoAnchorToCamera) AnchorToCamera();
            BuildMonitor();
            TrySubscribe();
        }

        void Update()
        {
            // Retry until KeyboardController exists (handles spawn-order races)
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
            if (_subscribed && KeyboardController.Instance != null)
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

            var off = JupiterTouchSizing.MonitorAnchorOffset;
            transform.position = cam.transform.position
                + right * off.x + Vector3.up * off.y + fwd * off.z;

            // Local +Z = -fwd → faces back toward user
            transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
        }

        void BuildMonitor()
        {
            float screenW = JupiterTouchSizing.MonitorScreenWidthM;
            float screenH = JupiterTouchSizing.MonitorScreenHeightM;
            float screenD = JupiterTouchSizing.MonitorScreenDepthM;
            float bezelT  = JupiterTouchSizing.MonitorBezelM;
            float marginM = JupiterTouchSizing.MonitorTextMarginM;

            // ── Bezel (back panel)
            var bezel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bezel.name  = "Bezel";
            bezel.layer = emsTouchableBezel ? JupiterTouchSizing.EmsContactLayer : 0;
            bezel.transform.SetParent(transform, false);
            bezel.transform.localPosition = new Vector3(0f, 0f, -screenD * 0.5f);
            bezel.transform.localScale    = new Vector3(
                screenW + bezelT * 2f,
                screenH + bezelT * 2f,
                screenD);
            bezel.GetComponent<Renderer>().material = MakeMaterial(bezelColor);
            bezel.GetComponent<BoxCollider>().isTrigger = true;

            // ── Screen face — pushed 1 mm proud of the bezel front face
            //                  to avoid Z-fighting at z=0
            var screen = GameObject.CreatePrimitive(PrimitiveType.Cube);
            screen.name  = "Screen";
            screen.layer = 0;
            Destroy(screen.GetComponent<BoxCollider>());
            screen.transform.SetParent(transform, false);
            screen.transform.localPosition = new Vector3(0f, 0f, 0.0010f);
            screen.transform.localScale    = new Vector3(screenW, screenH, 0.001f);
            screen.GetComponent<Renderer>().material = MakeMaterial(screenColor);

            // ── Text — 0.5 mm in front of screen face
            var textGo = new GameObject("MonitorText");
            textGo.transform.SetParent(transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, 0.0020f);
            // TMP front (local -Z) → flip to face local +Z (toward user)
            textGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            float parentScale = Mathf.Max(0.0001f, transform.lossyScale.x);
            textGo.transform.localScale = Vector3.one * (JupiterTouchSizing.LabelScale / parentScale);

            _tmp = textGo.AddComponent<TextMeshPro>();
            _tmp.text             = "";
            _tmp.alignment        = TextAlignmentOptions.TopLeft;
            _tmp.color            = textColor;
            _tmp.fontStyle        = FontStyles.Normal;
            _tmp.enableAutoSizing = false;
            _tmp.fontSize         = JupiterTouchSizing.MonitorTextFontSize;
            _tmp.enableWordWrapping = true;
            _tmp.overflowMode     = TextOverflowModes.Truncate;

            _tmp.rectTransform.sizeDelta = new Vector2(
                (screenW - marginM * 2f) / JupiterTouchSizing.LabelScale,
                (screenH - marginM * 2f) / JupiterTouchSizing.LabelScale);

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
