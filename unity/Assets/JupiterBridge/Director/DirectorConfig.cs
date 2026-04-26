using UnityEngine;

namespace JupiterBridge.Director
{
    [CreateAssetMenu(menuName = "JupiterBridge/DirectorConfig", fileName = "DirectorConfig")]
    public class DirectorConfig : ScriptableObject
    {
        [Tooltip("IP of the PC running director_server.py")]
        public string pcIP   = "192.168.1.100";
        public int    port   = 8765;
        public float  reconnectBaseDelay = 1f;  // seconds, doubles each attempt up to 16 s

        public string WsUrl => $"ws://{pcIP}:{port}/ws/quest";
    }
}
