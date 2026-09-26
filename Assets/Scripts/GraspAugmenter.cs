using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SortQuest
{
    /// <summary>
    /// The robot "practices" each good human grasp for the selected gripper: it tries small variations
    /// (a few mm and degrees off) on a hidden copy of the item resting on a stand-in belt, using that gripper's
    /// own checks, and keeps only the variations that work. Kept variations are saved with source "augmented"
    /// and the gripper's id. For the suction cup, each pinch is first converted to a contact point on the surface.
    /// When a different gripper is selected, existing human grasps are practiced for it too (backfill).
    /// The work is spread over frames so the game never stutters.
    /// </summary>
    public class GraspAugmenter : MonoBehaviour
    {
        private class Job
        {
            public GraspRecord Original;
            public ItemType Type;
            public GripperProfile Profile;
            public bool Started;
            public bool HasBase;
            public GripperGrasp Base;
            public bool OriginalWorks;
            public int Tried;
            public readonly List<GraspRecord> Kept = new List<GraspRecord>();
        }

        private class Sandbox
        {
            public Transform Item;
            public Rigidbody Body;
        }

        [Header("References (found automatically if empty)")]
        [SerializeField] private GraspDataset dataset;
        [SerializeField] private TrashSpawner spawner;
        [SerializeField] private GripperCatalog catalog;

        [Header("Variations")]
        [SerializeField] private int variationsPerGrasp = 12;

        [Tooltip("Largest shift of the grasp center, in meters.")]
        [SerializeField] private float positionJitter = 0.01f;

        [Tooltip("Largest tilt of the gripper, in degrees.")]
        [SerializeField] private float angleJitter = 10f;

        [Tooltip("Grasp checks per frame, to keep the frame rate steady.")]
        [SerializeField] private int checksPerFrame = 4;

        [Tooltip("When switching grippers, practice up to this many recent human grasps per item it has no practice for.")]
        [SerializeField] private int backfillPerItem = 10;

        [Tooltip("Where the hidden item copies live, far out of sight.")]
        [SerializeField] private Vector3 sandboxOrigin = new Vector3(0f, -100f, 0f);

        [SerializeField] private bool logResults = true;

        /// <summary>Raised when a human grasp has been practiced: (original, variations tried, variations kept).</summary>
        public event Action<GraspRecord, int, int> Practiced;

        /// <summary>Human grasps still waiting to be practiced.</summary>
        public int PendingGrasps => jobs.Count;

        private readonly Queue<Job> jobs = new Queue<Job>();
        private readonly Dictionary<ItemType, Sandbox> sandboxes = new Dictionary<ItemType, Sandbox>();

        private void Awake()
        {
            if (dataset == null) dataset = FindAnyObjectByType<GraspDataset>();
            if (spawner == null) spawner = FindAnyObjectByType<TrashSpawner>();
            if (catalog == null) catalog = FindAnyObjectByType<GripperCatalog>();
        }

        private void OnEnable()
        {
            if (dataset != null) dataset.RecordAdded += HandleRecordAdded;
            if (catalog != null) catalog.Changed += Backfill;
        }

        private void OnDisable()
        {
            if (dataset != null) dataset.RecordAdded -= HandleRecordAdded;
            if (catalog != null) catalog.Changed -= Backfill;
        }

        private void Start()
        {
            Backfill(GripperCatalog.CurrentOrStandard(catalog));
        }

        private void HandleRecordAdded(GraspRecord record)
        {
            // Only practice new good human grasps (not our own output or the robot's attempts).
            if (record.source == GraspRecord.SourceHuman && record.IsGood && TryParseItemType(record.item_type, out ItemType type))
            {
                jobs.Enqueue(new Job { Original = record, Type = type, Profile = GripperCatalog.CurrentOrStandard(catalog) });
            }
        }

        /// <summary>
        /// Queues recent good human grasps for every item this gripper hasn't practiced yet,
        /// so a newly selected gripper learns from data people already gave.
        /// </summary>
        public void Backfill(GripperProfile profile)
        {
            if (dataset == null || profile == null)
            {
                return;
            }
            int queued = 0;
            foreach (ItemType type in (ItemType[])Enum.GetValues(typeof(ItemType)))
            {
                List<GraspRecord> good = dataset.GoodGrasps(type);
                if (good.Exists(r => r.source == GraspRecord.SourceAugmented && r.gripper == profile.id))
                {
                    continue;
                }
                List<GraspRecord> human = good.FindAll(r => r.source == GraspRecord.SourceHuman);
                for (int i = Mathf.Max(0, human.Count - backfillPerItem); i < human.Count; i++)
                {
                    jobs.Enqueue(new Job { Original = human[i], Type = type, Profile = profile });
                    queued++;
                }
            }
            if (queued > 0 && logResults)
            {
                Debug.Log($"[SortQuest] Robot will practice {queued} existing human grasps for the {profile.displayName} gripper.");
            }
        }

        private void Update()
        {
            int budget = checksPerFrame;
            while (budget > 0 && jobs.Count > 0)
            {
                Job job = jobs.Peek();
                Sandbox sandbox = GetSandbox(job.Type);
                if (sandbox == null)
                {
                    jobs.Dequeue();
                    continue;
                }

                budget--;
                if (!job.Started)
                {
                    job.Started = true;
                    job.HasBase = TryMakeBase(job, sandbox, out job.Base);
                    job.OriginalWorks = job.HasBase && Check(job.Profile, sandbox, job.Base, out _);
                    if (!job.HasBase)
                    {
                        job.Tried = variationsPerGrasp; // Nothing to vary; finish below.
                    }
                }
                else
                {
                    GripperGrasp candidate = Vary(job.Base);
                    if (FitToGripper(job.Profile, sandbox, ref candidate) &&
                        Check(job.Profile, sandbox, candidate, out float width))
                    {
                        candidate.Width = width;
                        job.Kept.Add(GraspRecord.CreateAugmented(job.Original, candidate, job.Profile.id));
                    }
                    job.Tried++;
                }

                if (job.Tried >= variationsPerGrasp)
                {
                    jobs.Dequeue();
                    Finish(job);
                }
            }
        }

        private void Finish(Job job)
        {
            foreach (GraspRecord record in job.Kept)
            {
                dataset.Add(record);
            }
            if (logResults)
            {
                string original = !job.HasBase
                    ? "couldn't be placed on the item"
                    : job.OriginalWorks ? "works" : "doesn't work";
                Debug.Log($"[SortQuest] Robot practiced a {TrashTypes.DisplayName(job.Type)} grasp for the " +
                          $"{job.Profile.displayName} gripper: {job.Kept.Count} of {job.Tried} variations worked " +
                          $"(the player's grasp, converted for this gripper, {original}).");
            }
            Practiced?.Invoke(job.Original, job.Tried, job.Kept.Count);
        }

        /// <summary>
        /// The starting grasp for practice, in the item's frame. Two-finger grippers use the human grasp as is;
        /// the suction cup moves it onto the item's surface along the hand's approach direction.
        /// </summary>
        private bool TryMakeBase(Job job, Sandbox sandbox, out GripperGrasp baseGrasp)
        {
            baseGrasp = job.Original.LocalGrasp;
            if (!job.Profile.IsSuction)
            {
                return true;
            }
            GripperGrasp world = HandGripperPose.ToWorld(baseGrasp, sandbox.Item);
            if (!SuctionShape.ProjectOntoSurface(world, sandbox.Body, out GripperGrasp contact))
            {
                return false;
            }
            contact.Width = job.Profile.suction.cupDiameter;
            baseGrasp = HandGripperPose.ToLocal(contact, sandbox.Item);
            return true;
        }

        /// <summary>For suction, slide a varied grasp along its approach until the cup touches the surface again.</summary>
        private bool FitToGripper(GripperProfile profile, Sandbox sandbox, ref GripperGrasp localGrasp)
        {
            if (!profile.IsSuction)
            {
                return true;
            }
            GripperGrasp world = HandGripperPose.ToWorld(localGrasp, sandbox.Item);
            if (!SuctionShape.SlideOntoSurface(world, sandbox.Body, out GripperGrasp contact))
            {
                return false;
            }
            localGrasp = HandGripperPose.ToLocal(contact, sandbox.Item);
            return true;
        }

        /// <summary>A small random shift and tilt of a grasp, in the item's frame.</summary>
        private GripperGrasp Vary(GripperGrasp original)
        {
            Quaternion tilt = Quaternion.AngleAxis(Random.Range(-angleJitter, angleJitter), Random.onUnitSphere);
            return new GripperGrasp
            {
                Position = original.Position + Random.insideUnitSphere * positionJitter,
                Rotation = tilt * original.Rotation,
                Width = original.Width
            };
        }

        /// <summary>The gripper's own rule: it gets in without hitting anything, then grasps (fingers touch or cup seals).</summary>
        private bool Check(GripperProfile profile, Sandbox sandbox, GripperGrasp localGrasp, out float width)
        {
            width = 0f;
            GripperGrasp world = HandGripperPose.ToWorld(localGrasp, sandbox.Item);
            if (profile.Model.PathBlocked(world, profile.approachDistance, null))
            {
                return false;
            }
            return profile.Model.TryGrasp(world, sandbox.Body, out width);
        }

        /// <summary>
        /// A hidden copy of the item (colliders only) resting on a stand-in belt, like a fresh item on the conveyor.
        /// Built while inactive, so the item's own scripts and grab interaction never run on the copy.
        /// </summary>
        private Sandbox GetSandbox(ItemType type)
        {
            if (sandboxes.TryGetValue(type, out Sandbox existing))
            {
                return existing;
            }
            TrashItem prefab = spawner != null ? spawner.GetPrefab(type) : null;
            if (prefab == null)
            {
                sandboxes[type] = null;
                return null;
            }

            var holder = new GameObject($"AugmenterSandbox_{type}");
            holder.SetActive(false);
            holder.transform.SetParent(transform, false);
            holder.transform.position = sandboxOrigin + Vector3.right * (2f * sandboxes.Count);

            GameObject copy = Instantiate(prefab.gameObject, holder.transform);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = prefab.transform.rotation;
            StripToColliders(copy);
            Rigidbody copyBody = copy.GetComponent<Rigidbody>();
            copyBody.isKinematic = true;
            copyBody.useGravity = false;
            foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = false;
            }

            holder.SetActive(true);
            Physics.SyncTransforms();

            float bottom = float.MaxValue;
            foreach (Collider col in copy.GetComponentsInChildren<Collider>())
            {
                bottom = Mathf.Min(bottom, col.bounds.min.y);
            }
            var belt = new GameObject("BeltStandIn");
            belt.transform.SetParent(holder.transform, false);
            belt.transform.position = new Vector3(holder.transform.position.x, bottom - 0.05f, holder.transform.position.z);
            belt.AddComponent<BoxCollider>().size = new Vector3(1f, 0.1f, 1f);
            Physics.SyncTransforms();

            var sandbox = new Sandbox { Item = copy.transform, Body = copyBody };
            sandboxes[type] = sandbox;
            return sandbox;
        }

        private static void StripToColliders(GameObject copy)
        {
            for (int i = copy.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = copy.transform.GetChild(i);
                if (child.GetComponentInChildren<Collider>(true) == null)
                {
                    DestroyImmediate(child.gameObject);
                }
            }
            MonoBehaviour[] behaviours = copy.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = behaviours.Length - 1; i >= 0; i--)
            {
                DestroyImmediate(behaviours[i]);
            }
        }

        private static bool TryParseItemType(string id, out ItemType type)
        {
            foreach (ItemType candidate in (ItemType[])Enum.GetValues(typeof(ItemType)))
            {
                if (TrashTypes.ItemId(candidate) == id)
                {
                    type = candidate;
                    return true;
                }
            }
            type = default;
            return false;
        }
    }
}
