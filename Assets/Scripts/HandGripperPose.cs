using Oculus.Interaction.Input;
using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// A two-finger gripper grasp. Gripper frame: +Z is the approach direction (toward the item),
    /// +X is the closing axis (the fingers close along X), +Y completes the frame.
    /// </summary>
    public struct GripperGrasp
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public float Width;

        public Vector3 Approach => Rotation * Vector3.forward;
        public Vector3 ClosingAxis => Rotation * Vector3.right;
    }

    /// <summary>
    /// Converts a human pinch into a gripper grasp, and moves grasps between world space and an item's frame.
    /// The item frame is the item's position and rotation only (meters, ignores scale).
    /// </summary>
    public static class HandGripperPose
    {
        /// <summary>Reads the thumb, index, wrist, and middle knuckle joints and converts them to a world-space grasp.</summary>
        public static bool TryCompute(IHand hand, out GripperGrasp grasp)
        {
            grasp = default;
            if (hand == null || !hand.IsTrackedDataValid)
            {
                return false;
            }
            if (!hand.GetJointPose(HandJointId.HandThumbTip, out Pose thumbTip) ||
                !hand.GetJointPose(HandJointId.HandIndexTip, out Pose indexTip) ||
                !hand.GetJointPose(HandJointId.HandWristRoot, out Pose wrist) ||
                !hand.GetJointPose(HandJointId.HandMiddle1, out Pose middleKnuckle))
            {
                return false;
            }
            grasp = Compute(thumbTip.position, indexTip.position, wrist.position, middleKnuckle.position);
            return true;
        }

        /// <summary>
        /// Center = midpoint of thumb and index tips. Closing axis = index tip minus thumb tip.
        /// Approach = wrist-to-middle-knuckle direction with the closing-axis part removed.
        /// Width = thumb-to-index distance.
        /// </summary>
        public static GripperGrasp Compute(Vector3 thumbTip, Vector3 indexTip, Vector3 wrist, Vector3 middleKnuckle)
        {
            Vector3 closing = indexTip - thumbTip;
            float width = closing.magnitude;
            closing = width > 1e-5f ? closing / width : Vector3.right;

            Vector3 approach = Vector3.ProjectOnPlane(middleKnuckle - wrist, closing);
            if (approach.sqrMagnitude < 1e-8f)
            {
                // Hand direction is parallel to the closing axis; pick any perpendicular direction.
                approach = Vector3.ProjectOnPlane(Vector3.down, closing);
                if (approach.sqrMagnitude < 1e-8f)
                {
                    approach = Vector3.ProjectOnPlane(Vector3.forward, closing);
                }
            }
            approach.Normalize();

            // LookRotation puts +Z on approach; this up vector puts +X on the closing axis.
            Vector3 up = Vector3.Cross(approach, closing);
            return new GripperGrasp
            {
                Position = (thumbTip + indexTip) * 0.5f,
                Rotation = Quaternion.LookRotation(approach, up),
                Width = width
            };
        }

        public static GripperGrasp ToLocal(GripperGrasp world, Transform item)
        {
            Quaternion inverse = Quaternion.Inverse(item.rotation);
            return new GripperGrasp
            {
                Position = inverse * (world.Position - item.position),
                Rotation = inverse * world.Rotation,
                Width = world.Width
            };
        }

        public static GripperGrasp ToWorld(GripperGrasp local, Transform item)
        {
            return ToWorld(local, item.position, item.rotation);
        }

        public static GripperGrasp ToWorld(GripperGrasp local, Vector3 itemPosition, Quaternion itemRotation)
        {
            return new GripperGrasp
            {
                Position = itemPosition + itemRotation * local.Position,
                Rotation = itemRotation * local.Rotation,
                Width = local.Width
            };
        }
    }
}
