using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SortQuest
{
    /// <summary>
    /// All grasp records, kept in memory and appended to a local JSON Lines file
    /// (one JSON object per line) under Application.persistentDataPath. Never touches the network.
    /// </summary>
    public class GraspDataset : MonoBehaviour
    {
        [Tooltip("File name under Application.persistentDataPath.")]
        [SerializeField] private string fileName = "grasps.jsonl";

        [Tooltip("Load grasps saved in earlier sessions at startup.")]
        [SerializeField] private bool loadSavedGrasps = true;

        [Tooltip("Leave empty for a random anonymous id each session.")]
        [SerializeField] private string playerId = "";

        public event Action<GraspRecord> RecordAdded;

        public string SessionId { get; private set; }
        public string PlayerId { get; private set; }
        public string FilePath => Path.Combine(Application.persistentDataPath, fileName);
        public IReadOnlyList<GraspRecord> Records => records;

        private readonly List<GraspRecord> records = new List<GraspRecord>();

        private void Awake()
        {
            SessionId = "s-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
            PlayerId = string.IsNullOrEmpty(playerId) ? "anon-" + Random.Range(0, 0x10000).ToString("x4") : playerId;
            if (loadSavedGrasps)
            {
                Load();
            }
        }

        public void Add(GraspRecord record)
        {
            records.Add(record);
            try
            {
                File.AppendAllText(FilePath, JsonUtility.ToJson(record) + "\n");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SortQuest] Couldn't save grasp to {FilePath}: {e.Message}");
            }
            RecordAdded?.Invoke(record);
        }

        /// <summary>Successful (good) grasps for one item type that the robot can learn from (not its own).</summary>
        public List<GraspRecord> GoodGrasps(ItemType itemType)
        {
            string id = TrashTypes.ItemId(itemType);
            return records.Where(r => r.item_type == id && r.IsGood && r.source != GraspRecord.SourceRobot).ToList();
        }

        public int CountGood(ItemType itemType)
        {
            string id = TrashTypes.ItemId(itemType);
            return records.Count(r => r.item_type == id && r.IsGood && r.source != GraspRecord.SourceRobot);
        }

        private void Load()
        {
            if (!File.Exists(FilePath))
            {
                return;
            }
            int skipped = 0;
            try
            {
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }
                    try
                    {
                        records.Add(JsonUtility.FromJson<GraspRecord>(line));
                    }
                    catch (Exception)
                    {
                        skipped++;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SortQuest] Couldn't read {FilePath}: {e.Message}");
            }
            Debug.Log($"[SortQuest] Loaded {records.Count} saved grasps from {FilePath}" +
                      (skipped > 0 ? $" (skipped {skipped} bad lines)" : ""));
        }
    }
}
