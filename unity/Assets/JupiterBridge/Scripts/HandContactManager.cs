using System.Collections;
using System.Collections.Generic;
using UnityEngine;
// Removed: using Oculus.Interaction.Input — not needed, causes compile errors if Interaction SDK not installed

namespace JupiterBridge
{
    /// <summary>
    /// Spawns six fingertip tracker GameObjects (one per finger + palm),
    /// keeps them aligned to hand skeleton bones, and streams contact events
    /// to the PC bridge via UDP at ~90 Hz.
    ///
    /// Requires Meta XR SDK v50+.
    /// Drag the right-hand OVRCameraRig Hand anchor into 'handRoot',
    /// and the OVRSkeleton component into 'skeleton'.
    /// </summary>
    public class HandContactManager : MonoBehaviour
    {
        [Header("Meta XR References")]
        [Tooltip("The OVRSkeleton on the right hand (or left — just be consistent with your channel map)")]
        public OVRSkeleton skeleton;

        [Header("Contact Settings")]
        [Tooltip("Layer mask for objects that trigger EMS feedback")]
        public LayerMask contactLayer;

        [Tooltip("Radius of each fingertip sphere collider (metres)")]
        public float tipRadius = 0.012f;  // ~12 mm

        [Header("Update Rate")]
        [Tooltip("How often to re-send active contact intensities (seconds). 1/90 ≈ 0.011")]
        public float updateInterval = 1f / 90f;

        // Bone IDs for each finger tip + palm approximation
        private static readonly (string name, OVRSkeleton.BoneId boneId)[] FingerBones =
        {
            ("Thumb",  OVRSkeleton.BoneId.Hand_ThumbTip),
            ("Index",  OVRSkeleton.BoneId.Hand_IndexTip),
            ("Middle", OVRSkeleton.BoneId.Hand_MiddleTip),
            ("Ring",   OVRSkeleton.BoneId.Hand_RingTip),
            ("Pinky",  OVRSkeleton.BoneId.Hand_PinkyTip),
            ("Palm",   OVRSkeleton.BoneId.Hand_WristRoot),  // Palm offset applied below
        };

        private FingerContactDetector[] _detectors;
        private Transform[]             _bonePivots;   // cached bone transforms from OVRSkeleton
        private bool[]                  _wasActive;    // previous frame active state
        private float                   _timer;

        // Palm offset from wrist root: shift ~5 cm toward fingers
        private static readonly Vector3 PalmLocalOffset = new Vector3(0f, 0.05f, 0f);

        void Start()
        {
            _detectors = new FingerContactDetector[6];
            _bonePivots = new Transform[6];
            _wasActive  = new bool[6];

            for (int i = 0; i < 6; i++)
            {
                var go = new GameObject($"JB_{FingerBones[i].name}Tip");
                go.transform.SetParent(transform);

                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity  = false;

                var col = go.AddComponent<SphereCollider>();
                col.radius    = tipRadius;
                col.isTrigger = true;

                var det = go.AddComponent<FingerContactDetector>();
                det.fingerName = FingerBones[i].name;
                _detectors[i]  = det;
            }

            StartCoroutine(WaitForSkeleton());
        }

        private IEnumerator WaitForSkeleton()
        {
            // OVRSkeleton initialises asynchronously; wait until bones are ready
            while (skeleton == null || !skeleton.IsInitialized)
                yield return null;

            for (int i = 0; i < 6; i++)
            {
                OVRSkeleton.BoneId boneId = FingerBones[i].boneId;
                foreach (var bone in skeleton.Bones)
                {
                    if (bone.Id == boneId)
                    {
                        _bonePivots[i] = bone.Transform;
                        break;
                    }
                }

                if (_bonePivots[i] == null)
                    Debug.LogWarning($"[HandContactManager] Bone not found: {boneId}");
            }

            Debug.Log("[HandContactManager] Skeleton ready.");
        }

        void Update()
        {
            // Move tip GameObjects to current bone world positions
            for (int i = 0; i < 6; i++)
            {
                if (_bonePivots[i] == null) continue;

                Vector3 pos = _bonePivots[i].position;
                if (i == 5)  // Palm: offset from wrist toward fingers
                    pos += _bonePivots[i].TransformDirection(PalmLocalOffset);

                _detectors[i].transform.position = pos;
                _detectors[i].transform.rotation = _bonePivots[i].rotation;
            }

            // Send events on state change (low latency path)
            for (int i = 0; i < 6; i++)
            {
                bool active = _detectors[i].IsContacting;
                if (active != _wasActive[i])
                {
                    SendContactEvent(i, active, _detectors[i].ContactDepth);
                    _wasActive[i] = active;
                }
            }

            // Send continuous intensity updates for all active fingers
            _timer += Time.deltaTime;
            if (_timer >= updateInterval)
            {
                _timer = 0f;
                for (int i = 0; i < 6; i++)
                {
                    if (_detectors[i].IsContacting)
                        SendContactEvent(i, true, _detectors[i].ContactDepth);
                }
            }
        }

        private void SendContactEvent(int index, bool active, float depth)
        {
            if (UDPSender.Instance == null) return;

            // Compact JSON — avoid allocations with string interpolation only here
            string json = active
                ? $"{{\"finger\":\"{FingerBones[index].name}\",\"active\":true,\"depth\":{depth:F3}}}"
                : $"{{\"finger\":\"{FingerBones[index].name}\",\"active\":false,\"depth\":0}}";

            UDPSender.Instance.Send(json);
        }

        void OnDestroy()
        {
            // Safety: tell bridge to kill all channels when scene unloads
            if (UDPSender.Instance != null)
            {
                for (int i = 0; i < 6; i++)
                    SendContactEvent(i, false, 0f);
            }
        }
    }
}
