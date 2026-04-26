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
                out _, out float distance
            );

            if (!overlap) return 0f;
            return Mathf.Clamp01(distance / maxDepthMetres);
        }
    }
}
