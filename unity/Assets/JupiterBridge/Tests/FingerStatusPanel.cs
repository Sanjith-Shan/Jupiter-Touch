/*
 * FingerStatusPanel — clean floating HUD for 6 EMS channels.
 *
 * Intensity scale: 0 = hard press (max), 30 = barely touching (min)
 * Maps to pot value where lower = stronger stimulation.
 *
 * Built with world-space Canvas for crisp text in VR.
 * Billboards to face the player.
 */

using UnityEngine;
using UnityEngine.UI;

namespace JupiterBridge.Tests
{
    public class FingerStatusPanel : MonoBehaviour
    {
        [HideInInspector] public JupiterTester tester;
        [HideInInspector] public Vector3 spawnPos;
        [HideInInspector] public Vector3 spawnFwd;
        [HideInInspector] public string  handLabel = "RIGHT HAND";

        private Transform _root;
        private Canvas    _canvas;

        // Per-channel UI
        private Image[]  _barFills   = new Image[6];
        private Image[]  _dots       = new Image[6];
        private Text[]   _labels     = new Text[6];
        private Text[]   _values     = new Text[6];

        private static readonly Color ActiveDot   = new Color(0.1f, 1.0f, 0.45f);
        private static readonly Color InactiveDot = new Color(0.22f, 0.22f, 0.22f);
        private static readonly Color BgColor     = new Color(0.03f, 0.03f, 0.05f, 0.92f);

        void Start()
        {
            Build();
        }

        void Update()
        {
            if (tester == null || _root == null) return;

            // Billboard toward camera
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 dir = _root.position - cam.transform.position;
                if (dir.sqrMagnitude > 0.001f)
                    _root.rotation = Quaternion.Slerp(
                        _root.rotation,
                        Quaternion.LookRotation(dir.normalized),
                        Time.deltaTime * 8f);
            }

