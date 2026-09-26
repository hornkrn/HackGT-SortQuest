using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// A see-through gripper that sits on an item to show a grasp for the selected gripper, following the item.
    /// PlayerGrasp: shows the player's pinch converted to the selected gripper while they hold the item
    /// (for the suction cup, the pinch is moved onto the item's surface along the hand's approach direction).
    /// RobotPlan: shows where the robot is about to grasp, until its attempt ends.
    /// </summary>
    public class GhostGripper : MonoBehaviour
    {
        public enum Source
        {
            PlayerGrasp,
            RobotPlan
        }

        [SerializeField] private Source source = Source.PlayerGrasp;

        [Tooltip("Found automatically if left empty.")]
        [SerializeField] private GraspRecorder recorder;

        [Tooltip("Found automatically if left empty.")]
        [SerializeField] private RobotGripper robot;

        [Tooltip("Found automatically if left empty.")]
        [SerializeField] private GripperCatalog catalog;

        [Tooltip("See-through material (Sprites/Default). Created if left empty.")]
        [SerializeField] private Material material;

        [SerializeField] private Color color = new Color(0.3f, 0.9f, 1f, 0.45f);

        [Tooltip("Seconds the ghost stays after the grab or attempt ends.")]
        [SerializeField] private float lingerSeconds = 1.5f;

        private Transform root;
        private Transform fingerParts;
        private Transform suctionParts;
        private Transform palm;
        private Transform fingerLeft;
        private Transform fingerRight;
        private Transform cup;
        private Transform stem;
        private TrashItem target;
        private GripperGrasp localGrasp;
        private float hideAt = -1f;

        private GripperProfile Profile => GripperCatalog.CurrentOrStandard(catalog);

        private void Awake()
        {
            if (recorder == null) recorder = FindAnyObjectByType<GraspRecorder>();
            if (robot == null) robot = FindAnyObjectByType<RobotGripper>();
            if (catalog == null) catalog = FindAnyObjectByType<GripperCatalog>();
            if (material == null) material = VizUtil.FallbackMaterial();

            root = new GameObject("Ghost").transform;
            root.SetParent(transform, false);
            fingerParts = new GameObject("TwoFinger").transform;
            fingerParts.SetParent(root, false);
            palm = VizUtil.CreateShape("Palm", fingerParts, VizUtil.CubeMesh, material, color).transform;
            fingerLeft = VizUtil.CreateShape("FingerLeft", fingerParts, VizUtil.CubeMesh, material, color).transform;
            fingerRight = VizUtil.CreateShape("FingerRight", fingerParts, VizUtil.CubeMesh, material, color).transform;

            suctionParts = new GameObject("Suction").transform;
            suctionParts.SetParent(root, false);
            cup = VizUtil.CreateShape("Cup", suctionParts, VizUtil.CylinderMesh, material, color).transform;
            stem = VizUtil.CreateShape("Stem", suctionParts, VizUtil.CylinderMesh, material, color).transform;
            cup.localRotation = Quaternion.Euler(90f, 0f, 0f);
            stem.localRotation = Quaternion.Euler(90f, 0f, 0f);
            root.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (source == Source.PlayerGrasp && recorder != null)
            {
                recorder.GraspCaptured += HandleGraspCaptured;
            }
            if (source == Source.RobotPlan && robot != null)
            {
                robot.GraspPlanned += HandleGraspPlanned;
                robot.AttemptFinished += HandleAttemptFinished;
            }
        }

        private void OnDisable()
        {
            if (recorder != null) recorder.GraspCaptured -= HandleGraspCaptured;
            if (robot != null)
            {
                robot.GraspPlanned -= HandleGraspPlanned;
                robot.AttemptFinished -= HandleAttemptFinished;
            }
        }

        private void HandleGraspCaptured(TrashItem item, GripperGrasp grasp)
        {
            Show(item, grasp);
        }

        private void HandleGraspPlanned(TrashItem item, GraspChoice choice)
        {
            Show(item, choice.LocalGrasp);
        }

        private void HandleAttemptFinished(RobotGripper.Attempt attempt)
        {
            hideAt = Time.time + lingerSeconds * 0.5f;
        }

        private void Show(TrashItem item, GripperGrasp grasp)
        {
            target = item;
            localGrasp = grasp;
            hideAt = -1f;
            root.gameObject.SetActive(true);
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                root.gameObject.SetActive(false);
                return;
            }

            // Start fading out once the grab or the robot's attempt on this item is over.
            if (hideAt < 0f && !StillRelevant())
            {
                hideAt = Time.time + lingerSeconds;
            }
            if (hideAt >= 0f && Time.time >= hideAt)
            {
                target = null;
                root.gameObject.SetActive(false);
                return;
            }

            GripperProfile profile = Profile;
            GripperGrasp world = HandGripperPose.ToWorld(localGrasp, target.transform);
            bool suction = profile.IsSuction;
            if (suction && source == Source.PlayerGrasp)
            {
                // The player's pinch, moved onto the surface where a cup would press.
                if (!SuctionShape.ProjectOntoSurface(world, target.Body, out world))
                {
                    root.gameObject.SetActive(false);
                    return;
                }
                root.gameObject.SetActive(true);
            }

            root.SetPositionAndRotation(world.Position, world.Rotation);
            fingerParts.gameObject.SetActive(!suction);
            suctionParts.gameObject.SetActive(suction);
            if (suction)
            {
                SuctionShape shape = profile.suction;
                cup.localPosition = new Vector3(0f, 0f, -0.004f);
                cup.localScale = new Vector3(shape.cupDiameter, 0.004f, shape.cupDiameter);
                stem.localPosition = new Vector3(0f, 0f, -0.008f - shape.stemLength * 0.5f);
                stem.localScale = new Vector3(0.024f, shape.stemLength * 0.5f, 0.024f);
            }
            else
            {
                float half = Mathf.Clamp(localGrasp.Width, 0.005f, profile.parallel.maxOpening) * 0.5f;
                profile.parallel.Layout(palm, fingerLeft, fingerRight, half, half);
            }
        }

        private bool StillRelevant()
        {
            if (source == Source.PlayerGrasp)
            {
                return target.State == TrashItemState.Held && !target.LastHeldByRobot;
            }
            return target.State == TrashItemState.OnBelt ||
                   (target.State == TrashItemState.Held && target.LastHeldByRobot);
        }
    }
}
