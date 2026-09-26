using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SortQuest.Editor
{
    /// <summary>Explicit live test. The caller must clean up SORTQUEST_TEST_RECORD_ID afterward.</summary>
    public static class SortQuestApiSmokeTest
    {
        private static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        private static AsyncOperation waiting;
        private static GameObject host;
        private static double deadline;
        private static string queuePath;
        private static InsecureHttpOption previousHttpOption;

        public static void Run()
        {
            previousHttpOption = PlayerSettings.insecureHttpOption;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            deadline = EditorApplication.timeSinceStartup + 100;
            stack.Push(Check());
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Test deadline exceeded.");
                if (waiting != null && !waiting.isDone) return;
                waiting = null;
                if (stack.Count == 0) { Finish(0); return; }
                var current = stack.Peek();
                if (!current.MoveNext()) { stack.Pop(); return; }
                if (current.Current is IEnumerator nested) stack.Push(nested);
                else if (current.Current is AsyncOperation operation) waiting = operation;
            }
            catch (Exception error) { Debug.LogError("API_SMOKE_FAIL " + error.Message); Finish(1); }
        }

        private static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            PlayerSettings.insecureHttpOption = previousHttpOption;
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            if (queuePath != null && File.Exists(queuePath)) File.Delete(queuePath);
            if (queuePath != null && File.Exists(queuePath + ".tmp")) File.Delete(queuePath + ".tmp");
            if (code == 0) Debug.Log("API_SMOKE_PASS Unity serialization, HTTP, MongoDB round trip, duplicate, auth and offline checks.");
            EditorApplication.Exit(code);
        }

        private static IEnumerator Check()
        {
            string id = Environment.GetEnvironmentVariable("SORTQUEST_TEST_RECORD_ID");
            if (string.IsNullOrEmpty(id)) throw new Exception("Set SORTQUEST_TEST_RECORD_ID and arrange test cleanup.");
            host = new GameObject("API smoke test");
            var api = host.AddComponent<SortQuestApi>();
            api.BaseUrl = Environment.GetEnvironmentVariable("SORTQUEST_TEST_URL") ?? "http://127.0.0.1:8000";
            api.ApiKey = SortQuestApiSetup.ReadApiKey();
            bool passed = false;
            string error = null;
            yield return api.Health(r => passed = r != null && r.ok, e => error = e);
            Require(passed && error == null, "health");
            var record = new GraspRecord { record_id = id, session_id = "unity-api-smoke", player = "test", timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), source = "human", item_type = "battery_aa", correct_bin = "hazardous", hand = "right" };
            record.outcome.bin = "hazardous";
            record.outcome.correct = true;
            passed = false;
            yield return api.Upload(new[] { record }, r => passed = r.inserted == 1 && r.duplicates == 0, e => error = e);
            Require(passed && error == null, "insert serialized GraspRecord");
            passed = false;
            yield return api.Upload(new[] { record }, r => passed = r.inserted == 0 && r.duplicates == 1, e => error = e);
            Require(passed && error == null, "duplicate retry");
            passed = false;
            yield return api.ReadGoodGrasps("battery_aa", r => passed = Array.Exists(r.records, item => item.record_id == id && item.IsGood && item.image.id == ""), e => error = e);
            Require(passed && error == null, "read back Unity record");
            passed = false;
            yield return api.ReadStats(r => passed = r.total >= 1, e => error = e);
            Require(passed && error == null, "statistics");
            api.ApiKey = "wrong-key";
            yield return api.ReadStats(r => {}, e => error = e);
            Require(error != null && error.Contains("401"), "unauthorized response");
            error = null;
            api.BaseUrl = "http://127.0.0.1:1";
            yield return api.Health(r => {}, e => error = e);
            Require(error != null, "offline callback");

            var uploader = host.AddComponent<DataUploader>();
            string queueName = "smoke-" + id + ".json";
            queuePath = Path.Combine(Application.persistentDataPath, queueName);
            Set(uploader, "api", api);
            Set(uploader, "queueFileName", queueName);
            Call(uploader, "Queue", record);
            Call(uploader, "Queue", record);
            Require(uploader.PendingCount == 1 && File.Exists(queuePath), "queue persistence and deduplication");
            var loop = (IEnumerator)Call(uploader, "UploadLoop");
            Require(loop.MoveNext(), "offline upload begins");
            yield return (IEnumerator)loop.Current;
            loop.MoveNext();
            Require(uploader.PendingCount == 1 && uploader.LastError != null, "offline upload retains record");
            (loop as IDisposable)?.Dispose();
            UnityEngine.Object.DestroyImmediate(uploader);
            uploader = host.AddComponent<DataUploader>();
            Set(uploader, "api", api);
            Set(uploader, "queueFileName", queueName);
            Call(uploader, "RestoreQueue");
            Require(uploader.PendingCount == 1, "queue restored after component restart");
            api.BaseUrl = Environment.GetEnvironmentVariable("SORTQUEST_TEST_URL") ?? "http://127.0.0.1:8000";
            api.ApiKey = SortQuestApiSetup.ReadApiKey();
            loop = (IEnumerator)Call(uploader, "UploadLoop");
            loop.MoveNext();
            yield return (IEnumerator)loop.Current;
            loop.MoveNext();
            Require(uploader.PendingCount == 0 && JsonUtility.FromJson<SortQuestApi.Records>(File.ReadAllText(queuePath)).records.Length == 0, "successful retry clears durable queue");
            (loop as IDisposable)?.Dispose();
            var dataset = host.AddComponent<GraspDataset>();
            int events = 0;
            dataset.RecordAdded += r => events++;
            Require(dataset.MergeRemote(new[] { record, record }) == 1 && events == 0, "remote merge deduplicates without reupload events");
            Require(!System.Linq.Enumerable.Any(dataset.LocalRecords), "remote records excluded from local replay");
        }

        private static object Call(object target, string method, params object[] args)
            => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);

        private static void Set(object target, string field, object value)
            => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private static void Require(bool condition, string step)
        {
            if (!condition) throw new Exception("Failed " + step);
            Debug.Log("API_SMOKE_STEP " + step + " PASS");
        }
    }
}
