using System.Collections.Generic;
using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// Puts small dots on each new item where people's fingers were in successful grasps of that item type
    /// (human grasps only, not the robot's practiced variations),
    /// so players can see the crowd's grasps. Each grasp shows its two finger positions
    /// (one dot if the fingers were nearly together).
    /// </summary>
    public class GrabDots : MonoBehaviour
    {
        [Tooltip("Found automatically if left empty.")]
        [SerializeField] private TrashSpawner spawner;

        [Tooltip("Found automatically if left empty.")]
        [SerializeField] private GraspDataset dataset;

        [Tooltip("See-through material (Sprites/Default). Created if left empty.")]
        [SerializeField] private Material material;

        [Tooltip("Show the most recent this many good grasps per item.")]
        [SerializeField] private int maxGrasps = 20;

        [SerializeField] private float dotSize = 0.008f;
        [SerializeField] private Color color = new Color(0.2f, 1f, 0.4f, 0.9f);

        private void Awake()
        {
            if (spawner == null) spawner = FindAnyObjectByType<TrashSpawner>();
            if (dataset == null) dataset = FindAnyObjectByType<GraspDataset>();
            if (material == null) material = VizUtil.FallbackMaterial();
        }

        private void OnEnable()
        {
            if (spawner != null) spawner.ItemSpawned += AddDots;
        }

        private void OnDisable()
        {
            if (spawner != null) spawner.ItemSpawned -= AddDots;
        }

        private void AddDots(TrashItem item)
        {
            if (dataset == null)
            {
                return;
            }
            List<GraspRecord> good = dataset.GoodGrasps(item.ItemType)
                .FindAll(record => record.source == GraspRecord.SourceHuman);
            if (good.Count == 0)
            {
                return;
            }

            // Grasps are stored in meters in the item's frame, ignoring its scale.
            // This holder undoes the item's scale so dots can be placed in meters and stay round.
            Transform holder = new GameObject("GrabDots").transform;
            holder.SetParent(item.transform, false);
            Vector3 scale = item.transform.lossyScale;
            holder.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);

            for (int i = Mathf.Max(0, good.Count - maxGrasps); i < good.Count; i++)
            {
                GripperGrasp grasp = good[i].LocalGrasp;
                if (grasp.Width < 0.01f)
                {
                    AddDot(holder, grasp.Position);
                    continue;
                }
                Vector3 half = grasp.Rotation * Vector3.right * (grasp.Width * 0.5f);
                AddDot(holder, grasp.Position - half);
                AddDot(holder, grasp.Position + half);
            }
        }

        private void AddDot(Transform holder, Vector3 localPosition)
        {
            Transform dot = VizUtil.CreateShape("Dot", holder, VizUtil.SphereMesh, material, color).transform;
            dot.localPosition = localPosition;
            dot.localScale = Vector3.one * dotSize;
        }
    }
}
