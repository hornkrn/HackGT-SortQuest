using System;
using UnityEngine;

namespace SortQuest
{
    public enum GripperKind
    {
        Parallel,
        Suction
    }

    /// <summary>
    /// The physics checks for one kind of gripper. The robot, the augmenter, and the ghost grippers all use
    /// the same checks, so "the robot can do this grasp" means the same thing everywhere.
    /// </summary>
    public interface IGripperModel
    {
        /// <summary>Distance from the grasp point back to the top of the wrist, where the arm attaches.</summary>
        float MountOffset { get; }
        void GetParts(System.Collections.Generic.List<GripperPart> parts, float leftOffset, float rightOffset);
        bool BodyOverlaps(GripperGrasp pose, float opening, Rigidbody ignore);
        bool SweepBlocked(GripperGrasp fromPose, GripperGrasp toPose, float opening, Rigidbody ignore, float stepMeters);

        /// <summary>True if the gripper would pass through anything solid while moving in to the grasp.</summary>
        bool PathBlocked(GripperGrasp grasp, float approachDistance, Rigidbody ignore);

        /// <summary>The success rule for this gripper at a grasp on an item that isn't moving.</summary>
        bool TryGrasp(GripperGrasp grasp, Rigidbody item, out float width);
    }

    /// <summary>
    /// One selectable gripper: its id (stored in every grasp record), name, kind, and size.
    /// Grasps are learned separately per gripper.
    /// </summary>
    [Serializable]
    public class GripperProfile
    {
        [Tooltip("Stable id saved in every grasp record. Don't change it once data exists.")]
        public string id = "parallel_100mm";

        public string displayName = "Standard two-finger";

        [Tooltip("Short line shown on the menu block.")]
        public string description = "100 mm opening";

        public GripperKind kind = GripperKind.Parallel;

        [Tooltip("Used when the kind is Parallel.")]
        public GripperShape parallel = new GripperShape();

        [Tooltip("Used when the kind is Suction.")]
        public SuctionShape suction = new SuctionShape();

        [Tooltip("Distance before the grasp point where the gripper lines up before moving in.")]
        public float approachDistance = 0.12f;

        public Color accentColor = new Color(1f, 0.55f, 0.1f);

        public bool IsSuction => kind == GripperKind.Suction;
        public IGripperModel Model => IsSuction ? (IGripperModel)suction : parallel;
        public float MountOffset => Model.MountOffset;
    }
}
