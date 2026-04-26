using UnityEngine;

namespace JupiterBridge
{
    /// <summary>
    /// Procedurally builds the Jupiter Touch test scene at runtime.
    ///
    /// Attach this to an empty GameObject in a blank scene alongside
    /// OVRCameraRig (from Meta XR SDK). Configure the public fields
    /// in the Inspector, then hit Play / deploy to Quest.
    ///
    /// What it creates:
    ///   • A soft-looking white sphere suspended in front of the player
    ///   • A floor plane so the hand has a reference surface
    ///   • Connects HandContactManager to the skeleton on OVRCameraRig
    ///
    /// The sphere's layer is set to "EMS Contact" (layer 6 by default).
    /// Add that layer in Edit → Project Settings → Tags and Layers if needed.
    /// </summary>
    public class JupiterTestScene : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Drag in the OVRSkeleton component from the Right Hand anchor of OVRCameraRig")]
        public OVRSkeleton rightHandSkeleton;

        [Tooltip("Drag in the UDPSender GameObject (or leave empty to auto-find)")]
        public UDPSender udpSender;

        [Header("Contact object")]
        public float sphereRadius        = 0.08f;   // 8 cm sphere
        public Vector3 sphereLocalOffset = new Vector3(0f, 1.4f, 0.5f); // in front of player

        [Header("Layer")]
        [Tooltip("Layer index for touchable objects (must match contactLayer mask on HandContactManager)")]
        public int contactLayerIndex = 6;  // "EMS Contact"

        void Start()
        {
            // ── UDPSender ────────────────────────────────────────────────────
            if (udpSender == null)
                udpSender = FindObjectOfType<UDPSender>();
            if (udpSender == null)
            {
                var go = new GameObject("UDPSender");
                udpSender = go.AddComponent<UDPSender>();
            }

            // ── HandContactManager ───────────────────────────────────────────
            var hmGo = new GameObject("HandContactManager");
            var hm   = hmGo.AddComponent<HandContactManager>();
            hm.skeleton     = rightHandSkeleton;
            hm.contactLayer = 1 << contactLayerIndex;

            // ── Test sphere ──────────────────────────────────────────────────
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "JupiterTestSphere";
            sphere.transform.position   = sphereLocalOffset;
            sphere.transform.localScale = Vector3.one * (sphereRadius * 2f);
            sphere.layer = contactLayerIndex;

            // Make it visually distinct
            var mat  = new Material(Shader.Find("Standard"));
            mat.color = new Color(0.3f, 0.6f, 1.0f, 1f);  // soft blue
            sphere.GetComponent<Renderer>().material = mat;

            // ── Floor plane ──────────────────────────────────────────────────
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = Vector3.one * 2f;
            var floorMat  = new Material(Shader.Find("Standard"));
            floorMat.color = new Color(0.15f, 0.15f, 0.15f);
            floor.GetComponent<Renderer>().material = floorMat;
            // Floor should NOT trigger EMS
            floor.layer = 0;

            Debug.Log("[JupiterTestScene] Scene built. Reach out and touch the blue sphere.");
        }
    }
}
