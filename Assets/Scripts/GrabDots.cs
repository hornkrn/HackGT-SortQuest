using System.Collections;
using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// Shows verified contact points for the selected gripper, from successful demonstrations and practice.
    /// Parallel grasps show two opposing pad contacts; suction shows one seal point. Raw tracked fingertips
    /// are not contact points and are never drawn as green targets. Source records remain unchanged.
    /// </summary>
    public class GrabDots : MonoBehaviour
    {
        [Tooltip("Found automatically if left empty.")]
        [SerializeField] private TrashSpawner spawner;

        [Tooltip("Found automatically if left empty.")]
        [SerializeField] private GraspDataset dataset;

        [Tooltip("Found automatically if left empty.")]
        [SerializeField] private GripperCatalog catalog;

        [Tooltip("See-through material (Sprites/Default). Created if left empty.")]
        [SerializeField] private Material material;

        [Tooltip("Check up to this many recent examples per item, one per frame. Only contacts with gripper clearance are shown.")]
        [SerializeField] private int maxGrasps = 20;

        [SerializeField] private float dotSize = 0.008f;
        [SerializeField] private Color color = new Color(0.2f, 1f, 0.4f, 0.9f);

        private void Awake()
        {
            if (spawner == null) spawner = FindAnyObjectByType<TrashSpawner>();
            if (dataset == null) dataset = FindAnyObjectByType<GraspDataset>();
            if (catalog == null) catalog = FindAnyObjectByType<GripperCatalog>();
            if (material == null) material = VizUtil.FallbackMaterial();
        }

        private void OnEnable()
        {
            if (spawner != null) spawner.ItemSpawned += AddDots;
            if (catalog != null) catalog.Changed += RefreshDots;
        }

        private void OnDisable()
        {
            if (spawner != null) spawner.ItemSpawned -= AddDots;
            if (catalog != null) catalog.Changed -= RefreshDots;
            StopAllCoroutines();
        }

        private void RefreshDots(GripperProfile profile)
        {
            StopAllCoroutines();
            foreach (TrashItem item in FindObjectsByType<TrashItem>(FindObjectsSortMode.None)) AddDots(item);
        }

        private void AddDots(TrashItem item)
        {
            Transform previous = item.transform.Find("GrabDots");
            if (previous != null) Destroy(previous.gameObject);
            if (dataset != null) StartCoroutine(DrawContacts(item));
        }

        private IEnumerator DrawContacts(TrashItem item)
        {
            GripperProfile profile = GripperCatalog.CurrentOrStandard(catalog);
            var good = dataset.GoodGrasps(item.ItemType).FindAll(record =>
                (record.source == GraspRecord.SourceHuman && !profile.IsSuction) ||
                (record.source == GraspRecord.SourceAugmented && record.gripper == profile.id &&
                 record.checker_version == GripperCollision.Version && record.feasible == true));
            if (good.Count == 0) yield break;

            // Grasps are stored in meters in the item's frame, ignoring its scale.
            // This holder undoes the item's scale so dots can be placed in meters and stay round.
            Transform holder = new GameObject("GrabDots").transform;
            holder.SetParent(item.transform, false);
            Vector3 scale = item.transform.lossyScale;
            holder.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);

            // Allow freshly spawned colliders to enter the physics world before querying them.
            yield return null;
            for (int i = good.Count - 1; i >= Mathf.Max(0, good.Count - maxGrasps); i--)
            {
                if (item == null || holder == null) yield break;
                GripperGrasp world = HandGripperPose.ToWorld(good[i].LocalGrasp, item.transform);
                if (GripperCollision.Feasible(profile, world, item.Body, null, out _))
                {
                    if (profile.IsSuction && SuctionShape.SlideOntoSurface(world, item.Body, out var contact))
                        AddDot(holder, ToLocalPoint(item, contact.Position));
                    else if (!profile.IsSuction && profile.parallel.TryContacts(world, item.Body,
                        out _, out _, out Vector3 left, out Vector3 right))
                    {
                        AddDot(holder, ToLocalPoint(item, left));
                        AddDot(holder, ToLocalPoint(item, right));
                    }
                }
                yield return null;
            }
        }

        private static Vector3 ToLocalPoint(TrashItem item, Vector3 world) =>
            Quaternion.Inverse(item.transform.rotation) * (world - item.transform.position);

        private void AddDot(Transform holder, Vector3 localPosition)
        {
            Transform dot = VizUtil.CreateShape("Dot", holder, VizUtil.SphereMesh, material, color).transform;
            dot.localPosition = localPosition;
            dot.localScale = Vector3.one * dotSize;
        }
    }
}
