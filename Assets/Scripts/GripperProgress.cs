using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// Remembers each gripper's best result across sessions (certified, most items mastered, games played),
    /// saved as progress.json under Application.persistentDataPath. Shown as badges on the menu.
    /// </summary>
    public class GripperProgress : MonoBehaviour
    {
        [Serializable]
        public class Entry
        {
            public string gripper_id;
            public bool certified;
            public int best_mastered;
            public int items_total;
            public int games_played;
            public string last_played_utc;
        }

        [Serializable]
        private class SaveData
        {
            public Entry[] entries = new Entry[0];
        }

        [SerializeField] private string fileName = "progress.json";

        private SaveData data = new SaveData();

        private string FilePath => Path.Combine(Application.persistentDataPath, fileName);

        private void Awake()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    data = JsonUtility.FromJson<SaveData>(File.ReadAllText(FilePath)) ?? new SaveData();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SortQuest] Couldn't read {FilePath}: {e.Message}");
                data = new SaveData();
            }
        }

        public Entry Get(string gripperId)
        {
            foreach (Entry entry in data.entries)
            {
                if (entry.gripper_id == gripperId)
                {
                    return entry;
                }
            }
            return null;
        }

        /// <summary>Records a game's result for a gripper and saves. newGame is false when updating the same game.</summary>
        public void Record(string gripperId, bool certified, int mastered, int itemsTotal, bool newGame = true)
        {
            Entry entry = Get(gripperId);
            if (entry == null)
            {
                entry = new Entry { gripper_id = gripperId };
                Array.Resize(ref data.entries, data.entries.Length + 1);
                data.entries[data.entries.Length - 1] = entry;
            }
            entry.certified |= certified;
            entry.best_mastered = Mathf.Max(entry.best_mastered, mastered);
            entry.items_total = itemsTotal;
            if (newGame)
            {
                entry.games_played++;
            }
            entry.last_played_utc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SortQuest] Couldn't save {FilePath}: {e.Message}");
            }
        }

        /// <summary>Short text for a menu badge, such as "CERTIFIED" or "best: 3 of 6 mastered".</summary>
        public string Badge(string gripperId)
        {
            Entry entry = Get(gripperId);
            if (entry == null || entry.games_played == 0)
            {
                return "not trained yet";
            }
            return entry.certified ? "CERTIFIED" : $"best: {entry.best_mastered} of {entry.items_total} mastered";
        }
    }
}
