/*
 * JupiterTester — standalone contact detection test, no Arduino required.
 *
 * Hand rendering strategy:
 *   - OVRHandPrefab provides the hand mesh (semi-transparent dark fill)
 *   - White wireframe lines overlay the mesh (outline effect)
 *   - Combined: looks like Meta's default dark hand + white outline
 *
 * Self-contact filtering:
 *   - Detectors only trigger on layer 6 (EMS Contact) objects
 *   - Fingers touching each other or the hand = ignored
 *
 * Setup:
 *   1. OVRHandPrefab under TrackingSpace (not RightHandAnchor), Update Root Pose ON
 *   2. Set OVRHandPrefab to Hand Right / OpenXR Hand Right
 *   3. Drag its OVRSkeleton into this script's handSkeleton field
 *   4. Deploy to Quest 3
 */

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace JupiterBridge.Tests
{
    public class JupiterTester : MonoBehaviour
    {
        public enum Handedness { Right, Left }

        [Header("Meta XR")]
        [Tooltip("Which hand this tracker drives. Add ONE JupiterTester per hand " +
                 "(typically two GameObjects in Bootstrap: HandTracker_Right and " +
                 "HandTracker_Left), each pointed at its own OVRHandPrefab/OVRSkeleton.")]
        public Handedness handedness = Handedness.Right;

        [Tooltip("Drag the OVRSkeleton from this hand's OVRHandPrefab.")]
        [FormerlySerializedAs("rightHandSkeleton")]
        public OVRSkeleton handSkeleton;

        [Header("Contact Detection")]
        public float tipColliderRadius = 0.013f;
        public float maxDepthMetres    = 0.03f;

        [Header("Hand Outline")]
        [Tooltip("Width of the outline lines at rest (metres). 0.003 = 3 mm.")]
        public float outlineWidth = 0.003f;

        [Tooltip("Width of an active (touching) finger's line at full press depth " +
                 "(metres). Lerped between rest and this value by ContactDepth, with " +
                 "a baseline floor so initial contact is still visible.")]
        public float outlineActiveWidth = 0.012f;

        [Tooltip("Minimum fraction of the rest→active width range applied the moment " +
                 "a finger first registers contact (depth ≈ 0). 0.4 means initial " +
                 "contact already shows ~40% of the thickening, so a graze is visible.")]
        [Range(0f, 1f)] public float outlineContactBaseline = 0.4f;

        [Tooltip("Hand mesh transparency (0 = invisible, 1 = opaque)")]
        [Range(0f, 1f)]
        public float handMeshAlpha = 0.25f;

        [Tooltip("Hand mesh tint darkness (lower = darker)")]
        [Range(0f, 0.5f)]
        public float handMeshBrightness = 0.08f;

        [Header("EMS Bridge (UDP → Arduino)")]
        [Tooltip("Enable sending contact events to pc_bridge.py → Arduino")]
        public bool enableEMSBridge = true;

        [Tooltip("Rate for continuous intensity updates while touching (Hz)")]
        public float emsSendRate = 90f;

        [Header("Passthrough")]
        public bool startWithPassthrough = true;

        [Header("Production mode")]
        [Tooltip("Spawn the test sphere + platform + 5 finger-target balls. " +
                 "Use only for standalone testing — disable in director-driven demo scenes.")]
        public bool buildTestScene = true;

        [Tooltip("Spawn the EMS finger-status HUD (per-finger contact + intensity bars). " +
                 "Keep this true in Bootstrap so the panel is visible in every demo scene.")]
        public bool buildStatusPanel = true;

        // ── Definitions ────────────────────────────────────────────────────
        public static readonly string[] FingerNames =
            { "Thumb", "Index", "Middle", "Ring", "Pinky", "Palm" };
        public static readonly string[] ChannelLabels =
            { "CH1  Thumb", "CH2  Index", "CH3  Middle", "CH4  Ring", "CH5  Pinky", "CH6  Palm" };

        private static readonly string[][] TipNamePatterns =
        {
            new[] { "thumb", "tip" }, new[] { "index", "tip" },
            new[] { "middle", "tip" }, new[] { "ring", "tip" },
            new[] { "little", "tip" }, new[] { "palm" },
        };
        private static readonly string[][] TipNameFallbacks =
        {
            null, null, null, null,
            new[] { "pinky", "tip" },
            new[] { "wrist" },
        };

        public static readonly Color[] FingerColors =
        {
            new Color(1.00f, 0.42f, 0.21f), new Color(1.00f, 0.90f, 0.20f),
            new Color(0.31f, 0.80f, 0.77f), new Color(0.53f, 0.90f, 0.62f),
            new Color(0.40f, 0.60f, 1.00f), new Color(0.78f, 0.48f, 1.00f),
        };

        private static readonly Color InactiveColor = new Color(0.25f, 0.25f, 0.25f);

        private static readonly string[][] ChainDefinitions =
        {
            new[] { "thumb" }, new[] { "index" }, new[] { "middle" },
            new[] { "ring" }, new[] { "little", "pinky" },
        };

        // ── Runtime state ──────────────────────────────────────────────────
        [System.NonSerialized] public bool[]  IsContacting = new bool[6];
        [System.NonSerialized] public float[] ContactDepth = new float[6];

        // ── Private ────────────────────────────────────────────────────────
        private Transform[]             _bonePivots = new Transform[6];
        private FingerContactDetector[] _detectors  = new FingerContactDetector[6];
        private Shader                  _shader;
        private bool                    _palmIsWristBone;

        // Wireframe
        private List<LineRenderer>    _lines  = new List<LineRenderer>();
        private List<List<Transform>> _chains = new List<List<Transform>>();
        // Detector index that each chain in _lines/_chains belongs to.
        // 0..4 = thumb..pinky, 5 = palm. Tracked explicitly so a missing
        // finger chain doesn't shift the mapping for the others.
        private List<int>             _chainFingerIdx = new List<int>();

        // Outline colors. White when idle; finger-specific colour, slightly
        // brightened toward white at deeper press, when that finger is in
        // contact with a layer-6 object.
        private static readonly Color OutlineIdle = new Color(1f, 1f, 1f, 0.9f);

        // Passthrough
        private OVRPassthroughLayer _ptLayer;
        private bool _ptActive;

        // UDP/EMS bridge state
        private bool[]  _wasActive = new bool[6];
        private float   _emsSendTimer;

        private const int ContactLayer = 6;

        // ══════════════════════════════════════════════════════════════════

        void Start()
        {
            _shader = ResolveShader();
            BuildFingerTips();
            SetupPassthrough();
            SetupUDPSender();
            StartCoroutine(InitScene());
        }

        IEnumerator InitScene()
        {
            yield return null;
            yield return null;

            Camera cam = Camera.main;
            Vector3 headPos = cam != null ? cam.transform.position : new Vector3(0, 1.5f, 0);
            Vector3 headFwd = cam != null
                ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized
                : Vector3.forward;
            if (headFwd.sqrMagnitude < 0.01f) headFwd = Vector3.forward;
            Vector3 headRight = Vector3.Cross(Vector3.up, headFwd).normalized * -1f;

            if (buildTestScene)  BuildTestScene(headPos, headFwd, headRight);
            if (buildStatusPanel) BuildStatusPanel(headPos, headFwd, headRight);

            yield return StartCoroutine(BindBones());

            ApplyHandMeshMaterial();
            BuildOutlineWireframe();
        }

        void Update()
        {
            UpdateTipPositions();
            UpdateWireframe();

            for (int i = 0; i < 6; i++)
            {
                IsContacting[i] = _detectors[i] != null && _detectors[i].IsContacting;
                ContactDepth[i] = _detectors[i] != null ? _detectors[i].ContactDepth : 0f;
            }

            if (OVRInput.GetDown(OVRInput.Button.Two) ||
                OVRInput.GetDown(OVRInput.Button.SecondaryThumbstick))
                TogglePassthrough();

            // Send contact events to Arduino via UDP
            if (enableEMSBridge)
                SendEMSEvents();
        }

        // ══════════════════════════════════════════════════════════════════
        //  EMS BRIDGE — UDP to pc_bridge.py → Arduino
        // ══════════════════════════════════════════════════════════════════

        void SetupUDPSender()
        {
            if (!enableEMSBridge) return;

            // Auto-create UDPSender if it doesn't exist in the scene
            if (UDPSender.Instance == null)
            {
                var go = new GameObject("UDPSender");
                go.AddComponent<UDPSender>();
                Debug.Log("[JupiterTester] Auto-created UDPSender — set your PC's IP in the Inspector!");
            }
        }

        void SendEMSEvents()
        {
            if (UDPSender.Instance == null) return;

            // Immediate send on state change (low latency)
            for (int i = 0; i < 6; i++)
            {
                if (IsContacting[i] != _wasActive[i])
                {
                    SendContactUDP(i, IsContacting[i], ContactDepth[i]);
                    _wasActive[i] = IsContacting[i];
                }
            }

            // Continuous intensity updates for active contacts (~90 Hz)
            _emsSendTimer += Time.deltaTime;
            float interval = 1f / emsSendRate;
            if (_emsSendTimer >= interval)
            {
                _emsSendTimer = 0f;
                for (int i = 0; i < 6; i++)
                {
                    if (IsContacting[i])
                        SendContactUDP(i, true, ContactDepth[i]);
                }
            }
        }

        void SendContactUDP(int fingerIndex, bool active, float depth)
        {
            // The "hand" field tells pc_bridge.py which Arduino to route the
            // command to. Lowercase strings ("right" / "left") because that's
            // what the Python bridge keys on (matches its bridges dict).
            string handStr = (handedness == Handedness.Right) ? "right" : "left";
            string json = active
                ? $"{{\"hand\":\"{handStr}\",\"finger\":\"{FingerNames[fingerIndex]}\",\"active\":true,\"depth\":{depth:F3}}}"
                : $"{{\"hand\":\"{handStr}\",\"finger\":\"{FingerNames[fingerIndex]}\",\"active\":false,\"depth\":0}}";
            UDPSender.Instance.Send(json);
        }

        void OnDestroy()
        {
            // Safety: tell Arduino to kill all channels when app exits
            if (enableEMSBridge && UDPSender.Instance != null)
            {
                for (int i = 0; i < 6; i++)
                    SendContactUDP(i, false, 0f);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  HAND MESH MATERIAL — semi-transparent dark fill
        // ══════════════════════════════════════════════════════════════════

        void ApplyHandMeshMaterial()
        {
            if (handSkeleton == null) return;

            // Search for SkinnedMeshRenderer on the same GameObject or parent hierarchy
            // OVRHandPrefab puts it on the same object or a child
            SkinnedMeshRenderer smr = null;

            // Check the skeleton's GameObject and its parent/siblings
            var handObj = handSkeleton.gameObject;
            smr = handObj.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null) smr = handObj.GetComponentInParent<SkinnedMeshRenderer>();

            // Also search parent (OVRHandPrefab root)
            if (smr == null && handObj.transform.parent != null)
                smr = handObj.transform.parent.GetComponentInChildren<SkinnedMeshRenderer>();

            if (smr == null)
            {
                Debug.Log("[JupiterTester] No SkinnedMeshRenderer found — hand mesh material unchanged");
                return;
            }

            // Create semi-transparent dark material
            // Try to find a transparent shader
            Shader transShader = null;
            string[] transCandidates = {
                "Universal Render Pipeline/Lit",
                "Standard",
                "Sprites/Default",
                "Unlit/Transparent",
                "UI/Default",
            };
            foreach (var name in transCandidates)
            {
                var s = Shader.Find(name);
                if (s != null) { transShader = s; break; }
            }

            if (transShader == null)
            {
                Debug.LogWarning("[JupiterTester] No transparent shader found");
                return;
            }

            var mat = new Material(transShader);
            Color handColor = new Color(handMeshBrightness, handMeshBrightness, handMeshBrightness, handMeshAlpha);
            mat.color = handColor;

            // Configure for transparency based on shader type
            if (transShader.name.Contains("Standard"))
            {
                mat.SetFloat("_Mode", 3); // Transparent
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
            }
            else if (transShader.name.Contains("Universal"))
            {
                mat.SetFloat("_Surface", 1); // Transparent
                mat.SetFloat("_Blend", 0);    // Alpha
                mat.SetFloat("_DstBlend", 10);
                mat.SetFloat("_ZWrite", 0);
                mat.SetFloat("_AlphaClip", 0);
                mat.renderQueue = 3000;
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", handColor);
            }
            else
            {
                // Sprites/Default or similar — just set color with alpha
                mat.color = handColor;
            }

            smr.material = mat;
            Debug.Log($"[JupiterTester] Hand mesh material applied: {transShader.name}, alpha={handMeshAlpha}");
        }

        // ══════════════════════════════════════════════════════════════════
        //  WHITE OUTLINE WIREFRAME — lines along finger chains
        // ══════════════════════════════════════════════════════════════════

        void BuildOutlineWireframe()
        {
            if (handSkeleton == null || !handSkeleton.IsInitialized) return;
            var bones = handSkeleton.Bones;

            Color outlineColor = OutlineIdle;

            // Build one chain per finger
            for (int chain = 0; chain < ChainDefinitions.Length; chain++)
            {
                var chainBones = new List<Transform>();
                string[] patterns = ChainDefinitions[chain];

                // Find wrist bone as chain start
                Transform wrist = null;
                foreach (var b in bones)
                {
                    string n = b.Transform.name.ToLower();
                    if (n.Contains("wrist") || (n.Contains("palm") && !n.Contains("metacarpal")))
                    { wrist = b.Transform; break; }
                }
                if (wrist != null) chainBones.Add(wrist);

                // Collect bones for this finger, sorted by joint order
                var matched = new List<(Transform t, int order)>();
                foreach (var b in bones)
                {
                    string n = b.Transform.name.ToLower();
                    bool isMatch = false;
                    foreach (var pat in patterns)
                        if (n.Contains(pat)) { isMatch = true; break; }
                    if (!isMatch) continue;

                    int order = 99;
                    if (n.Contains("metacarpal")) order = 0;
                    else if (n.Contains("proximal")) order = 1;
                    else if (n.Contains("intermediate")) order = 2;
                    else if (n.Contains("distal")) order = 3;
                    else if (n.Contains("tip")) order = 4;
                    else if (n.EndsWith("0")) order = 0;
                    else if (n.EndsWith("1")) order = 1;
                    else if (n.EndsWith("2")) order = 2;
                    else if (n.EndsWith("3")) order = 3;

                    matched.Add((b.Transform, order));
                }
                matched.Sort((a, b) => a.order.CompareTo(b.order));
                foreach (var m in matched) chainBones.Add(m.t);

                if (chainBones.Count >= 2)
                {
                    _chains.Add(chainBones);
                    _lines.Add(MakeLine(outlineColor, chainBones.Count));
                    _chainFingerIdx.Add(chain);  // chain index 0..4 == detector index for thumb..pinky
                }
            }

            // Palm outline: connect metacarpal bases across the hand
            var metacarpals = new List<Transform>();
            foreach (var chainDef in ChainDefinitions)
            {
                foreach (var b in bones)
                {
                    string n = b.Transform.name.ToLower();
                    bool match = false;
                    foreach (var p in chainDef) if (n.Contains(p)) { match = true; break; }
                    if (match && (n.Contains("metacarpal") || n.EndsWith("0") || n.EndsWith("1")))
                    { metacarpals.Add(b.Transform); break; }
                }
            }

            if (metacarpals.Count >= 3)
            {
                Transform wrist = null;
                foreach (var b in bones)
                    if (b.Transform.name.ToLower().Contains("wrist"))
                    { wrist = b.Transform; break; }

                var palmChain = new List<Transform>(metacarpals);
                if (wrist != null) palmChain.Add(wrist);
                if (metacarpals.Count > 0) palmChain.Add(metacarpals[0]); // close loop

                _chains.Add(palmChain);
                _lines.Add(MakeLine(outlineColor, palmChain.Count));
                _chainFingerIdx.Add(5);  // palm detector
            }

            Debug.Log($"[JupiterTester] Outline: {_lines.Count} chains");
        }

        LineRenderer MakeLine(Color color, int count)
        {
            var go = new GameObject("OutlineLine");
            go.transform.SetParent(transform);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount     = count;
            lr.startWidth        = outlineWidth;
            lr.endWidth          = outlineWidth;
            lr.useWorldSpace     = true;
            lr.numCapVertices    = 3;
            lr.numCornerVertices = 3;

            var mat = new Material(Shader.Find("Sprites/Default") ?? _shader);
            mat.color = color;
            lr.material   = mat;
            lr.startColor = color;
            lr.endColor   = color;

            return lr;
        }

        void UpdateWireframe()
        {
            int n = Mathf.Min(_lines.Count, Mathf.Min(_chains.Count, _chainFingerIdx.Count));
            for (int i = 0; i < n; i++)
            {
                var chain = _chains[i];
                var lr    = _lines[i];
                if (lr == null) continue;

                // Bone positions
                if (lr.positionCount != chain.Count) lr.positionCount = chain.Count;
                for (int j = 0; j < chain.Count; j++)
                    if (chain[j] != null) lr.SetPosition(j, chain[j].position);

                // Resolve the detector this chain belongs to.
                int fingerIdx = _chainFingerIdx[i];
                var det = (fingerIdx >= 0 && fingerIdx < _detectors.Length) ? _detectors[fingerIdx] : null;
                bool active = det != null && det.IsContacting;
                float depth = active ? Mathf.Clamp01(det.ContactDepth) : 0f;

                // ── Width: rest → activeWidth, lerped by depth, with a baseline
                //    floor on first contact so a graze is still visibly thicker.
                float widthLerp = active ? Mathf.Max(outlineContactBaseline, depth) : 0f;
                float width     = Mathf.Lerp(outlineWidth, outlineActiveWidth, widthLerp);
                lr.startWidth = width;
                lr.endWidth   = width;

                // ── Colour: white at rest, finger colour on contact.
                Color target = OutlineIdle;
                if (active)
                {
                    target   = FingerColors[fingerIdx];
                    target.a = 1f;
                }
                lr.startColor = target;
                lr.endColor   = target;
                if (lr.material != null) lr.material.color = target;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  PASSTHROUGH
        // ══════════════════════════════════════════════════════════════════

        void SetupPassthrough()
        {
            _ptLayer = FindObjectOfType<OVRPassthroughLayer>();
            if (_ptLayer == null)
            {
                var rig = FindObjectOfType<OVRCameraRig>();
                if (rig != null)
                {
                    _ptLayer = rig.gameObject.AddComponent<OVRPassthroughLayer>();
                    _ptLayer.overlayType = OVROverlay.OverlayType.Underlay;
                    _ptLayer.compositionDepth = 0;
                }
            }
            var mgr = FindObjectOfType<OVRManager>();
            if (mgr != null) mgr.isInsightPassthroughEnabled = startWithPassthrough;
            if (startWithPassthrough) EnablePassthrough();
        }

        void EnablePassthrough()
        {
            _ptActive = true;
            if (_ptLayer != null) _ptLayer.enabled = true;
            var cam = Camera.main;
            if (cam != null) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0,0,0,0); }
            var mgr = FindObjectOfType<OVRManager>();
            if (mgr != null) mgr.isInsightPassthroughEnabled = true;
        }

        void DisablePassthrough()
        {
            _ptActive = false;
            if (_ptLayer != null) _ptLayer.enabled = false;
            var cam = Camera.main;
            if (cam != null) cam.clearFlags = CameraClearFlags.Skybox;
            var mgr = FindObjectOfType<OVRManager>();
            if (mgr != null) mgr.isInsightPassthroughEnabled = false;
        }

        void TogglePassthrough()
        { if (_ptActive) DisablePassthrough(); else EnablePassthrough(); }

        // ══════════════════════════════════════════════════════════════════
        //  SHADER UTILS
        // ══════════════════════════════════════════════════════════════════

        Shader ResolveShader()
        {
            string[] c = { "Unlit/Color", "Universal Render Pipeline/Lit",
                           "Standard", "Mobile/Diffuse" };
            foreach (var n in c) { var s = Shader.Find(n); if (s != null) return s; }
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var sh = tmp.GetComponent<Renderer>().sharedMaterial.shader;
            Destroy(tmp);
            return sh;
        }

        public Material MakeMat(Color color)
        {
            var mat = new Material(_shader);
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            return mat;
        }

        public Material MakeEmissiveMat(Color color, Color emission)
        {
            var mat = MakeMat(color);
            if (mat.HasProperty("_EmissionColor"))
            { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", emission); }
            return mat;
        }

        // ══════════════════════════════════════════════════════════════════
        //  FINGERTIP TRACKERS
        // ══════════════════════════════════════════════════════════════════

        void BuildFingerTips()
        {
            // Each detector is an INVISIBLE trigger collider that follows a fingertip
            // bone. Visualisation of contact happens via the outline-wireframe
            // colouring (UpdateWireframe), not via per-tip geometry.
            // Hand prefix in the GameObject name keeps both hands' detectors
            // distinguishable in the Hierarchy (JT_R_ThumbTip vs JT_L_ThumbTip).
            string handPrefix = (handedness == Handedness.Right) ? "R" : "L";
            for (int i = 0; i < 6; i++)
            {
                var go = new GameObject($"JT_{handPrefix}_{FingerNames[i]}Tip");
                go.transform.SetParent(transform);

                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true; rb.useGravity = false;

                var col = go.AddComponent<SphereCollider>();
                col.radius = tipColliderRadius; col.isTrigger = true;

                var det = go.AddComponent<FingerContactDetector>();
                det.fingerName       = FingerNames[i];
                det.maxDepthMetres   = maxDepthMetres;
                det.contactLayerIndex = ContactLayer;
                det.isLeftHand       = (handedness == Handedness.Left);
                _detectors[i] = det;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  BONE BINDING
        // ══════════════════════════════════════════════════════════════════

        IEnumerator BindBones()
        {
            float timeout = 10f, waited = 0f;
            while (handSkeleton == null || !handSkeleton.IsInitialized)
            {
                waited += Time.deltaTime;
                if (waited > timeout) { Debug.LogError("[JupiterTester] Skeleton timeout!"); yield break; }
                yield return null;
            }

            var bones = handSkeleton.Bones;
            Debug.Log($"[JupiterTester] Skeleton: {bones.Count} bones");
            for (int b = 0; b < bones.Count; b++)
                Debug.Log($"[JupiterTester]   [{b}] \"{bones[b].Transform.name}\"");

            for (int i = 0; i < 6; i++)
            {
                Transform match = FindBoneByName(bones, TipNamePatterns[i]);
                if (match == null && TipNameFallbacks[i] != null)
                {
                    match = FindBoneByName(bones, TipNameFallbacks[i]);
                    if (match != null && i == 5) _palmIsWristBone = true;
                }
                if (match != null) _bonePivots[i] = match;
            }

            int found = 0;
            for (int i = 0; i < 6; i++) if (_bonePivots[i] != null) found++;
            if (found < 3) TryIndexFallback(bones);
        }

        Transform FindBoneByName(IList<OVRBone> bones, string[] patterns)
        {
            foreach (var bone in bones)
            {
                string name = bone.Transform.name.ToLower();
                bool all = true;
                foreach (var p in patterns) if (!name.Contains(p.ToLower())) { all = false; break; }
                if (all) return bone.Transform;
            }
            return null;
        }

        void TryIndexFallback(IList<OVRBone> bones)
        {
            if (bones.Count >= 26)
            {
                int[] t = { 5, 10, 15, 20, 25 };
                for (int i = 0; i < 5; i++) if (_bonePivots[i] == null && t[i] < bones.Count) _bonePivots[i] = bones[t[i]].Transform;
                if (_bonePivots[5] == null) _bonePivots[5] = bones[0].Transform;
            }
        }

        void UpdateTipPositions()
        {
            for (int i = 0; i < 6; i++)
            {
                if (_bonePivots[i] == null) continue;
                _detectors[i].transform.position = _bonePivots[i].position;
                if (i == 5 && _palmIsWristBone)
                    _detectors[i].transform.position += _bonePivots[5].TransformDirection(new Vector3(0, 0.05f, 0));
                _detectors[i].transform.rotation = _bonePivots[i].rotation;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  TEST SCENE
        // ══════════════════════════════════════════════════════════════════

        void BuildTestScene(Vector3 headPos, Vector3 headFwd, Vector3 headRight)
        {
            Vector3 center = headPos + headFwd * 0.45f - Vector3.up * 0.25f;

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "GrabSphere"; sphere.layer = ContactLayer;
            sphere.transform.position = center;
            sphere.transform.localScale = Vector3.one * 0.20f;
            sphere.GetComponent<MeshRenderer>().material = MakeMat(new Color(0.25f, 0.55f, 1.0f));

            var platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            platform.name = "Platform"; platform.layer = ContactLayer;
            platform.transform.position = center + headRight * 0.3f - Vector3.up * 0.15f;
            platform.transform.localScale = new Vector3(0.25f, 0.02f, 0.25f);
            platform.GetComponent<MeshRenderer>().material = MakeMat(new Color(0.85f, 0.85f, 0.85f));

            Vector3 rowCenter = center + headFwd * -0.12f;
            string[] tips = { "Th", "Ix", "Md", "Rg", "Pk" };
            for (int i = 0; i < 5; i++)
            {
                var t = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                t.name = $"FingerTarget_{tips[i]}"; t.layer = ContactLayer;
                t.transform.position = rowCenter + headRight * ((i - 2) * 0.04f);
                t.transform.localScale = Vector3.one * 0.035f;
                t.GetComponent<MeshRenderer>().material = MakeMat(FingerColors[i] * 0.7f);
            }
        }

        void BuildStatusPanel(Vector3 headPos, Vector3 headFwd, Vector3 headRight)
        {
            // The `headRight` arg passed in is actually the user-LEFT vector
            // (it's Cross(up,fwd)*-1 — see InitScene). So `headRight * -0.35`
            // puts the panel on the user's RIGHT side. For the left hand we
            // want the panel on the user's LEFT side, hence flipping the sign.
            float lateralSign = (handedness == Handedness.Right) ? -1f : +1f;
            Vector3 pos = headPos + headFwd * 0.50f + headRight * (0.35f * lateralSign) + Vector3.up * 0.10f;
            var go = new GameObject($"FingerStatusPanel_{handedness}");
            go.transform.SetParent(transform);
            var panel = go.AddComponent<FingerStatusPanel>();
            panel.tester = this;
            panel.spawnPos = pos;
            panel.spawnFwd = headFwd;
            panel.handLabel = (handedness == Handedness.Right) ? "RIGHT HAND" : "LEFT HAND";
        }
    }
}
