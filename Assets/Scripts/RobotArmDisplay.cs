using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// A robot arm that holds the gripper, solved with inverse kinematics every frame.
    /// Like a typical 6-joint industrial arm, a turntable, shoulder, and elbow place the wrist,
    /// and a spherical wrist orients the gripper. The arm is visual (no colliders), but its reach is real:
    /// CanReach tells the RobotGripper which grasps a real arm of this size could not reach.
    /// </summary>
    public class RobotArmDisplay : MonoBehaviour
    {
        [Tooltip("The RobotGripper's transform, which the arm holds by its wrist.")]
        [SerializeField] private Transform gripper;

        [Tooltip("Distance from the grasp point back to the top of the gripper's wrist. " +
                 "Used only if the gripper has no RobotGripper; otherwise the selected gripper's value is used.")]
        [SerializeField] private float mountOffset = 0.185f;

        [Header("Size (meters)")]
        [SerializeField] private float shoulderHeight = 1.0f;
        [SerializeField] private float upperArmLength = 0.85f;
        [SerializeField] private float forearmLength = 0.8f;

        [Header("Materials")]
        [SerializeField] private Material bodyMaterial;
        [SerializeField] private Material accentMaterial;
        [SerializeField] private Material metalMaterial;

        public Vector3 Shoulder => transform.position + Vector3.up * shoulderHeight;
        public float MaxReach => upperArmLength + forearmLength;
        public float MinReach => Mathf.Abs(upperArmLength - forearmLength) + 0.05f;

        /// <summary>True when the arm is stretched as far as it goes and still can't reach the gripper.</summary>
        public bool OutOfReachNow { get; private set; }

        private Transform turret;
        private Transform shoulderJoint;
        private Transform upperArm;
        private Transform elbowJoint;
        private Transform forearm;
        private Transform wristJoint;
        private Vector3 lastDirection = Vector3.forward;
        private RobotGripper robotGripper;

        /// <summary>The selected gripper's wrist length, so the arm meets both two-finger and suction grippers.</summary>
        private float CurrentMountOffset => robotGripper != null ? robotGripper.Profile.MountOffset : mountOffset;

        private void Awake()
        {
            if (gripper == null)
            {
                RobotGripper robot = FindAnyObjectByType<RobotGripper>();
                if (robot != null) gripper = robot.transform;
            }
            if (gripper != null)
            {
                robotGripper = gripper.GetComponent<RobotGripper>();
            }
            Build();
        }

        /// <summary>Where the arm must put its wrist to hold the gripper at this pose.</summary>
        public Vector3 MountPoint(GripperGrasp pose)
        {
            return pose.Position + pose.Rotation * new Vector3(0f, 0f, -CurrentMountOffset);
        }

        public bool CanReach(GripperGrasp pose)
        {
            float distance = Vector3.Distance(Shoulder, MountPoint(pose));
            return distance >= MinReach && distance <= MaxReach - 0.01f;
        }

        private void LateUpdate()
        {
            if (gripper == null)
            {
                return;
            }
            Vector3 target = gripper.position + gripper.rotation * new Vector3(0f, 0f, -CurrentMountOffset);
            Solve(target);
        }

        /// <summary>
        /// Turntable faces the target; shoulder and elbow form a triangle (law of cosines) with the elbow up.
        /// </summary>
        private void Solve(Vector3 target)
        {
            Vector3 shoulder = Shoulder;
            Vector3 toTarget = target - shoulder;
            var flat = new Vector3(toTarget.x, 0f, toTarget.z);
            if (flat.sqrMagnitude > 1e-6f)
            {
                lastDirection = flat.normalized;
            }

            float distance = toTarget.magnitude;
            float reach = Mathf.Clamp(distance, MinReach, MaxReach - 0.001f);
            OutOfReachNow = distance > MaxReach;

            float a = upperArmLength;
            float b = forearmLength;
            float lift = Mathf.Atan2(toTarget.y, flat.magnitude);
            float bend = Mathf.Acos(Mathf.Clamp((a * a + reach * reach - b * b) / (2f * a * reach), -1f, 1f));
            float shoulderAngle = lift + bend;

            Vector3 elbow = shoulder + lastDirection * (a * Mathf.Cos(shoulderAngle)) + Vector3.up * (a * Mathf.Sin(shoulderAngle));
            Vector3 wrist = shoulder + (distance > 1e-4f ? toTarget / distance : lastDirection) * reach;

            turret.rotation = Quaternion.LookRotation(lastDirection, Vector3.up);
            shoulderJoint.position = shoulder;
            shoulderJoint.rotation = turret.rotation * Quaternion.Euler(0f, 0f, 90f);
            VizUtil.PlaceBetween(upperArm, shoulder, elbow, 0.055f);
            elbowJoint.position = elbow;
            elbowJoint.rotation = shoulderJoint.rotation;
            VizUtil.PlaceBetween(forearm, elbow, wrist, 0.045f);
            wristJoint.position = wrist;
        }

        private void Build()
        {
            Vector3 floor = transform.position;

            Transform plate = VizUtil.CreateShape("BasePlate", transform, VizUtil.CylinderMesh, bodyMaterial).transform;
            VizUtil.PlaceBetween(plate, floor, floor + Vector3.up * 0.04f, 0.28f);

            Transform pedestal = VizUtil.CreateShape("Pedestal", transform, VizUtil.CylinderMesh, bodyMaterial).transform;
            VizUtil.PlaceBetween(pedestal, floor, floor + Vector3.up * (shoulderHeight - 0.14f), 0.16f);

            turret = VizUtil.CreateShape("Turret", transform, VizUtil.CylinderMesh, accentMaterial).transform;
            turret.position = floor + Vector3.up * (shoulderHeight - 0.09f);
            turret.localScale = new Vector3(0.36f, 0.05f, 0.36f);

            // Shoulder and elbow joints are short cylinders lying across the arm, like motor housings.
            shoulderJoint = VizUtil.CreateShape("ShoulderJoint", transform, VizUtil.CylinderMesh, metalMaterial).transform;
            shoulderJoint.localScale = new Vector3(0.16f, 0.09f, 0.16f);
            upperArm = VizUtil.CreateShape("UpperArm", transform, VizUtil.CylinderMesh, accentMaterial).transform;
            elbowJoint = VizUtil.CreateShape("ElbowJoint", transform, VizUtil.CylinderMesh, metalMaterial).transform;
            elbowJoint.localScale = new Vector3(0.13f, 0.075f, 0.13f);
            forearm = VizUtil.CreateShape("Forearm", transform, VizUtil.CylinderMesh, accentMaterial).transform;
            wristJoint = VizUtil.CreateShape("WristJoint", transform, VizUtil.SphereMesh, metalMaterial).transform;
            wristJoint.localScale = Vector3.one * 0.075f;
        }
    }
}
