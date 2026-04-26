using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace JupiterBridge.Subway
{
    /// <summary>
    /// Procedurally builds a full QWERTY keyboard. EVERY size value comes from
    /// <see cref="JupiterTouchSizing"/> — there are no per-instance public
    /// dimension fields, so changes in code apply to all existing instances
    /// (Unity can't override what doesn't exist as a serialized field).
    ///
    /// Each key is structured as:
    ///   Key_X  (parent transform, no scale)
    ///   ├── Body  (Cube — scaled to key dims, has VirtualKey + BoxCollider
    ///   │          trigger; lives on the EMS-Contact layer)
    ///   └── Label (TextMeshPro 3D — sibling of body, no scale inheritance)
    /// </summary>
    public class VirtualKeyboard : MonoBehaviour
    {
        // ── Things that DO change between deployments are still public ────
        [Header("Visual style")]
        public Color baseplateColor = new Color(0.04f, 0.04f, 0.05f);
        public Color labelColor     = new Color(0.95f, 0.97f, 1.00f);

        [Header("Auto-anchor")]
        [Tooltip("On Start, position the keyboard relative to the main camera.")]
        public bool autoAnchorToCamera = true;

        // ── Layout (label, char, widthMultiplier). '\b' = backspace, '\n' = enter.
        struct K { public string s; public char c; public float w; public K(string s, char c, float w=1f){this.s=s; this.c=c; this.w=w;} }

        static readonly K[][] Rows = new[]
        {
            new[] {  // numbers
                new K("1",'1'), new K("2",'2'), new K("3",'3'), new K("4",'4'), new K("5",'5'),
                new K("6",'6'), new K("7",'7'), new K("8",'8'), new K("9",'9'), new K("0",'0'),
            },
            new[] {  // top letters
                new K("Q",'q'), new K("W",'w'), new K("E",'e'), new K("R",'r'), new K("T",'t'),
                new K("Y",'y'), new K("U",'u'), new K("I",'i'), new K("O",'o'), new K("P",'p'),
            },
            new[] {  // home row
                new K("A",'a'), new K("S",'s'), new K("D",'d'), new K("F",'f'), new K("G",'g'),
                new K("H",'h'), new K("J",'j'), new K("K",'k'), new K("L",'l'),
            },
            new[] {  // bottom letters
                new K("Z",'z'), new K("X",'x'), new K("C",'c'), new K("V",'v'),
                new K("B",'b'), new K("N",'n'), new K("M",'m'),
                new K(",",','), new K(".",'.'),
            },
            new[] {  // control row (short labels so they fit on wide keys)
                new K("Bk",    '\b', JupiterTouchSizing.WideKeyWidthMultiplier),
                new K("space", ' ',  JupiterTouchSizing.SpaceKeyWidthMultiplier),
                new K("Ent",   '\n', JupiterTouchSizing.WideKeyWidthMultiplier),
            },
        };

        // ── Internal ──────────────────────────────────────────────────────
        readonly List<VirtualKey> _keys = new List<VirtualKey>();
        Shader _litShader;

        // ──────────────────────────────────────────────────────────────────

        void Start()
        {
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

            Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

            var off = JupiterTouchSizing.KeyboardAnchorOffset;
            transform.position = cam.transform.position
                + right * off.x + Vector3.up * off.y + fwd * off.z;

            transform.rotation = Quaternion.LookRotation(fwd, Vector3.up)
                               * Quaternion.Euler(-JupiterTouchSizing.KeyboardTiltDegrees, 0f, 0f);
        }

        void BuildKeyboard()
        {
            float keyW   = JupiterTouchSizing.KeyWidthM;
            float keyD   = JupiterTouchSizing.KeyDepthM;
            float pitchZ = keyD + JupiterTouchSizing.KeyGapM;
            int   rowCount = Rows.Length;

            for (int r = 0; r < rowCount; r++)
            {
                var row = Rows[r];

                // Total row width handles wide keys
                float totalW = 0f;
                for (int c = 0; c < row.Length; c++)
                    totalW += row[c].w * keyW + (c > 0 ? JupiterTouchSizing.KeyGapM : 0f);

                float z = ((rowCount - 1) * 0.5f - r) * pitchZ;

                float cursorX = -totalW * 0.5f;
                for (int c = 0; c < row.Length; c++)
                {
                    float thisKeyW = row[c].w * keyW;
                    float xCenter = cursorX + thisKeyW * 0.5f;
                    SpawnKey(row[c].s, row[c].c, xCenter, z, thisKeyW);
                    cursorX += thisKeyW + JupiterTouchSizing.KeyGapM;
                }
            }

            BuildBasePlate();

            Debug.Log(
                $"[VirtualKeyboard] Built {_keys.Count} keys. " +
                $"Letter cap={JupiterTouchSizing.KeyLetterCapHeightMm}mm " +
                $"(fontSize={JupiterTouchSizing.KeyLetterFontSize:F1}), " +
                $"Wide cap={JupiterTouchSizing.KeyWideCapHeightMm}mm " +
                $"(fontSize={JupiterTouchSizing.KeyWideFontSize:F1}). " +
                $"Key {JupiterTouchSizing.KeyWidthM*1000:F0}×{JupiterTouchSizing.KeyDepthM*1000:F0}×{JupiterTouchSizing.KeyHeightM*1000:F0} mm, " +
                $"gap {JupiterTouchSizing.KeyGapM*1000:F0}mm");
        }

        void SpawnKey(string label, char ch, float x, float z, float width)
        {
            float keyHeight = JupiterTouchSizing.KeyHeightM;
            float keyDepth  = JupiterTouchSizing.KeyDepthM;

            // Parent
            var keyParent = new GameObject($"Key_{NormalizeName(label)}");
            keyParent.transform.SetParent(transform, false);
            keyParent.transform.localPosition = new Vector3(x, 0f, z);
            keyParent.transform.localRotation = Quaternion.identity;

            // Body cube
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.layer = JupiterTouchSizing.EmsContactLayer;
            body.transform.SetParent(keyParent.transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale    = new Vector3(width, keyHeight, keyDepth);
            body.GetComponent<Renderer>().material = MakeMaterial(new Color(0.10f, 0.10f, 0.12f));

            var key = body.AddComponent<VirtualKey>();
            key.keyChar = ch;
            key.label   = label;
            _keys.Add(key);

            // Label (sibling of body)
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(keyParent.transform, false);
            labelGo.transform.localPosition = new Vector3(
                0f,
                keyHeight * 0.5f + JupiterTouchSizing.KeyboardLabelLiftMm * 0.001f,
                0f);
            // TMP's readable face is local -Z (verified: Unity's default scene
            // camera sits at world -Z and renders TMP correctly).
            // +90° around X takes local +Z → world -Y, so local -Z → world +Y.
            // Net result: text faces UP, reads world +X (not mirrored), text-top
            // points away from the user (book-on-table orientation, not upside-down).
            labelGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // Compensate for any inherited parent scale
            float parentScale = Mathf.Max(0.0001f, keyParent.transform.lossyScale.x);
            labelGo.transform.localScale = Vector3.one * (JupiterTouchSizing.LabelScale / parentScale);

            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.text             = label;
            tmp.alignment        = TextAlignmentOptions.Center;
            tmp.color            = labelColor;
            tmp.fontStyle        = FontStyles.Normal;
            tmp.enableAutoSizing = false;
            tmp.fontSize         = (label.Length > 1)
                                     ? JupiterTouchSizing.KeyWideFontSize
                                     : JupiterTouchSizing.KeyLetterFontSize;
            tmp.enableWordWrapping = false;
            tmp.overflowMode       = TextOverflowModes.Overflow;

            // Generously oversized rect — text always fits even if larger than the key.
            // Overflow mode never clips, so this just ensures TMP's layout doesn't
            // get into degenerate states.
            tmp.rectTransform.sizeDelta = new Vector2(200f, 200f);

            tmp.ForceMeshUpdate();
        }

        void BuildBasePlate()
        {
            float keyW   = JupiterTouchSizing.KeyWidthM;
            float keyGap = JupiterTouchSizing.KeyGapM;

            float maxRowWidth = 0f;
            for (int r = 0; r < Rows.Length; r++)
            {
                var row = Rows[r];
                float w = 0f;
                for (int c = 0; c < row.Length; c++)
                    w += row[c].w * keyW + (c > 0 ? keyGap : 0f);
                if (w > maxRowWidth) maxRowWidth = w;
            }

            float plateW = maxRowWidth + keyW * 0.4f;
            float plateD = Rows.Length * (JupiterTouchSizing.KeyDepthM + keyGap) + keyW * 0.3f;

            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name  = "BasePlate";
            plate.layer = 0;
            Destroy(plate.GetComponent<BoxCollider>());
            plate.transform.SetParent(transform, false);
            plate.transform.localPosition = new Vector3(
                0f, -JupiterTouchSizing.KeyHeightM * 0.5f - 0.003f, 0f);
            plate.transform.localScale    = new Vector3(plateW, 0.005f, plateD);
            plate.GetComponent<Renderer>().material = MakeMaterial(baseplateColor);
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
                "Universal Render Pipeline/Lit", "Standard", "Mobile/Diffuse", "Unlit/Color",
            };
            foreach (var n in candidates)
            {
                var sh = Shader.Find(n);
                if (sh != null) return sh;
            }
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var s = tmp.GetComponent<Renderer>().sharedMaterial.shader;
            Destroy(tmp);
            return s;
        }

        static string NormalizeName(string s)
        {
            switch (s)
            {
                case "Bk":    return "Backspace";
                case "Ent":   return "Enter";
                case "space": return "Space";
                case ",":     return "Comma";
                case ".":     return "Period";
                default:      return s;
            }
        }
    }
}
