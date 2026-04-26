using UnityEngine;

namespace JupiterBridge
{
    /// <summary>
    /// Placed on a small sphere GameObject that tracks one fingertip bone.
    /// Reports contact state (IsContacting, ContactDepth) to the manager.
    ///
    /// ONLY triggers on objects in the designated contact layer (default: layer 6 "EMS Contact").
    /// Ignores all other colliders including other fingertips and the hand mesh itself,
    /// so fingers touching each other or the palm will NOT register as contact.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    [RequireComponent(typeof(Rigidbody))]
    public class FingerContactDetector : MonoBehaviour
    {
        [HideInInspector] public string fingerName;

        public bool  IsContacting   { get; private set; }
        public float ContactDepth   { get; private set; }

        // Plane the finger is pressing on. ContactNormal points OUT of the
        // contacted surface (i.e. the direction the fingertip would move to
        // un-overlap the collider). ContactSurfacePoint is the point on that
        // surface nearest the fingertip. Used by JupiterTester's wireframe
        // clamp to create a "hand cannot pass through the keyboard" illusion
        // without retargeting the real hand-tracked bones.
        public Vector3 ContactNormal       { get; private set; } = Vector3.up;
        public Vector3 ContactSurfacePoint { get; private set; }

        [Tooltip("Maximum penetration distance mapped to depth = 1.0 (metres)")]
        public float maxDepthMetres = 0.03f;

        [Tooltip("Layer index for touchable objects (must match EMS Contact layer)")]
        public int contactLayerIndex = 6;

        private SphereCollider _col;
        private Collider       _currentContact;

        void Awake()
        {
            _col = GetComponent<SphereCollider>();
            _col.isTrigger = true;

            var rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity  = false;
        }

        void OnTriggerEnter(Collider other)
        {
            // ONLY respond to objects on the contact layer
            if (other.gameObject.layer != contactLayerIndex) return;

            if (_currentContact == null)
            {
                _currentContact = other;
                IsContacting    = true;
                // Compute depth + normal + surface point now so the visual
                // clamp has valid data on the very first frame of contact,
                // before OnTriggerStay fires.
                ContactDepth = ComputeDepth(other);
            }
        }

        void OnTriggerExit(Collider other)
        {
            if (other == _currentContact)
            {
                _currentContact = null;
                IsContacting    = false;
                ContactDepth    = 0f;
            }
        }

        void OnTriggerStay(Collider other)
        {
            if (other != _currentContact) return;
            ContactDepth = ComputeDepth(other);
        }

        private float ComputeDepth(Collider other)
        {
            bool overlap = Physics.ComputePenetration(
                _col,  transform.position, transform.rotation,
                other, other.transform.position, other.transform.rotation,
                out Vector3 direction, out float distance
            );

            if (!overlap) return 0f;

            // `direction` is the unit vector along which our collider would
            // need to move to no longer overlap `other` — i.e. it points OUT
            // of the contacted surface. Cache for the visual clamp.
            ContactNormal       = direction;
            ContactSurfacePoint = transform.position + direction * distance;

            return Mathf.Clamp01(distance / maxDepthMetres);
        }
    }
}
