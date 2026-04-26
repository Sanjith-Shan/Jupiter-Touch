using UnityEngine;
using JupiterBridge.Tests;

namespace JupiterBridge
{
    /// <summary>
    /// Reads contact state from a JupiterTester (which already does hand
    /// tracking + finger detection) and streams per-finger contact events
    /// to the PC bridge over UDP — at state-changes (low latency) and
    /// at a steady rate while contacting (smooth intensity updates).
    ///
    /// Add this component to the same GameObject as JupiterTester. Auto-
    /// creates a UDPSender singleton if one isn't already in the scene.
    /// </summary>
    [RequireComponent(typeof(JupiterTester))]
    public class EMSBridge : MonoBehaviour
    {
        [Tooltip("Continuous-update rate while a finger is in contact (Hz).")]
        public float sendRateHz = 90f;

        [Tooltip("If no UDPSender is in the scene, create one automatically.")]
        public bool autoCreateUDPSender = true;

        JupiterTester _tester;
        bool[] _wasActive = new bool[6];
        float  _timer;

        static readonly string[] FingerNames =
            { "Thumb", "Index", "Middle", "Ring", "Pinky", "Palm" };

        void Awake()
        {
            _tester = GetComponent<JupiterTester>();
            if (autoCreateUDPSender && UDPSender.Instance == null)
            {
                var go = new GameObject("UDPSender");
                go.AddComponent<UDPSender>();
                Debug.Log("[EMSBridge] Auto-created UDPSender — set its IP in the Inspector.");
            }
        }

        void Update()
        {
            if (_tester == null || UDPSender.Instance == null) return;

            // 1) Edge events — fire immediately on touch / release
            for (int i = 0; i < 6; i++)
            {
                bool active = _tester.IsContacting[i];
                if (active != _wasActive[i])
                {
                    SendContact(i, active, _tester.ContactDepth[i]);
                    _wasActive[i] = active;
                }
            }

            // 2) Continuous intensity updates while contacting
            _timer += Time.deltaTime;
            float interval = 1f / Mathf.Max(1f, sendRateHz);
            if (_timer >= interval)
            {
                _timer = 0f;
                for (int i = 0; i < 6; i++)
                    if (_tester.IsContacting[i])
                        SendContact(i, true, _tester.ContactDepth[i]);
            }
        }

        void SendContact(int index, bool active, float depth)
        {
            string json = active
                ? $"{{\"finger\":\"{FingerNames[index]}\",\"active\":true,\"depth\":{depth:F3}}}"
                : $"{{\"finger\":\"{FingerNames[index]}\",\"active\":false,\"depth\":0}}";
            UDPSender.Instance.Send(json);
        }

        void OnDestroy()
        {
            // Safety: turn everything off when this component goes away
            if (UDPSender.Instance == null) return;
            for (int i = 0; i < 6; i++)
                SendContact(i, false, 0f);
        }
    }
}
