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
            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.Connect("192.0.2.1", 80);
                    api.BaseUrl = "http://" + ((IPEndPoint)socket.LocalEndPoint).Address + ":8000";
                }
            }
            catch (SocketException) { api.BaseUrl = "http://YOUR_COMPUTER_LAN_IP:8000"; }
            api.ApiKey = ReadApiKey();
            if (dataset.GetComponent<DataUploader>() == null) Undo.AddComponent<DataUploader>(dataset.gameObject);
            EditorUtility.SetDirty(api);
            EditorSceneManager.MarkSceneDirty(dataset.gameObject.scene);
            Selection.activeGameObject = dataset.gameObject;
            Debug.Log("SortQuest LAN API configured. Verify the computer address in the Inspector and save the scene.");
        }
    }
}
