using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace SortQuest
{
    /// <summary>
    /// HTTP access only. MongoDB credentials must stay on the Python server.
    /// The API address and key come from Assets/Resources/SortQuestApiSettings.json, which is gitignored
    /// (SortQuest > Configure LAN API writes it) but still packed into builds, so the key is never saved
    /// in the scene or pushed to Git. Values in that file override the Inspector fields.
    /// </summary>
    public class SortQuestApi : MonoBehaviour
    {
        /// <summary>Resources path (no extension) of the local, gitignored settings file.</summary>
        public const string SettingsResource = "SortQuestApiSettings";

        [Serializable]
        public class LocalSettings
        {
            public string baseUrl;
            public string apiKey;
        }

        [SerializeField] private string baseUrl = "http://127.0.0.1:8000";

        [Tooltip("Leave empty. The key is read from the gitignored settings file so it is never saved in the scene.")]
        [SerializeField] private string apiKey = "";
        [SerializeField, Min(1)] private int timeoutSeconds = 15;
        public string BaseUrl { get => baseUrl; set => baseUrl = value; }
        public string ApiKey { get => apiKey; set => apiKey = value; }

        [Serializable] public class Records { public GraspRecord[] records; }
        [Serializable] public class Annotations { public GraspFeasibilityAnnotation[] annotations; }
        [Serializable] public class UploadResult { public int inserted; public int duplicates; }
        [Serializable] public class HealthResult { public bool ok; }
        [Serializable] public class Count { public string item_type; public string source; public int total; public int good; }
        [Serializable] public class RobotDay { public string date; public int attempts; public int successes; public double success_rate; }
        [Serializable] public class Stats { public int total; public Count[] counts; public RobotDay[] robot_daily; }

        private void Awake()
        {
            TextAsset asset = Resources.Load<TextAsset>(SettingsResource);
            if (asset == null)
            {
                if (string.IsNullOrEmpty(apiKey))
                {
                    Debug.LogWarning("[SortQuest] No API settings file; run SortQuest > Configure LAN API. Uploads will be rejected.", this);
                }
                return;
            }
            try
            {
                LocalSettings settings = GraspJson.Read<LocalSettings>(asset.text);
                if (!string.IsNullOrEmpty(settings.baseUrl)) baseUrl = settings.baseUrl;
                if (!string.IsNullOrEmpty(settings.apiKey)) apiKey = settings.apiKey;
            }
            catch (Exception)
            {
                Debug.LogWarning("[SortQuest] API settings file is not valid JSON; using Inspector values.", this);
            }
        }

        public IEnumerator Health(Action<HealthResult> success, Action<string> failure)
            => Request("GET", "/health", null, success, failure);

        public IEnumerator Upload(GraspRecord[] records, Action<UploadResult> success, Action<string> failure)
            => Request("POST", "/grasps", GraspJson.Write(new Records { records = records }), success, failure);

        public IEnumerator UploadAnnotations(GraspFeasibilityAnnotation[] annotations, Action<UploadResult> success, Action<string> failure)
            => Request("POST", "/grasp-annotations", GraspJson.Write(new Annotations { annotations = annotations }), success, failure);

        public IEnumerator ReadGoodGrasps(string itemType, Action<Records> success, Action<string> failure, int limit = 500)
            => Request("GET", "/grasps?good=true&source=human,augmented&item_type=" +
                UnityWebRequest.EscapeURL(itemType) + "&limit=" + Mathf.Clamp(limit, 1, 1000), null, success, failure);

        public IEnumerator ReadStats(Action<Stats> success, Action<string> failure)
            => Request("GET", "/stats", null, success, failure);

        [Serializable] private class SpeechBody { public string text; }

        /// <summary>
        /// Spoken audio for a short text (up to 400 characters). The server asks ElevenLabs for it, so the
        /// ElevenLabs key never ships in the app. Allow a few seconds; callers must not wait on it.
        /// </summary>
        public IEnumerator Speak(string text, Action<AudioClip> success, Action<string> failure)
        {
            if (!ValidBaseUrl(failure)) yield break;
            string url = baseUrl.TrimEnd('/') + "/speech";
            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.timeout = Mathf.Max(timeoutSeconds, 20);
                request.redirectLimit = 0;
                var audio = new DownloadHandlerAudioClip(url, AudioType.MPEG);
                request.downloadHandler = audio;
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(GraspJson.Write(new SpeechBody { text = text })));
                request.SetRequestHeader("Content-Type", "application/json");
                if (!string.IsNullOrEmpty(apiKey)) request.SetRequestHeader("X-API-Key", apiKey);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    failure?.Invoke("Speech request failed (HTTP " + request.responseCode + ", " + request.result + ").");
                    yield break;
                }
                AudioClip clip = audio.audioClip;
                if (clip == null) failure?.Invoke("Speech audio could not be decoded.");
                else success?.Invoke(clip);
            }
        }

        private bool ValidBaseUrl(Action<string> failure)
        {
            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri uri) &&
                (uri.Scheme == "http" || uri.Scheme == "https") && string.IsNullOrEmpty(uri.UserInfo))
            {
                return true;
            }
            failure?.Invoke("Set a valid HTTP(S) API URL without credentials.");
            return false;
        }

        private IEnumerator Request<T>(string method, string path, string body, Action<T> success, Action<string> failure)
        {
            if (!ValidBaseUrl(failure)) yield break;
            using (var request = new UnityWebRequest(baseUrl.TrimEnd('/') + path, method))
            {
                request.timeout = Mathf.Max(1, timeoutSeconds);
                request.redirectLimit = 0;
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Accept", "application/json");
                if (!string.IsNullOrEmpty(apiKey)) request.SetRequestHeader("X-API-Key", apiKey);
                if (body != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    failure?.Invoke("API request failed (HTTP " + request.responseCode + ", " + request.result + ").");
                    yield break;
                }
                T result;
                try { result = GraspJson.Read<T>(request.downloadHandler.text); }
                catch (Exception) { failure?.Invoke("API returned invalid JSON."); yield break; }
                success?.Invoke(result);
            }
        }
    }
}
