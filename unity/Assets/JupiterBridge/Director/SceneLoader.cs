using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace JupiterBridge.Director
{
    /// <summary>
    /// Listens for DirectorRouter.OnSceneLoad and performs a fade → async load → fade-in.
    /// Attach to the same persistent GameObject as DirectorClient + DirectorRouter.
    /// Create a full-screen black Image (Canvas + CanvasGroup) and assign it to FadePanel.
    /// </summary>
    public class SceneLoader : MonoBehaviour
    {
        [Header("Fade overlay")]
        [Tooltip("CanvasGroup on a full-screen black Image placed in the Bootstrap scene.")]
        [SerializeField] CanvasGroup _fadePanel;

        [SerializeField] float _defaultFade = 0.5f;

        bool _loading;

        void OnEnable()  => DirectorRouter.OnSceneLoad += HandleSceneLoad;
        void OnDisable() => DirectorRouter.OnSceneLoad -= HandleSceneLoad;

        void HandleSceneLoad(DirectorRouter.SceneLoadMsg msg)
        {
            if (_loading)
            {
                Debug.LogWarning("[SceneLoader] Scene load already in progress — ignoring request");
                return;
            }
            float fade = msg.fade > 0 ? msg.fade : _defaultFade;
            StartCoroutine(LoadWithFade(msg.name, fade));
        }

        IEnumerator LoadWithFade(string sceneName, float fadeDuration)
        {
            _loading = true;

            // Fade to black
            yield return Fade(0f, 1f, fadeDuration * 0.5f);

            // Unload all non-persistent scenes additive-loaded previously
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.name != gameObject.scene.name)  // keep Bootstrap
                    yield return SceneManager.UnloadSceneAsync(s);
            }

            // Load the new scene additively so Bootstrap persists
            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            while (!op.isDone) yield return null;

            // Activate it
            Scene loaded = SceneManager.GetSceneByName(sceneName);
            SceneManager.SetActiveScene(loaded);

            // Fade back in
            yield return Fade(1f, 0f, fadeDuration * 0.5f);

            _loading = false;
            Debug.Log($"[SceneLoader] Loaded: {sceneName}");
        }

        IEnumerator Fade(float from, float to, float duration)
        {
            if (_fadePanel == null) yield break;
            _fadePanel.gameObject.SetActive(true);
            float t = 0;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                _fadePanel.alpha = Mathf.Lerp(from, to, t / duration);
                yield return null;
            }
            _fadePanel.alpha = to;
            if (to == 0f) _fadePanel.gameObject.SetActive(false);
        }
    }
}
