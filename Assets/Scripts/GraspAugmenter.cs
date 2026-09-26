using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SortQuest
{
    /// <summary>
    /// The robot "practices" each good human grasp: it tries small variations (a few mm and degrees off)
    /// on a hidden copy of the item resting on a stand-in belt, using the robot's own finger checks,
    /// and keeps only the variations that work. Kept variations are saved with source "augmented"
    /// and count as learning data. The work is spread over frames so the game never stutters.
    /// </summary>
    public class GraspAugmenter : MonoBehaviour
    {
        private class Job
        {
            public GraspRecord Original;
            public ItemType Type;
            public bool Started;
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

        [Tooltip("Its gripper shape and approach distance are used for the checks.")]
        [SerializeField] private RobotGripper robot;

        [Header("Variations")]
        [SerializeField] private int variationsPerGrasp = 12;

        [Tooltip("Largest shift of the grasp center, in meters.")]
        [SerializeField] private float positionJitter = 0.01f;

        [Tooltip("Largest tilt of the gripper, in degrees.")]
        [SerializeField] private float angleJitter = 10f;

        [Tooltip("Grasp checks per frame, to keep the frame rate steady.")]
        [SerializeField] private int checksPerFrame = 4;

        [Tooltip("Where the hidden item copies live, far out of sight.")]
        [SerializeField] private Vector3 sandboxOrigin = new Vector3(0f, -100f, 0f);

        [SerializeField] private bool logResults = true;

        /// <summary>Raised when a human grasp has been practiced: (original, variations tried, variations kept).</summary>
        public event Action<GraspRecord, int, int> Practiced;

        private readonly Queue<Job> jobs = new Queue<Job>();
        private readonly Dictionary<ItemType, Sandbox> sandboxes = new Dictionary<ItemType, Sandbox>();
        private GripperShape fallbackShape;

        private GripperShape Shape => robot != null ? robot.Shape : fallbackShape;
        private float ApproachDistance => robot != null ? robot.ApproachDistance : 0.12f;

        private void Awake()
        {
            if (dataset == null) dataset = FindAnyObjectByType<GraspDataset>();
            if (spawner == null) spawner = FindAnyObjectByType<TrashSpawner>();
            if (robot == null) robot = FindAnyObjectByType<RobotGripper>();
            fallbackShape = new GripperShape();
        }

        private void OnEnable()
        {
            if (dataset != null) dataset.RecordAdded += HandleRecordAdded;
        }

        private void OnDisable()
        {
            if (dataset != null) dataset.RecordAdded -= HandleRecordAdded;
        }

        private void HandleRecordAdded(GraspRecord record)
        {
            // Only practice new good human grasps (not our own output or the robot's attempts).
            if (record.source != GraspRecord.SourceHuman || !record.IsGood)
            {
                return;
            }
            if (TryParseItemType(record.item_type, out ItemType type))
            {
                jobs.Enqueue(new Job { Original = record, Type = type });
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
                    job.OriginalWorks = Check(sandbox, job.Original.LocalGrasp, out _);
                    continue;
                }

                GripperGrasp candidate = Vary(job.Original.LocalGrasp);
                if (Check(sandbox, candidate, out float closedWidth))
                {
                    candidate.Width = closedWidth;
                    job.Kept.Add(GraspRecord.CreateAugmented(job.Original, candidate));
                }
                job.Tried++;
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
                Debug.Log($"[SortQuest] Robot practiced a {TrashTypes.DisplayName(job.Type)} grasp: " +
                          $"{job.Kept.Count} of {job.Tried} variations worked " +
                          $"(the player's exact grasp {(job.OriginalWorks ? "works" : "doesn't work")} for the robot).");
            }
            Practiced?.Invoke(job.Original, job.Tried, job.Kept.Count);
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

        /// <summary>The robot's rule: the open fingers get in without hitting anything, then both touch the item.</summary>
        private bool Check(Sandbox sandbox, GripperGrasp localGrasp, out float closedWidth)
        {
            closedWidth = 0f;
            GripperGrasp world = HandGripperPose.ToWorld(localGrasp, sandbox.Item);
            if (Shape.PathBlocked(world, ApproachDistance, null))
            {
                return false;
            }
            return Shape.TryClose(world, sandbox.Body, out closedWidth);
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
