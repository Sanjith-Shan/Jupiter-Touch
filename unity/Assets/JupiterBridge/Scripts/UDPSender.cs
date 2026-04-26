using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace JupiterBridge
{
    /// <summary>
    /// Thin UDP client. Attach to any persistent GameObject.
    /// Other scripts call JupiterBridge.UDPSender.Instance.Send(json).
    /// </summary>
    public class UDPSender : MonoBehaviour
    {
        public static UDPSender Instance { get; private set; }

        [Header("PC Bridge target")]
        [Tooltip("IP of the PC running pc_bridge.py. Use 192.168.x.x on shared WiFi.")]
        public string bridgeIP   = "192.168.1.100";
        public int    bridgePort = 8053;

        private UdpClient   _client;
        private IPEndPoint  _endpoint;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _endpoint = new IPEndPoint(IPAddress.Parse(bridgeIP), bridgePort);
            _client   = new UdpClient();
            Debug.Log($"[UDPSender] Ready → {bridgeIP}:{bridgePort}");
        }

        /// <summary>Send a raw JSON string (fire-and-forget).</summary>
        public void Send(string json)
        {
            byte[] data = Encoding.UTF8.GetBytes(json);
            _client.Send(data, data.Length, _endpoint);
        }

        void OnDestroy()
        {
            _client?.Close();
        }
    }
}
