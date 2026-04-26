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
    /// fires a mild EMS pulse — sells the "real object" feel.
    /// </summary>
    public class VirtualMonitor : MonoBehaviour
    {
        [Header("Geometry")]
        public float screenWidth   = 0.42f;   // metres
        public float screenHeight  = 0.27f;   // ~16:10
        public float screenDepth   = 0.02f;   // panel thickness
        public float bezelThickness = 0.012f;

        [Header("Display")]
        [Tooltip("If empty, subscribes to KeyboardController.Instance text.")]
        [TextArea(3, 8)] public string staticText = "";
        public Color textColor       = new Color(0.85f, 1f, 0.95f);
        public Color screenColor     = new Color(0.04f, 0.05f, 0.06f);
        public Color bezelColor      = new Color(0.12f, 0.12f, 0.14f);
        [Range(8, 96)] public int fontSize = 36;

        [Header("EMS")]
        [Tooltip("Put the bezel on layer 6 so touching the monitor's frame fires EMS.")]
        public bool emsTouchableBezel = true;
        public int  contactLayer = 6;

        TextMeshPro _tmp;

        void Start()
        {
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

            // Bezel collider becomes a trigger so finger tracking still flows through
            var bezelCol = bezel.GetComponent<BoxCollider>();
            bezelCol.isTrigger = true;

            // ── Screen face ──────────────────────────────────────────────
            var screen = GameObject.CreatePrimitive(PrimitiveType.Cube);
            screen.name = "Screen";
            screen.layer = 0;  // do not double-fire EMS on the inner panel
            // Strip its collider — bezel handles touch; we don't want overlapping triggers
            Destroy(screen.GetComponent<BoxCollider>());
            screen.transform.SetParent(transform, false);
            screen.transform.localPosition = new Vector3(0, 0, -screenDepth * 0.45f);
            screen.transform.localScale = new Vector3(screenWidth, screenHeight, screenDepth * 0.5f);
            screen.GetComponent<Renderer>().material = MakeMaterial(screenColor);

            // ── Text element on front face ───────────────────────────────
            var textGo = new GameObject("MonitorText");
            textGo.transform.SetParent(transform, false);
            textGo.transform.localPosition = new Vector3(0, 0, -screenDepth * 0.7f);
            textGo.transform.localRotation = Quaternion.identity;

            _tmp = textGo.AddComponent<TextMeshPro>();
            _tmp.text = "";
            _tmp.alignment = TextAlignmentOptions.TopLeft;
            _tmp.color = textColor;
            _tmp.fontSize = fontSize;
            _tmp.enableWordWrapping = true;
            _tmp.overflowMode = TextOverflowModes.Truncate;

            // Size the text rect to fit the screen with margins
            var rt = _tmp.rectTransform;
            float margin = 0.02f;
            rt.sizeDelta = new Vector2(screenWidth - margin * 2f, screenHeight - margin * 2f);
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
