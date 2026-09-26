using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// A floating two-finger gripper (no arm) that sorts items near the end of the belt.
    /// For each item it asks the GraspPolicy for a grasp, moves there while following the belt,
    /// and closes. The grasp succeeds only if both fingers touch the item; the item is then held
    /// with a FixedJoint and dropped into its correct bin. No friction-based grasping.
    ///
    /// Gripper frame: the root sits at the grasp point, +Z is the approach direction,
    /// and the fingers close along X.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RobotGripper : MonoBehaviour
    {
        public struct Attempt
        {
            public TrashItem Item;
            public GraspChoice Choice;
            public bool Success;
        }

        [Header("References (found automatically if empty)")]
        [SerializeField] private GraspPolicy policy;
        [SerializeField] private GraspDataset dataset;
        [SerializeField] private ConveyorBelt belt;
        [SerializeField] private TrashSpawner spawner;

        [Header("Visuals")]
        [SerializeField] private Transform fingerLeft;
        [SerializeField] private Transform fingerRight;
        [SerializeField] private Transform palm;
        [Tooltip("Optional text showing the robot's accuracy and last attempt.")]
        [SerializeField] private TMP_Text statusText;

        [Header("Behavior")]
        [SerializeField] private bool runOnStart = true;

        [Tooltip("Only items past this point on the belt are picked (0 = start, 1 = end).")]
        [SerializeField, Range(0f, 1f)] private float pickZoneStart = 0.6f;

        [SerializeField] private float moveSpeed = 0.8f;
        [SerializeField] private float turnSpeed = 360f;

        [Tooltip("Distance before the grasp point where the gripper lines up before moving in.")]
        [SerializeField] private float approachDistance = 0.12f;

        [Tooltip("Finger closing speed, m/s per finger.")]
        [SerializeField] private float closeSpeed = 0.15f;

        [SerializeField] private float carryHeight = 1.25f;
        [SerializeField] private float releaseHeight = 0.85f;

        [Tooltip("Give up on a move after this many seconds.")]
        [SerializeField] private float stepTimeout = 4f;

        [Header("Gripper shape (meters)")]
        [SerializeField] private float maxOpening = 0.1f;
        [SerializeField] private float fingerLength = 0.06f;
        [SerializeField] private float fingerWidth = 0.025f;
        [SerializeField] private float fingerThickness = 0.012f;

        [Tooltip("How far the fingertips reach past the grasp point.")]
        [SerializeField] private float fingertipPastGrasp = 0.01f;

        [SerializeField] private float palmThickness = 0.02f;

        [Tooltip("A finger closer than this to the item counts as touching it.")]
        [SerializeField] private float touchSkin = 0.002f;

        public event Action<Attempt> AttemptFinished;

        public bool Active { get; set; }

        public float PickZoneStart
        {
            get => pickZoneStart;
            set => pickZoneStart = Mathf.Clamp01(value);
        }
        public int Attempts { get; private set; }
        public int Successes { get; private set; }
        public float Accuracy => Attempts == 0 ? 0f : (float)Successes / Attempts;

        private Rigidbody body;
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private float leftOffset;
        private float rightOffset;
        private SortingBin[] bins;
        private string lastResult = "Waiting for trash";
        private readonly HashSet<TrashItem> attempted = new HashSet<TrashItem>();
        private readonly Collider[] overlapBuffer = new Collider[16];
        private readonly WaitForFixedUpdate waitForFixedUpdate = new WaitForFixedUpdate();
        private Coroutine runRoutine;
        private TrashItem heldItem;
        private FixedJoint heldJoint;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            if (policy == null) policy = FindAnyObjectByType<GraspPolicy>();
            if (dataset == null) dataset = FindAnyObjectByType<GraspDataset>();
            if (belt == null) belt = FindAnyObjectByType<ConveyorBelt>();
            if (spawner == null) spawner = FindAnyObjectByType<TrashSpawner>();
            if (policy == null || belt == null || spawner == null)
            {
                Debug.LogError("[SortQuest] RobotGripper needs a GraspPolicy, ConveyorBelt, and TrashSpawner.", this);
                enabled = false;
                return;
            }

            homePosition = transform.position;
            homeRotation = transform.rotation;
            OpenFingers();
            Active = runOnStart;
        }

        private void Start()
        {
            bins = FindObjectsByType<SortingBin>(FindObjectsSortMode.None);
            UpdateStatus();
            if (runRoutine == null)
            {
                runRoutine = StartCoroutine(Run());
            }
        }

        /// <summary>Stops whatever the robot is doing, lets go of any item, and starts fresh.</summary>
        public void ResetRobot()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }
            StopAllCoroutines();
            if (heldJoint != null)
            {
                Destroy(heldJoint);
            }
            if (heldItem != null)
            {
                heldItem.EndRobotHold();
            }
            heldJoint = null;
            heldItem = null;
            OpenFingers();
            runRoutine = StartCoroutine(Run());
        }

        /// <summary>Clears the accuracy count, for example at the start of a new game.</summary>
        public void ResetStats()
        {
            Attempts = 0;
            Successes = 0;
            lastResult = "Waiting for trash";
            UpdateStatus();
        }

        private void Update()
        {
            ApplyFingerLayout();
        }

        private IEnumerator Run()
        {
            while (true)
            {
                TrashItem target = Active ? FindTarget() : null;
                if (target == null)
                {
                    MoveToward(homePosition, homeRotation);
                    yield return waitForFixedUpdate;
                    continue;
                }
                yield return StartCoroutine(TrySort(target));
            }
        }

        /// <summary>The item in the pick zone with the most belt left, so there is time to reach it.</summary>
        private TrashItem FindTarget()
        {
            attempted.RemoveWhere(item => item == null);
            TrashItem best = null;
            float bestProgress = float.MaxValue;
            foreach (TrashItem item in spawner.ActiveItems)
            {
                if (item == null || item.State != TrashItemState.OnBelt || attempted.Contains(item))
                {
                    continue;
                }
                float progress = belt.Progress(item.Body.position);
                if (progress >= pickZoneStart && progress < bestProgress)
                {
                    best = item;
                    bestProgress = progress;
                }
            }
            return best;
        }

        private IEnumerator TrySort(TrashItem item)
        {
            attempted.Add(item);
            GraspChoice choice = policy.ChooseGrasp(item);
            OpenFingers();

            // 1. Line up in front of the grasp, then 2. move in along the approach direction.
            //    Both follow the item as it rides the belt.
            for (int phase = 0; phase < 2; phase++)
            {
                float start = Time.time;
                while (true)
                {
                    if (!IsOnBelt(item) || Time.time - start > stepTimeout)
                    {
                        yield break; // A person took it or it fell off; not counted as an attempt.
                    }
                    GripperGrasp target = WorldGrasp(choice, item);
                    Vector3 position = phase == 0 ? target.Position - target.Approach * approachDistance : target.Position;
                    if (MoveToward(position, target.Rotation))
                    {
                        break;
                    }
                    yield return waitForFixedUpdate;
                }
            }

            // 3. The open fingers must not run into the item or anything else on the way in. Then close.
            GripperGrasp grasp = WorldGrasp(choice, item);
            bool blocked = PathBlocked(grasp);
            bool leftTouch = false;
            bool rightTouch = false;
            if (!blocked)
            {
                while (!(leftTouch && rightTouch) && (leftOffset > 0f || rightOffset > 0f))
                {
                    if (!IsOnBelt(item))
                    {
                        OpenFingers();
                        yield break;
                    }
                    grasp = WorldGrasp(choice, item);
                    MoveToward(grasp.Position, grasp.Rotation);
                    leftTouch = leftTouch || FingerTouches(grasp, true, leftOffset, item);
                    rightTouch = rightTouch || FingerTouches(grasp, false, rightOffset, item);
                    float step = closeSpeed * Time.fixedDeltaTime;
                    if (!leftTouch) leftOffset = Mathf.Max(0f, leftOffset - step);
                    if (!rightTouch) rightOffset = Mathf.Max(0f, rightOffset - step);
                    yield return waitForFixedUpdate;
                }
            }

            bool success = !blocked && leftTouch && rightTouch;
            Record(item, choice, success, blocked);
            if (!success)
            {
                OpenFingers();
                yield return StartCoroutine(MoveTo(body.position - grasp.Approach * approachDistance, body.rotation));
                yield break;
            }

            // 4. Hold the item with a joint and carry it to its bin.
            item.BeginRobotHold();
            FixedJoint joint = item.gameObject.AddComponent<FixedJoint>();
            joint.connectedBody = body;
            heldItem = item;
            heldJoint = joint;

            yield return StartCoroutine(MoveTo(new Vector3(body.position.x, carryHeight, body.position.z), body.rotation));
            SortingBin bin = FindBin(item.CorrectBin);
            if (bin != null)
            {
                Vector3 above = bin.transform.position;
                yield return StartCoroutine(MoveTo(new Vector3(above.x, carryHeight, above.z), body.rotation));
                yield return StartCoroutine(MoveTo(new Vector3(above.x, releaseHeight, above.z), body.rotation));
            }

            // 5. Let go over the bin.
            if (joint != null)
            {
                Destroy(joint);
            }
            if (item != null)
            {
                item.EndRobotHold();
            }
            heldItem = null;
            heldJoint = null;
            OpenFingers();
            yield return new WaitForSeconds(0.3f);
        }

        private void Record(TrashItem item, GraspChoice choice, bool success, bool blocked)
        {
            Attempts++;
            if (success)
            {
                Successes++;
            }

            string how = choice.FromData
                ? $"learned grasp ({choice.Support} of {choice.GoodCount} agree, {choice.Confidence * 100f:F0}% sure)"
                : "no data, random guess";
            string result = success ? "success" : blocked ? "missed (fingers hit something)" : "missed (fingers didn't both touch)";
            lastResult = $"{TrashTypes.DisplayName(item.ItemType)}: {how}, {result}";
            Debug.Log($"[SortQuest] Robot {lastResult}. Accuracy {Successes}/{Attempts}");
            UpdateStatus();

            if (dataset != null)
            {
                GripperGrasp actual = choice.LocalGrasp;
                actual.Width = success ? leftOffset + rightOffset : choice.LocalGrasp.Width;
                GraspRecord record = GraspRecord.Create(dataset, GraspRecord.SourceRobot, item, actual, "gripper");
                record.outcome.bin = success ? TrashTypes.BinId(item.CorrectBin) : GraspRecord.NoBin;
                record.outcome.correct = success;
                dataset.Add(record);
            }

            AttemptFinished?.Invoke(new Attempt { Item = item, Choice = choice, Success = success });
        }

        private void UpdateStatus()
        {
            if (statusText == null)
            {
                return;
            }
            string accuracy = Attempts == 0 ? "no attempts yet" : $"{Successes} of {Attempts} grasps ({Accuracy * 100f:F0}%)";
            statusText.text = $"ROBOT\n<size=60%>{accuracy}\n{lastResult}</size>";
        }

        // ---------- Motion ----------

        /// <summary>Moves one physics step toward a pose. Returns true once the pose is reached.</summary>
        private bool MoveToward(Vector3 position, Quaternion rotation)
        {
            float dt = Time.fixedDeltaTime;
            Vector3 nextPosition = Vector3.MoveTowards(body.position, position, moveSpeed * dt);
            Quaternion nextRotation = Quaternion.RotateTowards(body.rotation, rotation, turnSpeed * dt);
            body.MovePosition(nextPosition);
            body.MoveRotation(nextRotation);
            return (nextPosition - position).sqrMagnitude < 1e-6f && Quaternion.Angle(nextRotation, rotation) < 0.5f;
        }

        private IEnumerator MoveTo(Vector3 position, Quaternion rotation)
        {
            float start = Time.time;
            while (!MoveToward(position, rotation) && Time.time - start < stepTimeout)
            {
                yield return waitForFixedUpdate;
            }
            yield return waitForFixedUpdate;
        }

        private GripperGrasp WorldGrasp(GraspChoice choice, TrashItem item)
        {
            return HandGripperPose.ToWorld(choice.LocalGrasp, item.Body.position, item.Body.rotation);
        }

        private static bool IsOnBelt(TrashItem item)
        {
            return item != null && item.State == TrashItemState.OnBelt;
        }

        private SortingBin FindBin(BinType type)
        {
            foreach (SortingBin bin in bins)
            {
                if (bin != null && bin.BinType == type)
                {
                    return bin;
                }
            }
            return null;
        }

        // ---------- Fingers ----------

        private void OpenFingers()
        {
            leftOffset = maxOpening * 0.5f;
            rightOffset = maxOpening * 0.5f;
        }

        /// <summary>World box of one finger for a gripper at the given pose. Offset = grasp center to the finger's inner face.</summary>
        private void FingerBox(GripperGrasp pose, bool left, float offset, out Vector3 center, out Vector3 halfExtents)
        {
            float side = left ? -1f : 1f;
            var local = new Vector3(side * (offset + fingerThickness * 0.5f), 0f, fingertipPastGrasp - fingerLength * 0.5f);
            center = pose.Position + pose.Rotation * local;
            halfExtents = new Vector3(fingerThickness, fingerWidth, fingerLength) * 0.5f;
        }

        private bool FingerTouches(GripperGrasp pose, bool left, float offset, TrashItem item)
        {
            FingerBox(pose, left, offset, out Vector3 center, out Vector3 half);
            int count = Physics.OverlapBoxNonAlloc(center, half + Vector3.one * touchSkin, overlapBuffer,
                pose.Rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (overlapBuffer[i].attachedRigidbody == item.Body)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>True if the open fingers would pass through anything solid while moving in to the grasp.</summary>
        private bool PathBlocked(GripperGrasp grasp)
        {
            const int samples = 4;
            for (int i = 0; i <= samples; i++)
            {
                GripperGrasp pose = grasp;
                pose.Position = grasp.Position - grasp.Approach * (approachDistance * i / samples);
                if (FingerBlocked(pose, true) || FingerBlocked(pose, false))
                {
                    return true;
                }
            }
            return false;
        }

        private bool FingerBlocked(GripperGrasp pose, bool left)
        {
            FingerBox(pose, left, maxOpening * 0.5f, out Vector3 center, out Vector3 half);
            Vector3 shrunk = Vector3.Max(half - Vector3.one * touchSkin, Vector3.one * 0.001f);
            int count = Physics.OverlapBoxNonAlloc(center, shrunk, overlapBuffer, pose.Rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (overlapBuffer[i].attachedRigidbody != body)
                {
                    return true;
                }
            }
            return false;
        }

        private void ApplyFingerLayout()
        {
            float fingerZ = fingertipPastGrasp - fingerLength * 0.5f;
            var fingerScale = new Vector3(fingerThickness, fingerWidth, fingerLength);
            if (fingerLeft != null)
            {
                fingerLeft.localPosition = new Vector3(-(leftOffset + fingerThickness * 0.5f), 0f, fingerZ);
                fingerLeft.localScale = fingerScale;
            }
            if (fingerRight != null)
            {
                fingerRight.localPosition = new Vector3(rightOffset + fingerThickness * 0.5f, 0f, fingerZ);
                fingerRight.localScale = fingerScale;
            }
            if (palm != null)
            {
                palm.localPosition = new Vector3(0f, 0f, fingerZ - fingerLength * 0.5f - palmThickness * 0.5f);
                palm.localScale = new Vector3(maxOpening + 2f * fingerThickness, fingerWidth, palmThickness);
            }
        }
    }
}
