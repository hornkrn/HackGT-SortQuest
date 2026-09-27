using System;
using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// Size of the two-finger gripper and the physics checks the robot uses to decide whether a grasp works.
    /// Shared by the RobotGripper, the GraspAugmenter, and the ghost grippers so they all agree.
    ///
    /// Gripper frame: the grasp point is the origin, +Z is the approach direction, and the fingers close along X.
    /// A finger's "offset" is the distance from the grasp point to the finger's inner face.
    /// </summary>
    [Serializable]
    public class GripperShape : IGripperModel
    {
        [Tooltip("Widest the fingers can open, in meters.")]
        public float maxOpening = 0.1f;

        public float fingerLength = 0.06f;
        public float fingerWidth = 0.025f;
        public float fingerThickness = 0.012f;

        [Tooltip("How far the fingertips reach past the grasp point.")]
        public float fingertipPastGrasp = 0.01f;

        public float palmThickness = 0.02f;

        [Tooltip("A finger closer than this to the item counts as touching it.")]
        public float touchSkin = 0.002f;

        private const float CloseStep = 0.001f;
        private static readonly Collider[] OverlapBuffer = new Collider[16];

        public float OpenOffset => maxOpening * 0.5f;

        // Palm housing, flange, and wrist behind the fingers, matching GripperVisual's two-finger model.
        public float MountOffset => fingerLength - fingertipPastGrasp + 0.135f;

        public bool BodyOverlaps(GripperGrasp pose, float opening, Rigidbody ignore) =>
            GripperCollision.BodyOverlaps(this, pose, opening, ignore);
        public bool SweepBlocked(GripperGrasp fromPose, GripperGrasp toPose, float opening, Rigidbody ignore, float stepMeters) =>
            GripperCollision.SweepBlocked(this, fromPose, toPose, opening, ignore, stepMeters);

        public void GetParts(System.Collections.Generic.List<GripperPart> parts, float leftOffset, float rightOffset)
        {
            float front = fingertipPastGrasp - fingerLength;
            float width = maxOpening + 2 * fingerThickness + .03f;
            parts.Add(new GripperPart("Housing", new Vector3(0, 0, front - .02f), new Vector3(width, .055f, .04f)));
            parts.Add(new GripperPart("Rail", new Vector3(0, 0, front - .002f), new Vector3(width - .01f, .014f, .004f), 1));
            parts.Add(new GripperPart("Stripe", new Vector3(0, .0285f, front - .02f), new Vector3(width + .002f, .003f, .02f), 3));
            AddWrist(parts, front - .05f);
            for (int side = -1; side <= 1; side += 2)
            {
                float offset = side < 0 ? leftOffset : rightOffset;
                float pad = Mathf.Min(.004f, fingerThickness * .4f);
                float link = fingerThickness - pad;
                string name = side < 0 ? "Left" : "Right";
                float x = side * (offset + pad + link * .5f);
                parts.Add(new GripperPart(name + "Carriage", new Vector3(x, 0, front + .006f), new Vector3(.024f, .04f, .02f)));
                parts.Add(new GripperPart(name + "Finger", new Vector3(x, 0, fingertipPastGrasp - fingerLength * .5f), new Vector3(link, fingerWidth, fingerLength), 1));
                parts.Add(new GripperPart(name + "Pad", new Vector3(side * (offset + pad * .5f), 0, fingertipPastGrasp - fingerLength * .3f), new Vector3(pad, fingerWidth * 1.1f, fingerLength * .55f), 2, true));
            }
        }

        internal static void AddWrist(System.Collections.Generic.List<GripperPart> parts, float z)
        {
            parts.Add(new GripperPart("Flange", new Vector3(0, 0, z), new Vector3(.075f, .075f, .02f), 1, false, true));
            parts.Add(new GripperPart("StatusLight", new Vector3(0, 0, z - .012f), new Vector3(.08f, .08f, .004f), 4, false, true));
            parts.Add(new GripperPart("Wrist", new Vector3(0, 0, z - .05f), new Vector3(.05f, .05f, .07f), 0, false, true));
            parts.Add(new GripperPart("WristRing", new Vector3(0, 0, z - .05f), new Vector3(.054f, .054f, .006f), 3, false, true));
        }

        public bool ContactOffsets(GripperGrasp grasp, Rigidbody item, out float left, out float right)
        {
            left = CloseUntilTouch(grasp, true, item, out bool leftTouch);
            right = CloseUntilTouch(grasp, false, item, out bool rightTouch);
            return leftTouch && rightTouch;
        }

        public bool TryGrasp(GripperGrasp grasp, Rigidbody item, out float width)
        {
            return TryClose(grasp, item, out width);
        }
        public Vector3 FingerSize => new Vector3(fingerThickness, fingerWidth, fingerLength);

        public Vector3 FingerLocalCenter(bool left, float offset)
        {
            float side = left ? -1f : 1f;
            return new Vector3(side * (offset + fingerThickness * 0.5f), 0f, fingertipPastGrasp - fingerLength * 0.5f);
        }

        /// <summary>Positions visual finger and palm boxes (children of the gripper root) for the given offsets.</summary>
        public void Layout(Transform palm, Transform fingerLeft, Transform fingerRight, float leftOffset, float rightOffset)
        {
            float fingerZ = fingertipPastGrasp - fingerLength * 0.5f;
            if (fingerLeft != null)
            {
                fingerLeft.localPosition = FingerLocalCenter(true, leftOffset);
                fingerLeft.localScale = FingerSize;
            }
            if (fingerRight != null)
            {
                fingerRight.localPosition = FingerLocalCenter(false, rightOffset);
                fingerRight.localScale = FingerSize;
            }
            if (palm != null)
            {
                palm.localPosition = new Vector3(0f, 0f, fingerZ - fingerLength * 0.5f - palmThickness * 0.5f);
                palm.localScale = new Vector3(maxOpening + 2f * fingerThickness, fingerWidth, palmThickness);
            }
        }

        /// <summary>True if the finger at this offset touches the item's colliders.</summary>
        public bool FingerTouches(GripperGrasp pose, bool left, float offset, Rigidbody item)
        {
            Vector3 center = pose.Position + pose.Rotation * FingerLocalCenter(left, offset);
            Vector3 half = FingerSize * 0.5f + Vector3.one * touchSkin;
            int count = Physics.OverlapBoxNonAlloc(center, half, OverlapBuffer, pose.Rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (OverlapBuffer[i].attachedRigidbody == item)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>True if the fully open finger overlaps anything solid (the item, the belt, other trash).</summary>
        public bool FingerBlocked(GripperGrasp pose, bool left, Rigidbody ignore)
        {
            Vector3 center = pose.Position + pose.Rotation * FingerLocalCenter(left, OpenOffset);
            Vector3 half = Vector3.Max(FingerSize * 0.5f - Vector3.one * touchSkin, Vector3.one * 0.001f);
            int count = Physics.OverlapBoxNonAlloc(center, half, OverlapBuffer, pose.Rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (ignore == null || OverlapBuffer[i].attachedRigidbody != ignore)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>True if the open fingers would pass through anything solid while moving in to the grasp.</summary>
        public bool PathBlocked(GripperGrasp grasp, float approachDistance, Rigidbody ignore)
        {
            const int samples = 4;
            for (int i = 0; i <= samples; i++)
            {
                GripperGrasp pose = grasp;
                pose.Position = grasp.Position - grasp.Approach * (approachDistance * i / samples);
                if (FingerBlocked(pose, true, ignore) || FingerBlocked(pose, false, ignore))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Closes both fingers in 1 mm steps on an item that isn't moving. True if both end up touching it,
        /// which is the robot's success rule. Used by the augmenter; the robot closes over time instead.
        /// </summary>
        public bool TryClose(GripperGrasp grasp, Rigidbody item, out float closedWidth)
        {
            float left = CloseUntilTouch(grasp, true, item, out bool leftTouch);
            float right = CloseUntilTouch(grasp, false, item, out bool rightTouch);
            closedWidth = left + right;
            return leftTouch && rightTouch;
        }

        private float CloseUntilTouch(GripperGrasp grasp, bool left, Rigidbody item, out bool touched)
        {
            for (float offset = OpenOffset; offset >= 0f; offset -= CloseStep)
            {
                if (FingerTouches(grasp, left, offset, item))
                {
                    touched = true;
                    return offset;
                }
            }
            touched = false;
            return 0f;
        }
    }
}
