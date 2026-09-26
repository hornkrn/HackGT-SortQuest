using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SortQuest.Editor
{
    public static class SortQuestApiSetup
    {
        /// <summary>Gitignored, but under Resources so it is included in builds.</summary>
        public const string SettingsPath = "Assets/Resources/" + SortQuestApi.SettingsResource + ".json";

        public static string ReadApiKey()
        {
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".env");
            if (!File.Exists(path)) return "";
            foreach (string line in File.ReadAllLines(path))
            {
                int separator = line.IndexOf('=');
                if (separator > 0 && line.Substring(0, separator).Trim() == "SORTQUEST_API_KEY")
                    return line.Substring(separator + 1).Trim().Trim('\'', '"');
            }
            return "";
        }

        [MenuItem("SortQuest/Configure LAN API")]
        public static void Configure()
        {
            var dataset = UnityEngine.Object.FindAnyObjectByType<GraspDataset>();
            if (dataset == null) { Debug.LogError("Open the game scene containing GraspDataset first."); return; }
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            var api = dataset.GetComponent<SortQuestApi>();
            if (api == null) api = Undo.AddComponent<SortQuestApi>(dataset.gameObject);
            Undo.RecordObject(api, "Configure SortQuest LAN API");
            string baseUrl;
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.Connect("192.0.2.1", 80);
                    baseUrl = "http://" + ((IPEndPoint)socket.LocalEndPoint).Address + ":8000";
                }
            }
            catch (SocketException) { baseUrl = "http://YOUR_COMPUTER_LAN_IP:8000"; }
            string apiKey = ReadApiKey();

            // Address and key go in a gitignored file; the scene keeps only the default address and no key.
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, JsonUtility.ToJson(
                new SortQuestApi.LocalSettings { baseUrl = baseUrl, apiKey = apiKey }, true));
            AssetDatabase.ImportAsset(SettingsPath);
            api.BaseUrl = "http://127.0.0.1:8000";
            api.ApiKey = "";

            if (dataset.GetComponent<DataUploader>() == null) Undo.AddComponent<DataUploader>(dataset.gameObject);
            EditorUtility.SetDirty(api);
            EditorSceneManager.MarkSceneDirty(dataset.gameObject.scene);
            Selection.activeGameObject = dataset.gameObject;
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogWarning("SortQuest LAN API: no SORTQUEST_API_KEY in .env. Start the server once to create it, then run this again.");
            }
            Debug.Log($"SortQuest LAN API configured for {baseUrl}. Address and key saved to {SettingsPath} (gitignored, included in builds). Save the scene.");
        }
    }
}
