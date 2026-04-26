using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace JupiterBridge.Director
{
    /// <summary>
    /// Singleton WebSocket client that connects to the PC director server.
    /// Uses built-in System.Net.WebSockets — no external package required.
    /// Thread-safe: all WS callbacks are marshalled to the Unity main thread.
    /// </summary>
    [RequireComponent(typeof(DirectorRouter))]
    public class DirectorClient : MonoBehaviour
    {
        public static DirectorClient Instance { get; private set; }

        [SerializeField] DirectorConfig _config;

        [Tooltip("Override the PC IP at runtime without changing the config asset.")]
        public string ipOverride;

        ClientWebSocket _ws;
        DirectorRouter  _router;
        CancellationTokenSource _cts;

        readonly ConcurrentQueue<Action> _mainThreadQueue = new();

        bool  _intentionalClose;
        float _reconnectDelay;
        Coroutine _reconnectCoroutine;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _router         = GetComponent<DirectorRouter>();
            _reconnectDelay = _config ? _config.reconnectBaseDelay : 1f;
        }

        void Start() => _ = ConnectAsync();

        void Update()
        {
            while (_mainThreadQueue.TryDequeue(out var action)) action();
        }

        // ── Connection ────────────────────────────────────────────────────

        string WsUrl()
        {
            if (_config == null) return "ws://192.168.1.100:8765/ws/quest";
            string ip = string.IsNullOrEmpty(ipOverride) ? _config.pcIP : ipOverride;
            return $"ws://{ip}:{_config.port}/ws/quest";
        }

        async Task ConnectAsync()
        {
            _intentionalClose = false;
            _cts = new CancellationTokenSource();
            _ws  = new ClientWebSocket();

            string url = WsUrl();
            Debug.Log($"[DirectorClient] Connecting → {url}");

            try
            {
                await _ws.ConnectAsync(new Uri(url), _cts.Token);

                _mainThreadQueue.Enqueue(() =>
                {
                    Debug.Log("[DirectorClient] Connected");
                    _reconnectDelay = _config ? _config.reconnectBaseDelay : 1f;
                    // Send initial ping so the server knows we're alive
                    long t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    SendRaw($"{{\"type\":\"ping\",\"t\":{t}}}");
                });

                await ReceiveLoopAsync();
            }
            catch (OperationCanceledException) { /* intentional shutdown */ }
            catch (Exception e)
            {
                if (!_intentionalClose)
                    _mainThreadQueue.Enqueue(() =>
                    {
                        Debug.LogWarning($"[DirectorClient] Connect failed: {e.Message}");
                        ScheduleReconnect();
                    });
            }
        }

        async Task ReceiveLoopAsync()
        {
            var buffer = new byte[4096];
            var stream = new MemoryStream();

            try
            {
                while (_ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    stream.SetLength(0);
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            if (!_intentionalClose)
                                _mainThreadQueue.Enqueue(ScheduleReconnect);
                            return;
                        }

                        stream.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    string json = Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
                    _mainThreadQueue.Enqueue(() => _router.Dispatch(json));
                }
            }
            catch (OperationCanceledException) { /* intentional */ }
            catch (Exception e)
            {
                if (!_intentionalClose)
                    _mainThreadQueue.Enqueue(() =>
                    {
                        Debug.LogWarning($"[DirectorClient] Receive error: {e.Message}");
                        ScheduleReconnect();
                    });
            }
        }

        // ── Send ──────────────────────────────────────────────────────────

        public void SendRaw(string json)
        {
            if (_ws == null || _ws.State != WebSocketState.Open) return;
            byte[] data = Encoding.UTF8.GetBytes(json);
            _ = _ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        // ── Reconnect ─────────────────────────────────────────────────────

        void ScheduleReconnect()
        {
            if (_intentionalClose) return;
            Debug.Log($"[DirectorClient] Reconnecting in {_reconnectDelay:F1}s…");
            if (_reconnectCoroutine != null) StopCoroutine(_reconnectCoroutine);
            _reconnectCoroutine = StartCoroutine(ReconnectAfter(_reconnectDelay));
            _reconnectDelay = Mathf.Min(_reconnectDelay * 2f, 16f);
        }

        IEnumerator ReconnectAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            _ = ConnectAsync();
        }

        void OnDestroy()
        {
            _intentionalClose = true;
            _cts?.Cancel();
            _ws?.Dispose();
        }
    }
}
