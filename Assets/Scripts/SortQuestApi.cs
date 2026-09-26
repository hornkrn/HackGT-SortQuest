using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace SortQuest
{
    /// <summary>HTTP access only. MongoDB credentials must stay on the Python server.</summary>
    public class SortQuestApi : MonoBehaviour
    {
        [SerializeField] private string baseUrl = "http://127.0.0.1:8000";
        [SerializeField] private string apiKey = "";
        [SerializeField, Min(1)] private int timeoutSeconds = 15;
        public string BaseUrl { get => baseUrl; set => baseUrl = value; }
        public string ApiKey { get => apiKey; set => apiKey = value; }

        [Serializable] public class Records { public GraspRecord[] records; }
        [Serializable] public class UploadResult { public int inserted; public int duplicates; }
        [Serializable] public class HealthResult { public bool ok; }
        [Serializable] public class Count { public string item_type; public string source; public int total; public int good; }
        [Serializable] public class RobotDay { public string date; public int attempts; public int successes; public double success_rate; }
        [Serializable] public class Stats { public int total; public Count[] counts; public RobotDay[] robot_daily; }

        public IEnumerator Health(Action<HealthResult> success, Action<string> failure)
            => Request("GET", "/health", null, success, failure);

        public IEnumerator Upload(GraspRecord[] records, Action<UploadResult> success, Action<string> failure)
            => Request("POST", "/grasps", JsonUtility.ToJson(new Records { records = records }), success, failure);

        public IEnumerator ReadGoodGrasps(string itemType, Action<Records> success, Action<string> failure, int limit = 500)
            => Request("GET", "/grasps?good=true&source=human,augmented&item_type=" +
                UnityWebRequest.EscapeURL(itemType) + "&limit=" + Mathf.Clamp(limit, 1, 1000), null, success, failure);

        public IEnumerator ReadStats(Action<Stats> success, Action<string> failure)
            => Request("GET", "/stats", null, success, failure);

        private IEnumerator Request<T>(string method, string path, string body, Action<T> success, Action<string> failure)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo))
            {
                failure?.Invoke("Set a valid HTTP(S) API URL without credentials.");
                yield break;
            }
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
                try { result = JsonUtility.FromJson<T>(request.downloadHandler.text); }
                catch (Exception) { failure?.Invoke("API returned invalid JSON."); yield break; }
                success?.Invoke(result);
            }
        }
    }
}
