namespace JupiterBridge.Subway
{
    /// <summary>
    /// SINGLE source of truth for every physical dimension in the Jupiter
    /// Touch interactive elements (keyboard, monitor, future widgets).
    ///
    /// Why this exists: Unity's serialization persists public/SerializeField
    /// values onto every existing GameObject in the scene/prefab. Bumping a
    /// default value in code does NOT update existing instances. By moving
    /// every sizing parameter into <see cref="const"/> values here, we
    /// guarantee that the only way to change a size is to edit this file —
    /// and the change applies to every instance immediately on next play.
    ///
    /// All world dimensions in METRES. All "label height" specs in MM.
    /// </summary>
    public static class JupiterTouchSizing
    {
        // ─── TMP rendering constants ──────────────────────────────────────
        /// <summary>
        /// World scale applied to every TMP label transform. With this set
        /// to 0.001, 1 TMP-unit = 1 mm world. Universal across keyboard
        /// keys and monitor text so layouts stay consistent.
        /// </summary>
        public const float LabelScale = 0.001f;

        /// <summary>
        /// Cap-height ratio for Liberation Sans SDF (the TMP Essentials
        /// default font). Verified from FaceInfo: capLine ≈ 70.7, pointSize = 100.
        /// </summary>
        const float CapHeightRatio = 0.707f;

        /// <summary>
        /// Convert a desired world cap-height (in mm) to the TMP fontSize
        /// you should set, assuming LabelScale is applied to the transform.
        /// </summary>
        public static float HeightMmToFontSize(float capHeightMm)
            => capHeightMm / CapHeightRatio;

        // ─── Keyboard geometry ────────────────────────────────────────────
        public const float KeyWidthM   = 0.104f;   // 104 mm
        public const float KeyDepthM   = 0.104f;   // 104 mm
        public const float KeyHeightM  = 0.040f;   //  40 mm tall body
        public const float KeyGapM     = 0.020f;   //  20 mm between keys

        public const float WideKeyWidthMultiplier  = 2.5f;   // Back / Enter
        public const float SpaceKeyWidthMultiplier = 6.0f;

        public const float KeyboardTiltDegrees = 25f;
        public const float KeyboardLabelLiftMm = 6f;         // Label hover above key top face

        // ─── Keyboard label sizing ────────────────────────────────────────
        public const float KeyLetterCapHeightMm  = 56f;   // Letters / digits
        public const float KeyWideCapHeightMm    = 36f;   // Bk / Ent / space (slightly smaller)

        public static float KeyLetterFontSize => HeightMmToFontSize(KeyLetterCapHeightMm);
        public static float KeyWideFontSize   => HeightMmToFontSize(KeyWideCapHeightMm);

        // ─── Monitor geometry ─────────────────────────────────────────────
        public const float MonitorScreenWidthM  = 1.80f;   // 180 cm
        public const float MonitorScreenHeightM = 1.12f;   // 112 cm (~16:10)
        public const float MonitorScreenDepthM  = 0.080f;
        public const float MonitorBezelM        = 0.048f;
        public const float MonitorTextMarginM   = 0.048f;

        // ─── Monitor text sizing ──────────────────────────────────────────
        public const float MonitorTextCapHeightMm = 88f;   // Body text on screen

        public static float MonitorTextFontSize => HeightMmToFontSize(MonitorTextCapHeightMm);

        // ─── Anchor offsets (where things spawn relative to camera) ───────
        public static readonly UnityEngine.Vector3 KeyboardAnchorOffset
            = new UnityEngine.Vector3(0.30f, -0.35f, 0.45f);
        public static readonly UnityEngine.Vector3 MonitorAnchorOffset
            = new UnityEngine.Vector3(0.00f, 0.10f, 0.85f);

        // ─── Layers ───────────────────────────────────────────────────────
        public const int EmsContactLayer = 6;
    }
}
