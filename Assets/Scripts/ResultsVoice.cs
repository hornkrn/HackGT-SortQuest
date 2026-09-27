using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SortQuest
{
    /// <summary>
    /// Reads the Results screen aloud in the tutorial narrator's ElevenLabs voice. The Python server turns the
    /// text into speech (it holds the ElevenLabs key), and the game never waits on it: if the server or
    /// ElevenLabs is unavailable, Results stays silent with the same text on screen.
    /// Installs itself in any scene with a GameManager, so no scene wiring is needed.
    /// </summary>
    public sealed class ResultsVoice : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float volume = 1f;

        private GameManager game;
        private SortQuestApi api;
        private AudioSource voice;
        private int requestId; // audio that arrives after the player has moved on is dropped

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SceneManager.sceneLoaded -= Install;
            SceneManager.sceneLoaded += Install;
        }

        private static void Install(Scene scene, LoadSceneMode mode)
        {
            bool hasGame = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponentInChildren<ResultsVoice>(true) != null) return;
                if (root.GetComponentInChildren<GameManager>(true) != null) hasGame = true;
            }
            if (!hasGame) return;
            var go = new GameObject("Results voice");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<ResultsVoice>();
        }

        private void Start()
        {
            game = FindAnyObjectByType<GameManager>();
            api = FindAnyObjectByType<SortQuestApi>();
            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0f; // like the tutorial narrator: the same loudness wherever the player looks
            voice.volume = volume;
            if (game != null) game.StateChanged += HandleStateChanged;
        }

        private void OnDestroy()
        {
            if (game != null) game.StateChanged -= HandleStateChanged;
            if (voice != null && voice.clip != null) Destroy(voice.clip);
        }

        private void HandleStateChanged(GameState state)
        {
            requestId++;
            if (state != GameState.Results)
            {
                if (voice.isPlaying) voice.Stop();
                return;
            }
            if (api != null) StartCoroutine(Say(game.SpokenResults(), requestId));
        }

        private IEnumerator Say(string text, int id)
        {
            yield return api.Speak(text, clip =>
            {
                if (id != requestId || game.State != GameState.Results)
                {
                    Destroy(clip);
                    return;
                }
                if (voice.clip != null) Destroy(voice.clip);
                voice.clip = clip;
                voice.Play();
            }, error => Debug.Log("[SortQuest] Results voice unavailable: " + error));
        }
    }
}
