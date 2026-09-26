using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SortQuest
{
    /// <summary>
    /// Spawns random trash prefabs at the start of the conveyor on a timer.
    /// </summary>
    public class TrashSpawner : MonoBehaviour
    {
        [SerializeField] private ConveyorBelt belt;
        [SerializeField] private TrashItem[] itemPrefabs;

        [Tooltip("Seconds between spawns.")]
        [SerializeField] private float spawnInterval = 3f;

        [Tooltip("No new items spawn while this many are alive.")]
        [SerializeField] private int maxItems = 8;

        [SerializeField] private bool spawnOnStart = true;
        [SerializeField] private bool randomYaw = true;

        [Tooltip("Optional parent for spawned items, to keep the Hierarchy tidy.")]
        [SerializeField] private Transform itemParent;

        [Tooltip("Found automatically if left empty.")]
        [SerializeField] private ScoreBoard scoreBoard;

        public event Action<TrashItem> ItemSpawned;

        public bool Spawning { get; set; }

        /// <summary>When set, only this item type spawns.</summary>
        public ItemType? OnlyType { get; set; }
        public IReadOnlyList<TrashItem> ActiveItems => activeItems;

        private readonly List<TrashItem> activeItems = new List<TrashItem>();
        private float timer;

        private void Awake()
        {
            Spawning = spawnOnStart;
            if (scoreBoard == null)
            {
                scoreBoard = FindAnyObjectByType<ScoreBoard>();
            }
        }

        private void Update()
        {
            if (!Spawning || belt == null || itemPrefabs == null || itemPrefabs.Length == 0)
            {
                return;
            }

            timer += Time.deltaTime;
            if (timer < spawnInterval)
            {
                return;
            }
            timer = 0f;

            activeItems.RemoveAll(item => item == null);
            if (activeItems.Count < maxItems)
            {
                SpawnItem();
            }
        }

        public TrashItem SpawnItem()
        {
            TrashItem prefab = PickPrefab();
            if (prefab == null)
            {
                return null;
            }

            Quaternion rotation = prefab.transform.rotation;
            if (randomYaw)
            {
                rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * rotation;
            }

            TrashItem item = Instantiate(prefab, belt.StartPosition, rotation, itemParent);

            // Lift the item so its bottom rests on the start point instead of its center.
            float lift = belt.StartPosition.y - GetBottomY(item);
            Vector3 position = item.transform.position + Vector3.up * lift;
            item.transform.position = position;
            item.Body.position = position;

            item.PlaceOnBelt(belt);
            item.Missed += HandleMissed;
            activeItems.Add(item);
            ItemSpawned?.Invoke(item);
            return item;
        }

        /// <summary>Removes spawned items, except any a person is holding right now.</summary>
        public void ClearItems()
        {
            activeItems.RemoveAll(item => item == null);
            for (int i = activeItems.Count - 1; i >= 0; i--)
            {
                TrashItem item = activeItems[i];
                if (item.State == TrashItemState.Held && !item.LastHeldByRobot)
                {
                    continue;
                }
                Destroy(item.gameObject);
                activeItems.RemoveAt(i);
            }
        }

        /// <summary>The first prefab of this item type, or null.</summary>
        public TrashItem GetPrefab(ItemType type)
        {
            foreach (TrashItem prefab in itemPrefabs)
            {
                if (prefab != null && prefab.ItemType == type)
                {
                    return prefab;
                }
            }
            return null;
        }

        private TrashItem PickPrefab()
        {
            if (!OnlyType.HasValue)
            {
                return itemPrefabs[Random.Range(0, itemPrefabs.Length)];
            }
            var matches = new List<TrashItem>();
            foreach (TrashItem prefab in itemPrefabs)
            {
                if (prefab != null && prefab.ItemType == OnlyType.Value)
                {
                    matches.Add(prefab);
                }
            }
            return matches.Count > 0 ? matches[Random.Range(0, matches.Count)] : null;
        }

        private void HandleMissed(TrashItem item)
        {
            if (scoreBoard != null && !item.LastHeldByRobot)
            {
                scoreBoard.RegisterMiss(item);
            }
        }

        private static float GetBottomY(TrashItem item)
        {
            float bottom = item.transform.position.y;
            bool found = false;
            foreach (Collider col in item.GetComponentsInChildren<Collider>())
            {
                if (col.isTrigger)
                {
                    continue;
                }
                bottom = found ? Mathf.Min(bottom, col.bounds.min.y) : col.bounds.min.y;
                found = true;
            }
            return bottom;
        }
    }
}
