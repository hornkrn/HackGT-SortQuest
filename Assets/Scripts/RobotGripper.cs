using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// The robot's gripper, using whichever gripper is selected in the GripperCatalog. For each item it asks the
    /// GraspPolicy for a grasp, moves there while following the belt, and grasps. A two-finger grasp succeeds only
    /// if both fingers touch the item; a suction grasp succeeds only if the cup seals on a flat enough surface.
    /// The item is then held with a FixedJoint and dropped into its correct bin. No friction-based grasping.
    ///
    /// Gripper frame: the root sits at the grasp point, +Z is the approach direction,
    /// and two-finger grippers close along X.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RobotGripper : MonoBehaviour
    {
        public struct Attempt
        {
            public TrashItem Item;
            public GraspChoice Choice;
            public bool Success;

            /// <summary>
            /// False for attempts that ended for reasons outside the robot's control (a person took the item, it
            /// rode off the belt, or the round ended). Those are saved as data but not scored.
            /// </summary>
            public bool Counts;

            public string FailureReason;
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

        [Tooltip("Optional detailed gripper model. Found on this object if left empty.")]
        [SerializeField] private GripperVisual visual;

        [Tooltip("Optional arm. Grasps it can't reach count as misses. Found automatically if left empty.")]
        [SerializeField] private RobotArmDisplay arm;

        [Tooltip("Optional overhead camera that saves what the robot saw for each attempt. Found automatically if left empty.")]
        [SerializeField] private RobotCamera robotCamera;

        [Tooltip("Selected gripper. Found automatically if left empty; without one, the shape below is used.")]
        [SerializeField] private GripperCatalog catalog;

        [Header("Behavior")]
        [SerializeField] private bool runOnStart = true;

        [Tooltip("Only items past this point on the belt are picked (0 = start, 1 = end).")]
        [SerializeField, Range(0f, 1f)] private float pickZoneStart = 0.6f;

        [SerializeField] private float moveSpeed = 0.8f;
        [SerializeField] private float turnSpeed = 360f;

        [Tooltip("Used only without a GripperCatalog: distance before the grasp point where the gripper lines up.")]
        [SerializeField] private float approachDistance = 0.12f;

        [Tooltip("Finger closing speed, m/s per finger.")]
        [SerializeField] private float closeSpeed = 0.15f;

        [SerializeField] private float carryHeight = 1.25f;
        [SerializeField] private float releaseHeight = 0.85f;

        [Tooltip("If the gripper doesn't fit at the release height, it tries this much higher (twice) before failing.")]
        [SerializeField] private float releaseRaiseStep = 0.15f;

        [Tooltip("Give up on a move after this many seconds.")]
        [SerializeField] private float stepTimeout = 4f;

        [Header("Gripper shape (used only without a GripperCatalog)")]
        [SerializeField] private GripperShape shape = new GripperShape();

        public event Action<Attempt> AttemptFinished;

        /// <summary>Raised when the robot picks a grasp for an item, before it starts moving.</summary>
        public event Action<TrashItem, GraspChoice> GraspPlanned;

        public bool Active { get; set; }

        public float PickZoneStart
        {
            get => pickZoneStart;
            set => pickZoneStart = Mathf.Clamp01(value);
        }
        public int Attempts { get; private set; }
        public int Successes { get; private set; }
        public float Accuracy => Attempts == 0 ? 0f : (float)Successes / Attempts;
        /// <summary>The gripper the robot is using now.</summary>
        public GripperProfile Profile => catalog != null && catalog.Current != null ? catalog.Current : FallbackProfile;

        // Other components can ask before this one's Awake has run, so build the fallback on first use.
        private GripperProfile FallbackProfile =>
            fallbackProfile ?? (fallbackProfile = new GripperProfile { parallel = shape, approachDistance = approachDistance });
        public GripperShape Shape => Profile.parallel;
        public float ApproachDistance => Profile.approachDistance;

        [Header("Query-only collision checks")]
        [SerializeField] private LayerMask collisionMask = Physics.DefaultRaycastLayers;
        [SerializeField] private bool drawCollisionShapes = true;
        private string motionFailure;
        private GraspRecord pendingRecord;
        private GraspChoice pendingChoice;
        private TrashItem pendingItem;
        private List<GripperCollision.PayloadPart> payload;
        private Rigidbody body;
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private float leftOffset;
        private float rightOffset;
        private SortingBin[] bins;
        private string lastResult = "Waiting for trash";
        private readonly HashSet<TrashItem> attempted = new HashSet<TrashItem>();
        private readonly WaitForFixedUpdate waitForFixedUpdate = new WaitForFixedUpdate();
        private Coroutine runRoutine;
        private TrashItem heldItem;
        private FixedJoint heldJoint;
        private GripperProfile fallbackProfile;

        private void Awake()
        {
            GripperCollision.Mask = collisionMask;
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            if (policy == null) policy = FindAnyObjectByType<GraspPolicy>();
            if (dataset == null) dataset = FindAnyObjectByType<GraspDataset>();
            if (belt == null) belt = FindAnyObjectByType<ConveyorBelt>();
            if (spawner == null) spawner = FindAnyObjectByType<TrashSpawner>();
            if (visual == null) visual = GetComponent<GripperVisual>();
            if (arm == null) arm = FindAnyObjectByType<RobotArmDisplay>();
            if (robotCamera == null) robotCamera = FindAnyObjectByType<RobotCamera>();
            if (catalog == null) catalog = FindAnyObjectByType<GripperCatalog>();
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

        private void OnEnable()
        {
            if (catalog != null) catalog.Changed += HandleGripperChanged;
        }

        private void OnDisable()
        {
            if (catalog != null) catalog.Changed -= HandleGripperChanged;
        }

        // A different gripper was chosen: drop whatever the robot was doing and start over with it.
        private void HandleGripperChanged(GripperProfile profile)
        {
            ResetRobot();
            ResetStats();
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
            FinishAttempt(false, "aborted", pendingRecord?.outcome.stage ?? "plan");
            StopAllCoroutines();
            payload = null;
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
            if (visual != null) visual.SetBusy(false);
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
            if (visual != null)
            {
                visual.Apply(leftOffset, rightOffset);
            }
            else if (!Profile.IsSuction)
            {
                ApplyFingerLayout();
            }
        }

        private IEnumerator Run()
        {
            while (true)
            {
                TrashItem target = Active ? FindTarget() : null;
                if (target == null)
                {
                    motionFailure = null;
                    if (Vector3.Distance(body.position, homePosition) > .005f)
                        yield return MoveViaSafeHeight(homePosition, homeRotation);
                    else MoveToward(homePosition, homeRotation);
                    yield return waitForFixedUpdate;
                    continue;
                }
                if (visual != null) visual.SetBusy(true);
                yield return StartCoroutine(TrySort(target));
                if (visual != null) visual.SetBusy(false);
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
            var profile = Profile;
            GraspChoice choice = default;
            bool chosen = false;
            yield return policy.ChooseGraspAsync(item, c => { choice = c; chosen = true; });
            if (!chosen || !IsOnBelt(item)) yield break;
            pendingChoice = choice;
            pendingItem = item;
            pendingRecord = GraspRecord.Create(dataset, GraspRecord.SourceRobot, item, choice.LocalGrasp,
                "gripper", profile.id, GraspRecord.InputNone);
            pendingRecord.checker_version = GripperCollision.Version;
            pendingRecord.parent_record_id = choice.ParentRecordId;
            pendingRecord.outcome.stage = "plan";
            if (robotCamera != null) pendingRecord.image = robotCamera.Capture(item);
            motionFailure = null;
            OpenFingers();

            var planned = WorldGrasp(choice, item);
            var lineUp = planned;
            lineUp.Position -= planned.Approach * profile.approachDistance;
            bool feasible = GripperCollision.Feasible(profile, planned, item.Body, body, out string rejection);
            pendingRecord.feasible = feasible;
            if (!feasible) { FinishAttempt(false, rejection, "plan"); yield break; }
            // Arm reach is separate from the gripper's own feasibility label.
            if (arm != null && (!arm.CanReach(planned) || !arm.CanReach(lineUp)))
            { FinishAttempt(false, "out_of_reach", "plan"); yield break; }
            // The arm's links must not pass through the belt or bins at the poses it would hold.
            // Arm-specific, so it's labeled separately and never changes the grasp's own feasibility label.
            if (arm != null && (!arm.LinksClear(planned) || !arm.LinksClear(lineUp)))
            { FinishAttempt(false, "arm_collision", "plan"); yield break; }

            GraspPlanned?.Invoke(item, choice);

            yield return MoveViaSafeHeight(lineUp.Position, lineUp.Rotation);
            if (motionFailure != null) { FinishAttempt(false, motionFailure, "approach"); yield break; }
            pendingRecord.outcome.stage = "approach";
            for (int phase = 0; phase < 2; phase++)
            {
                // The belt kept moving while the gripper traveled: recheck the grasp once before the final approach.
                // (The per-step motion guard in MoveToward covers the rest of the approach.)
                if (phase == 1 && IsOnBelt(item) &&
                    !GripperCollision.Feasible(profile, WorldGrasp(choice, item), item.Body, body, out rejection))
                { FinishAttempt(false, rejection, "approach"); yield break; }
                float start = Time.time;
                while (true)
                {
                    if (!IsOnBelt(item) || Time.time - start > stepTimeout)
                    { FinishAttempt(false, "aborted", "approach"); yield break; }
                    var target = WorldGrasp(choice, item);
                    if (phase == 0) target.Position -= target.Approach * profile.approachDistance;
                    bool reached = MoveToward(target.Position, target.Rotation, phase == 1 ? item.Body : null, true);
                    if (motionFailure != null) { FinishAttempt(false, motionFailure, "approach"); yield break; }
                    yield return waitForFixedUpdate;
                    if (reached) break;
                }
            }

            pendingRecord.outcome.stage = "grasp";
            bool success = false;
            var grasp = WorldGrasp(choice, item);
            float heldWidth = profile.IsSuction ? profile.suction.cupDiameter : 0;
            if (profile.IsSuction) success = profile.suction.TrySeal(grasp, item.Body);
            else
            {
                bool leftTouch = false, rightTouch = false;
                while (!(leftTouch && rightTouch) && (leftOffset > 0 || rightOffset > 0))
                {
                    if (!IsOnBelt(item)) { FinishAttempt(false, "aborted", "grasp"); yield break; }
                    grasp = WorldGrasp(choice, item);
                    MoveToward(grasp.Position, grasp.Rotation, item.Body);
                    if (motionFailure != null) { FinishAttempt(false, motionFailure, "grasp"); yield break; }
                    leftTouch = profile.parallel.FingerTouches(grasp, true, leftOffset, item.Body);
                    rightTouch = profile.parallel.FingerTouches(grasp, false, rightOffset, item.Body);
                    float left = leftTouch ? leftOffset : Mathf.Max(0, leftOffset - closeSpeed * Time.fixedDeltaTime);
                    float right = rightTouch ? rightOffset : Mathf.Max(0, rightOffset - closeSpeed * Time.fixedDeltaTime);
                    if (GripperCollision.BodyOverlaps(profile.Model, grasp, left * 2, body, item.Body, right))
                    { FinishAttempt(false, "collision_at_grasp", "grasp"); yield break; }
                    leftOffset = left; rightOffset = right;
                    yield return waitForFixedUpdate;
                }
                success = leftTouch && rightTouch && leftOffset + rightOffset > 2f * profile.parallel.touchSkin &&
                    profile.parallel.TryGrasp(grasp, item.Body, out _);
                heldWidth = leftOffset + rightOffset;
            }
            pendingRecord.outcome.grasp_success = success;
            if (!success) { FinishAttempt(false, profile.IsSuction ? "no_seal" : "no_contact", "grasp"); yield break; }
            pendingRecord.grasp.width_m = GraspMath.Round(heldWidth);
            pendingRecord.outcome.stage = "carry";
            // Capture the object's collider envelopes in the gripper frame before the joint moves it.
            payload = GripperCollision.CapturePayload(item.Body, CurrentPose);
            item.BeginRobotHold();
            heldItem = item;
            heldJoint = item.gameObject.AddComponent<FixedJoint>();
            heldJoint.connectedBody = body;

            // Withdraw along the approach, then lift vertically, then cross above the belt.
            var retreat = body.position - grasp.Approach * profile.approachDistance;
            yield return MoveTo(retreat, body.rotation);
            if (motionFailure == null)
            {
                var bin = FindBin(item.CorrectBin);
                if (bin == null) motionFailure = "aborted";
                else
                {
                    // Release as low over the bin as the gripper fits. Held sideways, its housing can reach over
                    // the belt beside the bin, so try a little higher before giving up.
                    var above = bin.transform.position;
                    for (int i = 0; i < 3; i++)
                    {
                        motionFailure = null;
                        float height = releaseHeight + i * releaseRaiseStep;
                        yield return MoveViaSafeHeight(new Vector3(above.x, height, above.z), body.rotation);
                        if (motionFailure != "path_blocked") break;
                    }
                }
            }
            if (motionFailure != null) { FinishAttempt(false, motionFailure, "carry"); yield break; }
            FinishAttempt(true, "none", "release");
            yield return new WaitForSeconds(.3f);
        }

        private void ReleasePayload()
        {
            if (heldJoint != null) Destroy(heldJoint);
            if (heldItem != null) heldItem.EndRobotHold();
            heldItem = null; heldJoint = null; payload = null;
        }

        private void FinishAttempt(bool completed, string reason, string stage)
        {
            if (pendingRecord == null) return;
            var record = pendingRecord;
            pendingRecord = null; // Event handlers may reset the robot; finish exactly once.
            record.outcome.failure_reason = reason;
            record.outcome.stage = stage;
            record.outcome.motion_success = completed;
            record.outcome.correct = completed;
            record.outcome.bin = completed ? record.correct_bin : GraspRecord.NoBin;
            bool counts = reason != "aborted";
            if (counts)
            {
                Attempts++;
                if (completed) Successes++;
            }
            string detail = reason.StartsWith("collision") || reason == "path_blocked" ? ": " + GripperCollision.LastObstacle
                : reason == "arm_collision" && arm != null ? ": " + arm.LastObstacle : "";
            lastResult = completed ? "Grasped and released over the bin" : stage + " rejected: " + reason + detail;
            if (!pendingChoice.FromData) lastResult += " (random guess)";
            else if (pendingChoice.Rank > 1) lastResult += $" (grasp #{pendingChoice.Rank}: better-agreed ones collide here)";
            Debug.Log("[SortQuest] Robot " + lastResult);
            if (visual != null) visual.Flash(completed);
            ReleasePayload();
            OpenFingers();
            UpdateStatus();
            if (dataset != null) dataset.Add(record);
            AttemptFinished?.Invoke(new Attempt
            {
                Item = pendingItem, Choice = pendingChoice, Success = completed, Counts = counts, FailureReason = reason
            });
        }

        private void UpdateStatus()
        {
            if (statusText == null)
            {
                return;
            }
            string accuracy = Attempts == 0 ? "no attempts yet" : $"{Successes} of {Attempts} grasps ({Accuracy * 100f:F0}%)";
            statusText.text = $"ROBOT: {Profile.displayName.ToUpperInvariant()}\n<size=60%>{accuracy}\n{lastResult}</size>";
        }

        // ---------- Motion ----------

        private GripperGrasp CurrentPose => new GripperGrasp { Position = body.position, Rotation = body.rotation };
        private float SafeHeight => Mathf.Max(carryHeight, belt.StartPosition.y + .25f, body.position.y);

        private bool SegmentBlocked(GripperGrasp from, GripperGrasp to, Rigidbody contact = null, bool endpointOnly = false)
        {
            float distance = Vector3.Distance(from.Position, to.Position) +
                Quaternion.Angle(from.Rotation, to.Rotation) * Mathf.Deg2Rad * (Profile.MountOffset + .15f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance / .01f));
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                var pose = new GripperGrasp { Position = Vector3.Lerp(from.Position, to.Position, t),
                    Rotation = Quaternion.Slerp(from.Rotation, to.Rotation, t) };
                var allowed = endpointOnly && i != steps ? null : contact;
                if (GripperCollision.BodyOverlaps(Profile.Model, pose, leftOffset * 2, body, allowed, rightOffset,
                    heldItem != null ? heldItem.Body : null) ||
                    GripperCollision.PayloadOverlaps(payload, pose, heldItem != null ? heldItem.Body : null, body)) return true;
            }
            return false;
        }

        private bool MoveToward(Vector3 position, Quaternion rotation, Rigidbody contact = null, bool endpointOnly = false)
        {
            var next = new GripperGrasp {
                Position = Vector3.MoveTowards(body.position, position, moveSpeed * Time.fixedDeltaTime),
                Rotation = Quaternion.RotateTowards(body.rotation, rotation, turnSpeed * Time.fixedDeltaTime) };
            bool reached = Vector3.Distance(next.Position, position) < .001f && Quaternion.Angle(next.Rotation, rotation) < .5f;
            if (SegmentBlocked(CurrentPose, next, endpointOnly && !reached ? null : contact, endpointOnly))
            { motionFailure = "collision_during_motion"; return false; }
            body.MovePosition(next.Position);
            body.MoveRotation(next.Rotation);
            return reached;
        }

        private IEnumerator MoveTo(Vector3 position, Quaternion rotation)
        {
            var target = new GripperGrasp { Position = position, Rotation = rotation };
            if (SegmentBlocked(CurrentPose, target)) { motionFailure = "path_blocked"; yield break; }
            float start = Time.time;
            while (motionFailure == null)
            {
                bool reached = MoveToward(position, rotation);
                yield return waitForFixedUpdate;
                if (reached) yield break;
                if (Time.time - start > stepTimeout) { motionFailure = "aborted"; yield break; }
            }
        }

        private IEnumerator MoveViaSafeHeight(Vector3 position, Quaternion rotation)
        {
            GripperGrasp[] route = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                float height = SafeHeight + attempt * .25f;
                var candidate = new[] {
                    new GripperGrasp { Position = new Vector3(body.position.x, height, body.position.z), Rotation = body.rotation },
                    new GripperGrasp { Position = new Vector3(position.x, height, position.z), Rotation = rotation },
                    new GripperGrasp { Position = position, Rotation = rotation } };
                var from = CurrentPose;
                bool blocked = false;
                foreach (var to in candidate) { if (SegmentBlocked(from, to)) { blocked = true; break; } from = to; }
                if (!blocked) { route = candidate; break; }
            }
            if (route == null) { motionFailure = "path_blocked"; yield break; }
            foreach (var to in route)
            {
                yield return MoveTo(to.Position, to.Rotation);
                if (motionFailure != null) yield break;
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawCollisionShapes) return;
            var parts = new List<GripperPart>();
            Profile.Model.GetParts(parts, Application.isPlaying ? leftOffset : Profile.parallel.OpenOffset,
                Application.isPlaying ? rightOffset : Profile.parallel.OpenOffset);
            var previous = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            foreach (var part in parts) { Gizmos.color = part.Contact ? Color.green : Color.cyan; Gizmos.DrawWireCube(part.Center, part.Size); }
            Gizmos.matrix = previous;
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
            if (body != null && GripperCollision.BodyOverlaps(Profile.Model, CurrentPose,
                Profile.parallel.maxOpening, body)) return;
            leftOffset = Profile.parallel.OpenOffset;
            rightOffset = Profile.parallel.OpenOffset;
        }

        private void ApplyFingerLayout()
        {
            Profile.parallel.Layout(palm, fingerLeft, fingerRight, leftOffset, rightOffset);
        }
    }
}
