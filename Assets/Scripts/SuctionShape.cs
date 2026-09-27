using System;
using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// A suction cup gripper and its success rule. A suction grasp's position is the contact point on the item's
    /// surface, and its +Z (approach) points into the surface. The cup seals if the surface under it is flat enough
    /// and faces the cup closely enough; that is checked with raycasts, with no friction or pressure model.
    /// </summary>
    [Serializable]
    public class SuctionShape : IGripperModel
    {
        [Tooltip("Cup diameter in meters.")]
        public float cupDiameter = 0.04f;

        [Tooltip("Rays around the cup's rim used to check the seal.")]
        public int sealRingSamples = 8;

        [Tooltip("The surface under the rim may vary this much in depth (m) and still seal.")]
        public float maxSurfaceDeviation = 0.004f;

        [Tooltip("Surface normals under the rim may differ from the center by this many degrees.")]
        public float maxNormalDeviationDeg = 15f;

        [Tooltip("The cup may approach this many degrees away from straight into the surface.")]
        public float maxApproachTiltDeg = 20f;

        [Tooltip("Length of the stem between the cup and the wrist flange (visual).")]
        public float stemLength = 0.08f;

        public float touchSkin = 0.002f;

        private static readonly RaycastHit[] HitBuffer = new RaycastHit[16];
        private static readonly Collider[] OverlapBuffer = new Collider[16];

        public float CupRadius => cupDiameter * 0.5f;

        // Cup (0.012) + stem + flange, light ring, and wrist (0.095), matching GripperVisual's suction model.
        public float MountOffset => 0.012f + stemLength + 0.095f;

        public bool BodyOverlaps(GripperGrasp pose, float opening, Rigidbody ignore) =>
            GripperCollision.BodyOverlaps(this, pose, opening, ignore);
        public bool SweepBlocked(GripperGrasp fromPose, GripperGrasp toPose, float opening, Rigidbody ignore, float stepMeters) =>
            GripperCollision.SweepBlocked(this, fromPose, toPose, opening, ignore, stepMeters);

        public void GetParts(System.Collections.Generic.List<GripperPart> parts, float leftOffset, float rightOffset)
        {
            parts.Add(new GripperPart("Cup", new Vector3(0, 0, -.004f), new Vector3(cupDiameter, cupDiameter, .008f), 2, true, true));
            parts.Add(new GripperPart("Bellows", new Vector3(0, 0, -.010f), new Vector3(cupDiameter * .7f, cupDiameter * .7f, .006f), 2, false, true));
            float top = -(.012f + stemLength);
            parts.Add(new GripperPart("Stem", new Vector3(0, 0, (-.012f + top) * .5f), new Vector3(.024f, .024f, stemLength), 1, false, true));
            parts.Add(new GripperPart("StemBand", new Vector3(0, 0, -.012f - stemLength * .3f), new Vector3(.028f, .028f, .01f), 3, false, true));
            GripperShape.AddWrist(parts, top - .01f);
        }

        public bool PathBlocked(GripperGrasp grasp, float approachDistance, Rigidbody ignore)
        {
            // The cup's swept volume from the line-up point to 1 cm short of contact.
            float radius = Mathf.Max(0.001f, CupRadius - touchSkin);
            Vector3 start = grasp.Position - grasp.Approach * approachDistance;
            Vector3 end = grasp.Position - grasp.Approach * (CupRadius + 0.01f);
            int count = Physics.OverlapCapsuleNonAlloc(start, end, radius, OverlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (ignore == null || OverlapBuffer[i].attachedRigidbody != ignore)
                {
                    return true;
                }
            }
            return false;
        }

        public bool TryGrasp(GripperGrasp grasp, Rigidbody item, out float width)
        {
            width = cupDiameter;
            return TrySeal(grasp, item);
        }

        /// <summary>
        /// The seal rule: the center ray hits the item near the grasp point, the surface faces the cup within
        /// the tilt limit, and rays around the rim all hit the item at nearly the same depth and angle.
        /// </summary>
        public bool TrySeal(GripperGrasp grasp, Rigidbody item)
        {
            Vector3 approach = grasp.Approach;
            if (!RaycastItem(grasp.Position - approach * 0.05f, approach, 0.08f, item, out RaycastHit center))
            {
                return false;
            }
            if (Vector3.Distance(center.point, grasp.Position) > 0.005f ||
                Vector3.Angle(center.normal, -approach) > maxApproachTiltDeg)
            {
                return false;
            }

            float centerDepth = Vector3.Dot(center.point - grasp.Position, approach);
            Vector3 right = grasp.Rotation * Vector3.right;
            Vector3 up = grasp.Rotation * Vector3.up;
            float ringRadius = CupRadius - 0.002f;
            for (int k = 0; k < sealRingSamples; k++)
            {
                float angle = k * Mathf.PI * 2f / sealRingSamples;
                Vector3 offset = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * ringRadius;
                if (!RaycastItem(grasp.Position + offset - approach * 0.05f, approach, 0.08f, item, out RaycastHit rim))
                {
                    return false; // The rim hangs past the item's edge.
                }
                float depth = Vector3.Dot(rim.point - grasp.Position, approach);
                if (Mathf.Abs(depth - centerDepth) > maxSurfaceDeviation ||
                    Vector3.Angle(rim.normal, center.normal) > maxNormalDeviationDeg)
                {
                    return false; // Too curved or uneven to seal.
                }
            }
            return true;
        }

        /// <summary>
        /// Converts a grasp (for example a human pinch) into a suction grasp: casts along its approach direction
        /// onto the item and puts the cup at the hit point, facing straight into the surface.
        /// </summary>
        public static bool ProjectOntoSurface(GripperGrasp grasp, Rigidbody item, out GripperGrasp contact)
        {
            contact = grasp;
            Vector3 approach = grasp.Approach;
            if (!RaycastItem(grasp.Position - approach * 0.15f, approach, 0.3f, item, out RaycastHit hit))
            {
                // Fall back to aiming at the item's center.
                Vector3 toCenter = item.worldCenterOfMass - grasp.Position;
                if (toCenter.sqrMagnitude < 1e-8f ||
                    !RaycastItem(grasp.Position - toCenter.normalized * 0.15f, toCenter.normalized, 0.3f, item, out hit))
                {
                    return false;
                }
            }
            Vector3 into = -hit.normal;
            Vector3 up = Vector3.ProjectOnPlane(grasp.Rotation * Vector3.up, into);
            if (up.sqrMagnitude < 1e-6f)
            {
                up = Vector3.ProjectOnPlane(Vector3.up, into);
                if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Vector3.forward, into);
            }
            contact.Position = hit.point;
            contact.Rotation = Quaternion.LookRotation(into, up.normalized);
            return true;
        }

        /// <summary>Moves a suction grasp along its own approach direction until the cup touches the item.</summary>
        public static bool SlideOntoSurface(GripperGrasp grasp, Rigidbody item, out GripperGrasp contact)
        {
            contact = grasp;
            if (!RaycastItem(grasp.Position - grasp.Approach * 0.05f, grasp.Approach, 0.1f, item, out RaycastHit hit))
            {
                return false;
            }
            contact.Position = hit.point;
            return true;
        }

        /// <summary>The first hit along a ray, which must be on the item (anything in front of it blocks the ray).</summary>
        private static bool RaycastItem(Vector3 origin, Vector3 direction, float distance, Rigidbody item, out RaycastHit result)
        {
            result = default;
            int count = Physics.RaycastNonAlloc(origin, direction, HitBuffer, distance, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (HitBuffer[i].distance < best)
                {
                    best = HitBuffer[i].distance;
                    result = HitBuffer[i];
                    found = true;
                }
            }
            return found && result.collider != null && result.collider.attachedRigidbody == item;
        }
    }
}
