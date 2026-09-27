using System.Collections.Generic;
using UnityEngine;

namespace SortQuest
{
    // All dimensions are in the gripper frame. Cylinders use conservative box envelopes for queries.
    public struct GripperPart
    {
        public string Name;
        public Vector3 Center, Size;
        public bool Cylinder, Contact;
        public int Material; // body, metal, pad, accent, status
        public GripperPart(string name, Vector3 center, Vector3 size, int material = 0, bool contact = false, bool cylinder = false)
        { Name = name; Center = center; Size = size; Material = material; Contact = contact; Cylinder = cylinder; }
    }

    public static class GripperCollision
    {
        public const int Version = 2;
        private static readonly Collider[] Hits = new Collider[128];
        private static readonly List<GripperPart> Parts = new List<GripperPart>(20);
        // Query-only geometry: no colliders are added to the visual robot or the player's hands.
        public static int Mask = Physics.DefaultRaycastLayers;
        public static string LastObstacle { get; private set; }

        /// <param name="carried">An item the gripper is holding. It moves with the gripper, so no part can hit it;
        /// its own clearance from the scene is checked separately with PayloadOverlaps.</param>
        public static bool BodyOverlaps(IGripperModel model, GripperGrasp pose, float opening,
            Rigidbody ignore, Rigidbody contactTarget = null, float? rightOffset = null, Rigidbody carried = null)
        {
            Parts.Clear();
            model.GetParts(Parts, opening * .5f, rightOffset ?? opening * .5f);
            foreach (var part in Parts)
            {
                int count = Physics.OverlapBoxNonAlloc(pose.Position + pose.Rotation * part.Center,
                    part.Size * .5f, Hits, pose.Rotation, Mask, QueryTriggerInteraction.Ignore);
                if (count == Hits.Length) { LastObstacle = "query capacity exceeded"; return true; }
                for (int i = 0; i < count; i++)
                {
                    var hit = Hits[i];
                    if (ignore != null && hit.attachedRigidbody == ignore) continue;
                    if (carried != null && hit.attachedRigidbody == carried) continue;
                    if (part.Contact && contactTarget != null && hit.attachedRigidbody == contactTarget) continue;
                    LastObstacle = part.Name + " would hit " + hit.name;
                    return true;
                }
            }
            return false;
        }

        public static bool SweepBlocked(IGripperModel model, GripperGrasp from, GripperGrasp to,
            float opening, Rigidbody ignore, float stepMeters = .01f, Rigidbody contactAtEnd = null)
        {
            // Include angular travel at the outermost part, so rotation cannot skip thin obstacles.
            float travel = Vector3.Distance(from.Position, to.Position) +
                Quaternion.Angle(from.Rotation, to.Rotation) * Mathf.Deg2Rad * (model.MountOffset + .15f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(travel / Mathf.Max(.002f, stepMeters)));
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                var pose = new GripperGrasp { Position = Vector3.Lerp(from.Position, to.Position, t),
                    Rotation = Quaternion.Slerp(from.Rotation, to.Rotation, t) };
                if (BodyOverlaps(model, pose, opening, ignore, i == steps ? contactAtEnd : null)) return true;
            }
            return false;
        }

        public static bool Feasible(GripperProfile profile, GripperGrasp grasp, Rigidbody target,
            Rigidbody ignore, out string reason, float stepMeters = .01f)
        {
            var lineUp = grasp;
            lineUp.Position -= grasp.Approach * profile.approachDistance;
            float opening = profile.IsSuction ? 0 : profile.parallel.maxOpening;
            if (BodyOverlaps(profile.Model, lineUp, opening, ignore) ||
                SweepBlocked(profile.Model, lineUp, grasp, opening, ignore, stepMeters, target))
            { reason = "collision_on_approach"; return false; }
            float width = Mathf.Clamp(grasp.Width, 0, opening);
            // Predict actual asymmetric contact offsets, rather than trusting a human pinch width.
            if (!profile.IsSuction)
            {
                profile.parallel.ContactOffsets(grasp, target, out float left, out float right);
                if (BodyOverlaps(profile.Model, grasp, left * 2, ignore, target, right))
                { reason = "collision_at_grasp"; return false; }
                // Closing sweeps the carriages and pads too, not only the endpoint.
                int steps = Mathf.Max(1, Mathf.CeilToInt((opening - left - right) / .004f));
                for (int i = 0; i <= steps; i++)
                    if (BodyOverlaps(profile.Model, grasp, Mathf.Lerp(opening, left * 2, (float)i / steps), ignore,
                        target, Mathf.Lerp(opening * .5f, right, (float)i / steps)))
                    { reason = "collision_at_grasp"; return false; }
            }
            else if (BodyOverlaps(profile.Model, grasp, width, ignore, target))
            { reason = "collision_at_grasp"; return false; }
            reason = "none";
            return true;
        }

        // Conservative envelopes for every solid collider of the carried object. Captured before attaching.
        public struct PayloadPart { public Vector3 Center, Half; public Quaternion Rotation; }
        public static List<PayloadPart> CapturePayload(Rigidbody item, GripperGrasp pose)
        {
            var parts = new List<PayloadPart>();
            var inverse = Quaternion.Inverse(pose.Rotation);
            foreach (var col in item.GetComponentsInChildren<Collider>())
            {
                if (!col.enabled || col.isTrigger || col.attachedRigidbody != item) continue;
                parts.Add(new PayloadPart { Center = inverse * (col.bounds.center - pose.Position),
                    Half = col.bounds.extents, Rotation = inverse });
            }
            return parts;
        }

        public static bool PayloadOverlaps(List<PayloadPart> parts, GripperGrasp pose, Rigidbody item, Rigidbody robot)
        {
            if (parts == null) return false;
            foreach (var part in parts)
            {
                // Tiny skin allows an item resting on the belt to start lifting away from it.
                int count = Physics.OverlapBoxNonAlloc(pose.Position + pose.Rotation * part.Center,
                    Vector3.Max(part.Half - Vector3.one * .001f, Vector3.one * .0001f), Hits,
                    pose.Rotation * part.Rotation, Mask, QueryTriggerInteraction.Ignore);
                if (count == Hits.Length) { LastObstacle = "query capacity exceeded"; return true; }
                for (int i = 0; i < count; i++)
                    if (Hits[i].attachedRigidbody != item && (robot == null || Hits[i].attachedRigidbody != robot))
                    { LastObstacle = "carried item would hit " + Hits[i].name; return true; }
            }
            return false;
        }
    }
}
