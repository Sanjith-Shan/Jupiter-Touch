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

        // ─── Keyboard geometry (ORIGINAL physical sizes) ──────────────────
        public const float KeyWidthM   = 0.026f;   // 26 mm
        public const float KeyDepthM   = 0.026f;   // 26 mm
        public const float KeyHeightM  = 0.010f;   // 10 mm tall body
        public const float KeyGapM     = 0.005f;   //  5 mm between keys

        public const float WideKeyWidthMultiplier  = 2.5f;
        public const float SpaceKeyWidthMultiplier = 6.0f;

        public const float KeyboardTiltDegrees = 25f;
        public const float KeyboardLabelLiftMm = 1.5f;

        // ─── Keyboard label sizing (4× bigger than the original 14/9 mm) ──
        public const float KeyLetterCapHeightMm  = 56f;
        public const float KeyWideCapHeightMm    = 36f;

        public static float KeyLetterFontSize => HeightMmToFontSize(KeyLetterCapHeightMm);
        public static float KeyWideFontSize   => HeightMmToFontSize(KeyWideCapHeightMm);

        // ─── Monitor geometry (ORIGINAL physical sizes) ───────────────────
        public const float MonitorScreenWidthM  = 0.45f;
        public const float MonitorScreenHeightM = 0.28f;
        public const float MonitorScreenDepthM  = 0.020f;
        public const float MonitorBezelM        = 0.012f;
        public const float MonitorTextMarginM   = 0.012f;

        // ─── Monitor text sizing (4× bigger than the original 22 mm) ──────
        public const float MonitorTextCapHeightMm = 88f;

        public static float MonitorTextFontSize => HeightMmToFontSize(MonitorTextCapHeightMm);

        // ─── Anchor offsets (where things spawn relative to camera) ───────
        public static readonly UnityEngine.Vector3 KeyboardAnchorOffset
            = new UnityEngine.Vector3(0.30f, -0.35f, 0.45f);
        public static readonly UnityEngine.Vector3 MonitorAnchorOffset
            = new UnityEngine.Vector3(0.00f, 0.10f, 0.85f);

        // ─── Director-driven subway spawn placement (relative to seated user) ──
        // SubwaySceneController uses these when the presenter clicks
        // "Spawn Monitors" / "Spawn Keyboard". The user is assumed seated and
        // facing forward; these offsets are applied along the head-projected
        // forward / up / right vectors.
        public const float MonitorSpawnDistanceM = 0.85f;  // forward of head
        public const float MonitorSpawnHeightM   = 0.05f;  // above head
        public const float MonitorSpawnSpacingM  = 0.50f;  // L/R separation

        public const float KeyboardSpawnDistanceM = 0.45f; // forward of head
        public const float KeyboardSpawnDropM     = -0.30f; // below head (lap)

        // ─── Spawn fade-in animation ──────────────────────────────────────
        // A simple translation-only "rise into place" effect. Scale-based
        // animation would break VirtualKeyboard's lossyScale-compensated
        // label sizing, so we keep root scale fixed and animate position.
        public const float SpawnFadeDurationS = 0.35f;
        public const float SpawnRiseM         = 0.06f;

        // ─── Press detection ──────────────────────────────────────────────
        // "Primed → press" gating: a finger only fires a key press if it
        // entered the key's collider FROM ABOVE (its world Y was above the
        // key's top surface at OnTriggerEnter time). Resting/curled fingers
        // already below the keyboard plane never fire.
        //
        // SlideTolerance forgives a glancing entry on the very top edge —
        // if the finger enters with its Y at most this far below the top
        // surface, it's still treated as "from above". Keeps fast taps that
        // arrive at a slight angle from being rejected.
        public const float KeyPressSlideToleranceM = 0.003f;   // 3 mm

        // Cooldown after a press, applied to subsequent press attempts. The
        // "same hand" window is much wider than "cross hand" because real
        // typing rarely requires <80 ms between two same-hand keys, while
        // alternating hands at 100 WPM produces genuine ~50 ms gaps.
        // Anything faster than the same-hand cooldown is treated as
        // finger-curl noise from the user's other (non-typing) fingers.
        public const float KeyPressSameHandCooldownMs  = 80f;
        public const float KeyPressCrossHandCooldownMs = 25f;

        // ─── Monitor cursor ──────────────────────────────────────────────
        public const float MonitorCursorBlinkSeconds = 0.5f;
        public const string MonitorCursorChar = "|";

        // ─── Layers ───────────────────────────────────────────────────────
        public const int EmsContactLayer = 6;
    }
}
