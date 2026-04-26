using System;
using System.Collections.Generic;
using UnityEngine;

namespace JupiterBridge.Director
{
    /// <summary>
    /// Dispatches JSON messages from the director server to registered handlers.
    /// Messages are always delivered on the Unity main thread by DirectorClient.
    /// </summary>
    public class DirectorRouter : MonoBehaviour
    {
        // External systems (e.g. SceneLoader, EMS controller) subscribe to these.
        public static event Action<SceneLoadMsg>   OnSceneLoad;
        public static event Action<EventTriggerMsg> OnEventTrigger;
        public static event Action<VarSetMsg>      OnVarSet;

        DirectorClient _client;

        void Awake() => _client = GetComponent<DirectorClient>();

        public void Dispatch(string json)
        {
            try
            {
                // Peek at the "type" field before full deserialisation.
                var wrapper = JsonUtility.FromJson<MsgWrapper>(json);
                if (wrapper == null) return;

                switch (wrapper.type)
                {
                    case "scene.load":
                        var sl = JsonUtility.FromJson<SceneLoadMsg>(json);
                        Debug.Log($"[DirectorRouter] scene.load → {sl.name}  fade={sl.fade}");
                        OnSceneLoad?.Invoke(sl);
                        Ack("scene.load");
                        break;

                    case "event.trigger":
                        var et = JsonUtility.FromJson<EventTriggerMsg>(json);
                        Debug.Log($"[DirectorRouter] event.trigger → {et.id}");
                        OnEventTrigger?.Invoke(et);
                        Ack("event.trigger");
                        break;

                    case "var.set":
                        var vs = JsonUtility.FromJson<VarSetMsg>(json);
                        Debug.Log($"[DirectorRouter] var.set → {vs.key}={vs.value}");
                        OnVarSet?.Invoke(vs);
                        Ack("var.set");
                        break;

                    case "ping":
                        var ping = JsonUtility.FromJson<PingMsg>(json);
                        _client.SendRaw($"{{\"type\":\"pong\",\"t\":{ping.t}}}");
                        break;

                    default:
                        Debug.LogWarning($"[DirectorRouter] Unknown message type: {wrapper.type}");
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[DirectorRouter] Dispatch error: {e.Message}\nJSON: {json}");
            }
        }

        void Ack(string ofType, bool ok = true)
        {
            long tMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _client.SendRaw($"{{\"type\":\"ack\",\"of\":\"{ofType}\",\"ok\":{(ok ? "true" : "false")},\"t_ms\":{tMs}}}");
        }

        // ── Message POCOs ──────────────────────────────────────────────────
        [Serializable] class MsgWrapper       { public string type; }
        [Serializable] public class SceneLoadMsg    { public string type; public string name; public float fade = 0.5f; }
        [Serializable] public class EventTriggerMsg { public string type; public string id; }
        [Serializable] public class VarSetMsg       { public string type; public string key; public string value; }
        [Serializable] class PingMsg          { public string type; public long t; }
    }
}
