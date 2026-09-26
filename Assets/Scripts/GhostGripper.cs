using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// A see-through gripper that sits on an item to show a grasp. It follows the item as it moves.
    /// PlayerGrasp: shows the player's pinch converted to a gripper grasp while they hold the item.
    /// RobotPlan: shows where the robot is about to grab, until its attempt ends.
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

        [Tooltip("See-through material (Sprites/Default). Created if left empty.")]
        [SerializeField] private Material material;

        [SerializeField] private Color color = new Color(0.3f, 0.9f, 1f, 0.45f);

        [Tooltip("Seconds the ghost stays after the grab or attempt ends.")]
        [SerializeField] private float lingerSeconds = 1.5f;

        [Tooltip("Gripper size; matches the robot gripper by default.")]
        [SerializeField] private GripperShape shape = new GripperShape();

        private Transform root;
        private Transform palm;
        private Transform fingerLeft;
        private Transform fingerRight;
        private TrashItem target;
        private GripperGrasp localGrasp;
        private float hideAt = -1f;

        private void Awake()
        {
            if (recorder == null) recorder = FindAnyObjectByType<GraspRecorder>();
            if (robot == null) robot = FindAnyObjectByType<RobotGripper>();
            if (material == null) material = VizUtil.FallbackMaterial();

            root = new GameObject("Ghost").transform;
            root.SetParent(transform, false);
            palm = VizUtil.CreateShape("Palm", root, VizUtil.CubeMesh, material, color).transform;
            fingerLeft = VizUtil.CreateShape("FingerLeft", root, VizUtil.CubeMesh, material, color).transform;
            fingerRight = VizUtil.CreateShape("FingerRight", root, VizUtil.CubeMesh, material, color).transform;
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

            GripperGrasp world = HandGripperPose.ToWorld(localGrasp, target.transform);
            root.SetPositionAndRotation(world.Position, world.Rotation);
            float half = Mathf.Clamp(localGrasp.Width, 0.005f, shape.maxOpening) * 0.5f;
            shape.Layout(palm, fingerLeft, fingerRight, half, half);
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
