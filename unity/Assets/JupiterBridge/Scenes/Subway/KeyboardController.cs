using System;
using System.Text;
using UnityEngine;

namespace JupiterBridge.Subway
{
    /// <summary>
    /// Singleton aggregator for VirtualKey events. Maintains a text buffer
    /// (handles backspace, enter, space) and broadcasts TextChanged so
    /// monitors / other UI can subscribe.
    /// </summary>
    public class KeyboardController : MonoBehaviour
    {
        public static KeyboardController Instance { get; private set; }

        [Tooltip("Maximum characters held in the buffer (older trimmed off)")]
        public int maxBufferLength = 240;

        public string Text => _buffer.ToString();
        public event Action<string> TextChanged;

        readonly StringBuilder _buffer = new StringBuilder();

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        /// <summary>Called by VirtualKey on press. Special chars: '\b' = backspace, '\n' = enter, ' ' = space.</summary>
        public void HandleKeyPress(char c)
        {
            if (c == '\b')
            {
                if (_buffer.Length > 0) _buffer.Length -= 1;
            }
            else
            {
                _buffer.Append(c);
                if (_buffer.Length > maxBufferLength)
                    _buffer.Remove(0, _buffer.Length - maxBufferLength);
            }

            string txt = _buffer.ToString();
            Debug.Log($"[KeyboardController] '{c}' → \"{txt}\"");
            TextChanged?.Invoke(txt);
        }

        public void Clear()
        {
            _buffer.Clear();
            TextChanged?.Invoke("");
        }
    }
}
