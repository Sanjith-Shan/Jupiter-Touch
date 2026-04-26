using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace JupiterBridge.Subway
{
    /// <summary>
    /// Procedurally builds a floating QWERTY keyboard at this GameObject's
    /// transform on Start. Each key is a child cube with a VirtualKey + a
    /// TextMeshPro label, placed on layer 6 so the EMS pipeline picks up
    /// finger contacts automatically.
    ///
    /// Spawns a KeyboardController on the same GameObject if one isn't
    /// already present (singleton).
    /// </summary>
    public class VirtualKeyboard : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Width of one key (metres) — standard real keys are ~0.018 m.")]
        public float keyWidth     = 0.024f;
        [Tooltip("Depth (front→back) of one key (metres).")]
        public float keyDepth     = 0.024f;
        [Tooltip("Height of the key body (metres). Affects how 'deep' a press is.")]
        public float keyHeight    = 0.015f;
        [Tooltip("Gap between adjacent keys (metres).")]
        public float keyGap       = 0.003f;

        [Header("Layer")]
        [Tooltip("Unity layer to assign to every key. Must match the 'EMS Contact' layer.")]
        public int contactLayer   = 6;

        // ── Layout definition ────────────────────────────────────────────
        // Each row: list of (label, char). Use '\b' for backspace, '\n' for enter.
        static readonly (string label, char ch)[][] Rows = new[]
        {
            new[] {
                ("Q",'q'),("W",'w'),("E",'e'),("R",'r'),("T",'t'),
                ("Y",'y'),("U",'u'),("I",'i'),("O",'o'),("P",'p'),
            },
            new[] {
                ("A",'a'),("S",'s'),("D",'d'),("F",'f'),("G",'g'),
                ("H",'h'),("J",'j'),("K",'k'),("L",'l'),
            },
            new[] {
                ("Z",'z'),("X",'x'),("C",'c'),("V",'v'),("B",'b'),("N",'n'),("M",'m'),
            },
        };

        // ── Internal ─────────────────────────────────────────────────────
        readonly List<VirtualKey> _keys = new List<VirtualKey>();

        void Start()
        {
            // Ensure a KeyboardController exists in the scene
            if (KeyboardController.Instance == null)
                gameObject.AddComponent<KeyboardController>();

            BuildKeyboard();
        }

        void BuildKeyboard()
        {
            // Letter rows: place centered around local (0,0,0)
            float pitchX = keyWidth + keyGap;
            float pitchZ = keyDepth + keyGap;

            for (int r = 0; r < Rows.Length; r++)
            {
                var row = Rows[r];
                float rowWidth = row.Length * pitchX - keyGap;
                float startX   = -rowWidth * 0.5f + keyWidth * 0.5f;
                // Top row at +Z (away from user), bottom row at -Z.
                // Subway scenario: user looking forward along +Z, so "Q" row is farthest.
                float z = (Rows.Length - 1 - r) * pitchZ - (Rows.Length - 1) * pitchZ * 0.5f;

                for (int c = 0; c < row.Length; c++)
                {
                    float x = startX + c * pitchX;
                    SpawnKey(row[c].label, row[c].ch,
                        new Vector3(x, 0, z),
                        new Vector3(keyWidth, keyHeight, keyDepth));
                }
            }

            // Bottom row: backspace (left), space (centered, wide), enter (right)
            float bottomZ = (Rows.Length) * pitchZ - (Rows.Length - 1) * pitchZ * 0.5f;
            bottomZ = -bottomZ;  // one row below the Z row

            float spaceWidth = keyWidth * 5f + keyGap * 4f;
            float wideKeyW   = keyWidth * 1.6f;

            SpawnKey("⌫",  '\b',
                new Vector3(-spaceWidth * 0.5f - keyGap - wideKeyW * 0.5f, 0, bottomZ),
                new Vector3(wideKeyW, keyHeight, keyDepth));

            SpawnKey("space", ' ',
                new Vector3(0, 0, bottomZ),
                new Vector3(spaceWidth, keyHeight, keyDepth));

            SpawnKey("⏎",   '\n',
                new Vector3(spaceWidth * 0.5f + keyGap + wideKeyW * 0.5f, 0, bottomZ),
                new Vector3(wideKeyW, keyHeight, keyDepth));

            // Optional base plate so the keyboard reads as a unit
            BuildBasePlate(pitchX, pitchZ, bottomZ);

            Debug.Log($"[VirtualKeyboard] Built {_keys.Count} keys");
        }

        void SpawnKey(string label, char ch, Vector3 localPos, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Key_{label}";
            go.layer = contactLayer;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale    = size;

            // Replace the default material with an unlit dark material
            var rend = go.GetComponent<Renderer>();
            rend.material = MakeDarkMaterial();

            // VirtualKey component
            var key = go.AddComponent<VirtualKey>();
            key.keyChar = ch;
            key.label   = label;
            _keys.Add(key);

            // Label on top face
            BuildLabel(go.transform, label, size);
        }

        void BuildLabel(Transform parent, string text, Vector3 keySize)
        {
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(parent, false);
            // Position slightly above the top face so it doesn't z-fight
            labelGo.transform.localPosition = new Vector3(0, 0.51f, 0);
            // Lay flat: rotate so X faces forward when looking down
            labelGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // Scale invariant of parent's non-uniform scale: counter-scale
            labelGo.transform.localScale = new Vector3(
                1f / Mathf.Max(0.001f, keySize.x),
                1f / Mathf.Max(0.001f, keySize.y),
                1f / Mathf.Max(0.001f, keySize.z)
            );

            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            // The world-space size is now 1 unit per axis; pick a font size that fits
            tmp.fontSize = 0.5f * Mathf.Min(keySize.x, keySize.z) * 200f;
            tmp.color = new Color(0.92f, 0.92f, 0.95f);
            tmp.enableWordWrapping = false;
            tmp.rectTransform.sizeDelta = new Vector2(0.9f, 0.9f);
        }

        void BuildBasePlate(float pitchX, float pitchZ, float bottomZ)
        {
            float topZ      = (Rows.Length - 1) * pitchZ * 0.5f + pitchZ * 0.5f;
            float plateZMin = bottomZ - pitchZ * 0.6f;
            float plateZMax = topZ + pitchZ * 0.1f;
            float plateW    = 12 * keyWidth + 11 * keyGap + keyWidth * 0.4f;
            float plateD    = plateZMax - plateZMin;

            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "BasePlate";
            plate.layer = 0;  // not on EMS layer — purely cosmetic
            // Strip the box collider so it doesn't interfere with finger tracking
            Destroy(plate.GetComponent<BoxCollider>());
            plate.transform.SetParent(transform, false);
            plate.transform.localPosition = new Vector3(0, -keyHeight * 0.5f - 0.004f, (plateZMin + plateZMax) * 0.5f);
            plate.transform.localScale    = new Vector3(plateW, 0.006f, plateD);

            var rend = plate.GetComponent<Renderer>();
            var mat = MakeDarkMaterial();
            mat.color = new Color(0.05f, 0.05f, 0.06f);
            rend.material = mat;
        }

        Material MakeDarkMaterial()
        {
            // Try URP Lit first, then Standard, then Mobile/Diffuse
            string[] candidates = {
                "Universal Render Pipeline/Lit", "Standard", "Mobile/Diffuse", "Unlit/Color"
            };
            Shader sh = null;
            foreach (var n in candidates) { sh = Shader.Find(n); if (sh != null) break; }
            var mat = new Material(sh);
            var c = new Color(0.10f, 0.10f, 0.12f);
            mat.color = c;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            return mat;
        }
    }
}
