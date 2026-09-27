using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SortQuest
{
    /// <summary>Durable, at-least-once uploads; duplicate IDs are safe on the server.</summary>
    [RequireComponent(typeof(SortQuestApi))]
    public class DataUploader : MonoBehaviour
    {
        [SerializeField] private GraspDataset dataset;
        [SerializeField, Range(1, 100)] private int batchSize = 100;
        [SerializeField, Min(1)] private float uploadInterval = 5;
        [SerializeField] private string queueFileName = "pending-grasps.json";
        private SortQuestApi api;
        private readonly List<GraspRecord> pending = new List<GraspRecord>();
        private readonly HashSet<string> ids = new HashSet<string>();
        private bool initialized;
        public int PendingCount => pending.Count;
        public string LastError { get; private set; }
        private string QueuePath => Path.Combine(Application.persistentDataPath, Path.GetFileName(queueFileName));

        private void Start() { initialized = true; Begin(); }
        private void OnEnable() { if (initialized) Begin(); }
        private void OnDisable()
        {
            if (dataset != null) dataset.RecordAdded -= Queue;
            StopAllCoroutines();
        }

        private void Begin()
        {
            api = GetComponent<SortQuestApi>();
            if (dataset == null) dataset = FindAnyObjectByType<GraspDataset>();
            if (dataset == null) { LastError = "DataUploader needs a GraspDataset."; Debug.LogWarning(LastError, this); return; }
            RestoreQueue();
            // Replays local history on startup, recovering even a crash during a queue write.
            // Server deduplication makes this safe; downloaded records are memory-only.
            foreach (var record in dataset.LocalRecords) AddPending(record);
            SaveQueue();
            dataset.RecordAdded -= Queue;
            dataset.RecordAdded += Queue;
            StartCoroutine(UploadLoop());
            StartCoroutine(UploadAnnotations());
        }

        private void RestoreQueue()
        {
            try
            {
                if (File.Exists(QueuePath))
                {
                    var saved = GraspJson.Read<SortQuestApi.Records>(File.ReadAllText(QueuePath));
                    if (saved?.records != null) foreach (var record in saved.records) AddPending(record);
                }
            }
            catch (Exception) { Debug.LogWarning("[SortQuest] Queue unreadable; recovering records from dataset.", this); }
        }

        private void AddPending(GraspRecord record)
        {
            if (record != null && !string.IsNullOrEmpty(record.record_id) && ids.Add(record.record_id)) pending.Add(record);
        }

        private void Queue(GraspRecord record) { AddPending(record); SaveQueue(); }

        private void SaveQueue()
        {
            try
            {
                string temp = QueuePath + ".tmp";
                File.WriteAllText(temp, GraspJson.Write(new SortQuestApi.Records { records = pending.ToArray() }));
                if (File.Exists(QueuePath)) File.Replace(temp, QueuePath, null);
                else File.Move(temp, QueuePath);
            }
            catch (Exception) { LastError = "Could not persist upload queue; local dataset remains available for recovery."; Debug.LogWarning(LastError, this); }
        }

        private IEnumerator UploadLoop()
        {
            float retry = Mathf.Max(1, uploadInterval);
            while (true)
            {
                if (pending.Count == 0) { yield return new WaitForSecondsRealtime(Mathf.Max(1, uploadInterval)); continue; }
                var batch = pending.GetRange(0, Mathf.Min(Mathf.Clamp(batchSize, 1, 100), pending.Count)).ToArray();
                bool acknowledged = false;
                yield return api.Upload(batch, result =>
                {
                    acknowledged = result != null && result.inserted >= 0 && result.duplicates >= 0 &&
                        result.inserted + result.duplicates == batch.Length;
                    if (!acknowledged) LastError = "Server did not acknowledge every record; retaining batch.";
                }, error => LastError = error);
                if (acknowledged)
                {
                    pending.RemoveRange(0, batch.Length);
                    foreach (var record in batch) ids.Remove(record.record_id);
                    SaveQueue();
                    LastError = null;
                    retry = Mathf.Max(1, uploadInterval);
                }
                else
                {
                    Debug.LogWarning("[SortQuest] " + LastError + " Queued records will retry.", this);
                    retry = Mathf.Min(60, retry * 2);
                }
                yield return new WaitForSecondsRealtime(retry);
            }
        }

        // The durable append-only annotation journal is its own queue. Replay is idempotent on the server.
        private IEnumerator UploadAnnotations()
        {
            int sent = 0;
            float retry = Mathf.Max(1, uploadInterval);
            while (true)
            {
                if (sent < dataset.Annotations.Count)
                {
                    var batch = dataset.Annotations.GetRange(sent, Mathf.Min(100, dataset.Annotations.Count - sent)).ToArray();
                    bool acknowledged = false;
                    yield return api.UploadAnnotations(batch, result => acknowledged = result != null &&
                        result.inserted >= 0 && result.duplicates >= 0 && result.inserted + result.duplicates == batch.Length,
                        error => Debug.LogWarning("[SortQuest] Annotation upload retained for retry: " + error));
                    if (acknowledged) { sent += batch.Length; retry = Mathf.Max(1, uploadInterval); }
                    else retry = Mathf.Min(60, retry * 2);
                }
                yield return new WaitForSecondsRealtime(retry);
            }
        }

        /// <summary>Download good grasps into memory without uploading or augmenting them again.</summary>
        public void DownloadGoodGrasps(string itemType)
        {
            if (api == null || dataset == null) return;
            StartCoroutine(api.ReadGoodGrasps(itemType, result =>
            {
                if (result?.records != null) dataset.MergeRemote(result.records);
            }, error => { LastError = error; Debug.LogWarning(error, this); }));
        }
    }
}
