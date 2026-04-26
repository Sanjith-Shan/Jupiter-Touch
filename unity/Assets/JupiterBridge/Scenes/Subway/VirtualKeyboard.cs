using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace JupiterBridge.Subway
{
    /// <summary>
    /// Procedurally builds a full QWERTY keyboard with number row, letter rows,
    /// and a control row (backspace, space, enter, comma, period). Each key is
    /// structured as:
    ///
    ///   Key_X  (parent transform, no scale)
    ///   ├── Body  (Cube primitive — scaled to key dimensions, has VirtualKey
    ///   │         + BoxCollider trigger; lives on layer 6 for EMS pickup)
    ///   └── Label (TextMeshPro 3D — positioned above the top face, no scale
    ///              inheritance issues since it's a sibling of Body, not a child)
    ///
    /// The keyboard root tilts ~22° toward the user (real keyboards tilt back
    /// 5-10° and we exaggerate slightly so labels are readable in VR).
    /// </summary>
    public class VirtualKeyboard : MonoBehaviour
    {
        [Header("Key dimensions (metres)")]
        [Tooltip("Width and depth of one standard key. 24-30 mm is the VR sweet spot.")]
        public float keyWidth  = 0.026f;
        public float keyDepth  = 0.026f;
        [Tooltip("Height of the key body — affects how 'deep' a press is.")]
        public float keyHeight = 0.010f;
        [Tooltip("Gap between adjacent keys (centre-to-centre = keyWidth + keyGap).")]
        public float keyGap    = 0.005f;

        [Header("Layer")]
        [Tooltip("Unity layer for keys. Must match the EMS Contact layer (6 in Jupiter Touch).")]
        public int contactLayer = 6;

        [Header("Visual")]
        public Color baseplateColor = new Color(0.04f, 0.04f, 0.05f);
        public Color labelColor     = new Color(0.95f, 0.97f, 1.00f);
        [Tooltip("Label transform scale. 0.001 means 1 TMP unit = 1 mm world.")]
        public float labelScale     = 0.001f;
        [Tooltip("TMP fontSize in POINT units (like 12pt, 24pt). With labelScale=0.001, fontSize 24 ≈ 17 mm capital height.")]
        public float labelFontSize  = 24f;
        [Tooltip("If true, shrink the font on multi-character labels (e.g. 'space') so they fit.")]
        public bool  shrinkLongLabels = true;
        [Tooltip("Multi-char shrink factor (used when shrinkLongLabels is on).")]
        [Range(0.3f, 1f)] public float longLabelShrink = 0.55f;

        [Header("Tilt & anchor")]
        [Tooltip("Tilt the whole keyboard back by this many degrees (toward user's face).")]
        public float tiltDegrees = 25f;

        [Header("Auto-anchor (camera-relative)")]
        [Tooltip("On Start, position the keyboard relative to the main camera using the offset below.")]
        public bool    autoAnchorToCamera = true;
        [Tooltip("Offset from camera: x = right, y = up, z = forward (metres).")]
        public Vector3 cameraAnchorOffset = new Vector3(0.30f, -0.35f, 0.45f);

        // ── Layout ───────────────────────────────────────────────────────
        // Each entry: (label, char, widthMultiplier).  '\b' = backspace, '\n' = enter.
        struct K { public string s; public char c; public float w; public K(string s, char c, float w=1f){this.s=s; this.c=c; this.w=w;} }

        static readonly K[][] Rows = new[]
        {
            // Number row
            new[] {
                new K("1",'1'), new K("2",'2'), new K("3",'3'), new K("4",'4'), new K("5",'5'),
                new K("6",'6'), new K("7",'7'), new K("8",'8'), new K("9",'9'), new K("0",'0'),
            },
            // Top letter row
            new[] {
                new K("Q",'q'), new K("W",'w'), new K("E",'e'), new K("R",'r'), new K("T",'t'),
                new K("Y",'y'), new K("U",'u'), new K("I",'i'), new K("O",'o'), new K("P",'p'),
            },
            // Home row
            new[] {
                new K("A",'a'), new K("S",'s'), new K("D",'d'), new K("F",'f'), new K("G",'g'),
                new K("H",'h'), new K("J",'j'), new K("K",'k'), new K("L",'l'),
            },
            // Bottom letter row
            new[] {
                new K("Z",'z'), new K("X",'x'), new K("C",'c'), new K("V",'v'),
                new K("B",'b'), new K("N",'n'), new K("M",'m'),
                new K(",",','), new K(".",'.'),
            },
            // Control row: backspace, space, enter
            new[] {
                new K("⌫", '\b', 1.6f),
                new K("space", ' ', 6.0f),
                new K("⏎", '\n', 1.6f),
            },
        };

        // ── State ────────────────────────────────────────────────────────
        readonly List<VirtualKey> _keys = new List<VirtualKey>();
        Shader _litShader;

        // ──────────────────────────────────────────────────────────────────

        void Start()
        {
            // Ensure a KeyboardController exists in the scene
            if (KeyboardController.Instance == null)
                gameObject.AddComponent<KeyboardController>();

            _litShader = ResolveShader();

            if (autoAnchorToCamera) AnchorToCamera();
            BuildKeyboard();
        }

        void AnchorToCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 fwd   = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            // In Unity (left-handed): Cross(up, fwd) = right
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

            transform.position = cam.transform.position
                + right * cameraAnchorOffset.x
                + Vector3.up * cameraAnchorOffset.y
                + fwd * cameraAnchorOffset.z;

            // Face the keyboard toward the user, tilted back so labels are readable
            transform.rotation = Quaternion.LookRotation(fwd, Vector3.up)
                               * Quaternion.Euler(-tiltDegrees, 0f, 0f);
        }

        void BuildKeyboard()
        {
            float pitchX = keyWidth + keyGap;
            float pitchZ = keyDepth + keyGap;

            // Centre the keyboard in front of the user. Z=0 is the centre row;
            // top row is at +Z, bottom row at -Z (toward the user).
            int rowCount = Rows.Length;

            for (int r = 0; r < rowCount; r++)
            {
                var row = Rows[r];

                // Compute total row width (handles wide keys)
                float totalW = 0f;
                for (int c = 0; c < row.Length; c++)
                    totalW += row[c].w * keyWidth + (c > 0 ? keyGap : 0f);

                // Z position: row 0 (numbers) at the BACK, last row at the FRONT
                float z = ((rowCount - 1) * 0.5f - r) * pitchZ;

                // Build keys left-to-right
                float cursorX = -totalW * 0.5f;
                for (int c = 0; c < row.Length; c++)
                {
                    float keyW = row[c].w * keyWidth;
                    float xCenter = cursorX + keyW * 0.5f;

                    SpawnKey(row[c].s, row[c].c, xCenter, z, keyW);

                    cursorX += keyW + keyGap;
                }
            }

            BuildBasePlate();

            Debug.Log($"[VirtualKeyboard] Built {_keys.Count} keys");
        }

        void SpawnKey(string label, char ch, float x, float z, float width)
        {
            // ── Parent (transform only, no scale)
            var keyParent = new GameObject($"Key_{NormalizeName(label)}");
            keyParent.transform.SetParent(transform, false);
            keyParent.transform.localPosition = new Vector3(x, 0f, z);
            keyParent.transform.localRotation = Quaternion.identity;

            // ── Body (cube, scaled, has VirtualKey + collider)
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.layer = contactLayer;
            body.transform.SetParent(keyParent.transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale    = new Vector3(width, keyHeight, keyDepth);
            body.GetComponent<Renderer>().material = MakeKeyMaterial();

            var key = body.AddComponent<VirtualKey>();
            key.keyChar = ch;
            key.label   = label;
            _keys.Add(key);

            // ── Label (TMP 3D, sibling of body — no scale inheritance issues)
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(keyParent.transform, false);
            // Position just above the top face of the body
            labelGo.transform.localPosition = new Vector3(0f, keyHeight * 0.5f + 0.0010f, 0f);
            // Orient: text front (+Z) faces world UP, text up (+Y) faces FORWARD
            // (away from user). Reading direction stays world +X.
            labelGo.transform.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            // labelScale = 0.001 → 1 TMP unit (mesh space) = 1 mm world.
            // Compensate for any non-unit scale inherited from parents in the
            // hierarchy (XR rig, scene root, etc.) so glyphs always render at
            // the intended physical size regardless of where the keyboard sits.
            float parentScale = Mathf.Max(0.0001f, keyParent.transform.lossyScale.x);
            labelGo.transform.localScale = Vector3.one * (labelScale / parentScale);

            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.text             = label;
            tmp.alignment        = TextAlignmentOptions.Center;
            tmp.color            = labelColor;
            // Skip Bold — it can trigger missing-glyph fallback if the asset
            // doesn't ship a bold variant. Plain works on every TMP install.
            tmp.fontStyle        = FontStyles.Normal;
            // FIXED font size — autoSizing collapses to fontSizeMin and renders garbage
            tmp.enableAutoSizing = false;
            tmp.fontSize         = (shrinkLongLabels && label.Length > 1)
                                     ? labelFontSize * longLabelShrink
                                     : labelFontSize;
            // Disable margins/word-wrap so a single character isn't word-wrapped weirdly
            tmp.enableWordWrapping = false;
            tmp.overflowMode       = TextOverflowModes.Overflow;
            // sizeDelta in TMP-units. With labelScale=0.001, 1 TMP unit = 1 mm.
            //   World rect (m) = sizeDelta * labelScale
            //   For a 26 mm key: sizeDelta = 26 * 0.95 = 24.7 TMP units
            tmp.rectTransform.sizeDelta = new Vector2(
                (width    / labelScale) * 0.95f,
                (keyDepth / labelScale) * 0.95f);

            // Force the SDF mesh to regenerate now that all properties are set —
            // some Unity / TMP versions otherwise wait until next frame and
            // occasionally render the un-textured fallback at frame 0.
            tmp.ForceMeshUpdate();
        }

        void BuildBasePlate()
        {
            // Compute the full footprint of all keys
            float maxRowWidth = 0f;
            for (int r = 0; r < Rows.Length; r++)
            {
                var row = Rows[r];
                float w = 0f;
                for (int c = 0; c < row.Length; c++)
                    w += row[c].w * keyWidth + (c > 0 ? keyGap : 0f);
                if (w > maxRowWidth) maxRowWidth = w;
            }

            float plateW = maxRowWidth + keyWidth * 0.4f;
            float plateD = Rows.Length * (keyDepth + keyGap) + keyWidth * 0.3f;

            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name  = "BasePlate";
            plate.layer = 0;  // not on EMS layer — purely visual
            Destroy(plate.GetComponent<BoxCollider>());  // strip collider so finger tracking ignores it
            plate.transform.SetParent(transform, false);
            plate.transform.localPosition = new Vector3(0f, -keyHeight * 0.5f - 0.003f, 0f);
            plate.transform.localScale    = new Vector3(plateW, 0.005f, plateD);

            var mat = MakeMaterial(baseplateColor);
            plate.GetComponent<Renderer>().material = mat;
        }

        // ── Materials ────────────────────────────────────────────────────

        Material MakeKeyMaterial()
        {
            // Use a fresh material per key so each one can flash independently
            var c = new Color(0.10f, 0.10f, 0.12f);
            return MakeMaterial(c);
        }

        Material MakeMaterial(Color c)
        {
            var mat = new Material(_litShader);
            mat.color = c;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            return mat;
        }

        Shader ResolveShader()
        {
            string[] candidates = {
                "Universal Render Pipeline/Lit",
                "Standard",
                "Mobile/Diffuse",
                "Unlit/Color",
            };
            foreach (var n in candidates)
            {
                var sh = Shader.Find(n);
                if (sh != null) return sh;
            }
            // Last resort: pull from a temp primitive
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var s = tmp.GetComponent<Renderer>().sharedMaterial.shader;
            Destroy(tmp);
            return s;
        }

        // Replace special chars in GameObject names so they look clean in the Hierarchy
        static string NormalizeName(string s)
        {
            switch (s)
            {
                case "⌫": return "Backspace";
                case "⏎": return "Enter";
                case "space": return "Space";
                case ",": return "Comma";
                case ".": return "Period";
                default: return s;
            }
        }
    }
}