            for (int i = 0; i < 6; i++)
            {
                bool  active = tester.IsContacting[i];
                float depth  = tester.ContactDepth[i];

                // Bar fill (0 to 1)
                _barFills[i].fillAmount = active ? depth : 0f;
                _barFills[i].color = active
                    ? Color.Lerp(new Color(0.1f, 0.9f, 0.45f), JupiterTester.FingerColors[i], depth)
                    : new Color(0.15f, 0.15f, 0.15f);

                // Dot
                _dots[i].color = active ? ActiveDot : InactiveDot;

                // Intensity value: 0 = max press, 30 = min press
                if (active)
                {
                    int potValue = Mathf.RoundToInt((1f - depth) * 30f);
                    _values[i].text  = potValue.ToString();
                    _values[i].color = Color.white;
                }
                else
                {
                    _values[i].text  = "—";
                    _values[i].color = new Color(0.35f, 0.35f, 0.35f);
                }

                // Label brightness
                _labels[i].color = active ? Color.white : new Color(0.55f, 0.55f, 0.55f);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  BUILD UI WITH WORLD-SPACE CANVAS
        // ══════════════════════════════════════════════════════════════════

        void Build()
        {
            // Root container
            _root = new GameObject("HUD_Root").transform;
            _root.SetParent(transform);
            _root.position = spawnPos;
            _root.rotation = Quaternion.LookRotation(spawnFwd);

            // Canvas
            var canvasGo = new GameObject("HUD_Canvas");
            canvasGo.transform.SetParent(_root);
            canvasGo.transform.localPosition = Vector3.zero;
            canvasGo.transform.localRotation = Quaternion.identity;
            canvasGo.transform.localScale    = Vector3.one * 0.001f; // 1 pixel = 1mm

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;

            canvasGo.AddComponent<GraphicRaycaster>();

            var canvasRT = canvasGo.GetComponent<RectTransform>();
            canvasRT.sizeDelta = new Vector2(380, 260);

            // Background panel
            var bgGo = CreateUIElement("BG", canvasGo.transform, new Vector2(380, 260));
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.color = BgColor;

            // Title
            var titleGo = CreateUIElement("Title", canvasGo.transform, new Vector2(360, 30));
            var titleRT = titleGo.GetComponent<RectTransform>();
            titleRT.anchoredPosition = new Vector2(0, 100);
            var titleText = titleGo.AddComponent<Text>();
            titleText.text      = $"JUPITER  ·  {handLabel}";
            titleText.font      = Font.CreateDynamicFontFromOSFont("Arial", 20);
            titleText.fontSize  = 20;
            titleText.fontStyle = FontStyle.Bold;
            titleText.color     = new Color(0.92f, 0.92f, 0.92f);
            titleText.alignment = TextAnchor.MiddleCenter;

            // Passthrough hint
            var hintGo = CreateUIElement("Hint", canvasGo.transform, new Vector2(360, 18));
            var hintRT = hintGo.GetComponent<RectTransform>();
            hintRT.anchoredPosition = new Vector2(0, -115);
            var hintText = hintGo.AddComponent<Text>();
            hintText.text      = "Press B to toggle passthrough";
            hintText.font      = Font.CreateDynamicFontFromOSFont("Arial", 12);
            hintText.fontSize  = 12;
            hintText.color     = new Color(0.4f, 0.4f, 0.4f);
            hintText.alignment = TextAnchor.MiddleCenter;

            // Build 6 channel rows
            float startY = 72f;
            float rowH   = 32f;

            for (int i = 0; i < 6; i++)
            {
                float y = startY - i * rowH;
                BuildRow(canvasGo.transform, i, y);
            }
        }

        void BuildRow(Transform parent, int i, float y)
        {
            float leftX = -170f;

            // ── Channel label ──────────────────────────────────────────────
            var labelGo = CreateUIElement($"Label_{i}", parent, new Vector2(120, 24));
            var labelRT = labelGo.GetComponent<RectTransform>();
            labelRT.anchoredPosition = new Vector2(leftX + 60f, y);

            _labels[i] = labelGo.AddComponent<Text>();
            _labels[i].text      = JupiterTester.ChannelLabels[i];
            _labels[i].font      = Font.CreateDynamicFontFromOSFont("Arial", 15);
            _labels[i].fontSize  = 15;
            _labels[i].fontStyle = FontStyle.Normal;
            _labels[i].color     = new Color(0.55f, 0.55f, 0.55f);
            _labels[i].alignment = TextAnchor.MiddleLeft;

            // ── Progress bar background ────────────────────────────────────
            float barX = leftX + 155f;
            float barW = 120f;
            float barH = 14f;

            var barBgGo = CreateUIElement($"BarBg_{i}", parent, new Vector2(barW, barH));
            var barBgRT = barBgGo.GetComponent<RectTransform>();
            barBgRT.anchoredPosition = new Vector2(barX + barW * 0.5f, y);
            var barBgImg = barBgGo.AddComponent<Image>();
            barBgImg.color = new Color(0.08f, 0.08f, 0.08f);

            // ── Progress bar fill ──────────────────────────────────────────
            var barFillGo = CreateUIElement($"BarFill_{i}", barBgGo.transform, new Vector2(barW, barH));
            var barFillRT = barFillGo.GetComponent<RectTransform>();
            barFillRT.anchorMin = new Vector2(0, 0);
            barFillRT.anchorMax = new Vector2(1, 1);
            barFillRT.offsetMin = Vector2.zero;
            barFillRT.offsetMax = Vector2.zero;

            _barFills[i] = barFillGo.AddComponent<Image>();
            _barFills[i].color = new Color(0.15f, 0.15f, 0.15f);
            _barFills[i].type       = Image.Type.Filled;
            _barFills[i].fillMethod = Image.FillMethod.Horizontal;
            _barFills[i].fillAmount = 0f;

            // ── Active dot ─────────────────────────────────────────────────
            float dotX = barX + barW + 18f;

            var dotGo = CreateUIElement($"Dot_{i}", parent, new Vector2(12, 12));
            var dotRT = dotGo.GetComponent<RectTransform>();
            dotRT.anchoredPosition = new Vector2(dotX, y);

            _dots[i] = dotGo.AddComponent<Image>();
            _dots[i].color = InactiveDot;
            // Make it round (use default white sprite)

            // ── Intensity value ────────────────────────────────────────────
            float valX = dotX + 22f;

            var valGo = CreateUIElement($"Val_{i}", parent, new Vector2(40, 24));
            var valRT = valGo.GetComponent<RectTransform>();
            valRT.anchoredPosition = new Vector2(valX, y);

            _values[i] = valGo.AddComponent<Text>();
            _values[i].text      = "—";
            _values[i].font      = Font.CreateDynamicFontFromOSFont("Arial", 18);
            _values[i].fontSize  = 18;
            _values[i].fontStyle = FontStyle.Bold;
            _values[i].color     = new Color(0.35f, 0.35f, 0.35f);
            _values[i].alignment = TextAnchor.MiddleCenter;
        }

        // ── Utility ────────────────────────────────────────────────────────

        GameObject CreateUIElement(string name, Transform parent, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = size;
            return go;
        }
    }
}
