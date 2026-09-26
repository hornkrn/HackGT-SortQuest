using System.Collections.Generic;
using UnityEngine;

namespace SortQuest
{
    /// <summary>The grasp the robot will try on an item, and how sure it is.</summary>
    public struct GraspChoice
    {
        /// <summary>The grasp in the item's frame.</summary>
        public GripperGrasp LocalGrasp;

        /// <summary>False when there was no data and the grasp is a random guess.</summary>
        public bool FromData;

        /// <summary>Share of successful grasps that agree with the chosen one (0 when guessing).</summary>
        public float Confidence;

        /// <summary>How many successful grasps agree with the chosen one, including itself.</summary>
        public int Support;

        /// <summary>How many successful grasps the robot has for this item and gripper.</summary>
        public int GoodCount;

        /// <summary>How many of those came straight from people (the rest were practiced variations).</summary>
        public int HumanCount;
    }

    /// <summary>
    /// Chooses the robot's grasp for the selected gripper. With no data it guesses a random grasp on the item's
    /// bounds. With data it uses the medoid: the successful grasp with the most other successful grasps nearby.
    /// Grasps are never averaged, since the average of two good grasps can be a grasp on empty air.
    ///
    /// Two-finger grippers learn from good human grasps plus variations practiced for that gripper.
    /// The suction cup learns only from practiced variations, because a pinch has to be converted to a
    /// contact point on the surface first (the GraspAugmenter does that).
    /// </summary>
    public class GraspPolicy : MonoBehaviour
    {
        [Tooltip("Found automatically if left empty.")]
        [SerializeField] private GraspDataset dataset;

        [Tooltip("Found automatically if left empty. Without one, the standard gripper is used.")]
        [SerializeField] private GripperCatalog catalog;

        [Header("Two-finger agreement")]
        [Tooltip("Two grasps agree if their centers are this close (meters)...")]
        [SerializeField] private float positionTolerance = 0.02f;

        [Tooltip("...and their orientations are within this angle (degrees).")]
        [SerializeField] private float angleTolerance = 20f;

        [Header("Suction agreement")]
        [Tooltip("Two suction grasps agree if their contact points are this close (meters)...")]
        [SerializeField] private float suctionPositionTolerance = 0.015f;

        [Tooltip("...and their approach directions are within this angle (degrees). Cup roll doesn't matter.")]
        [SerializeField] private float suctionAngleTolerance = 20f;

        [Tooltip("Random guesses tilt the gripper by up to this many degrees around its approach direction.")]
        [SerializeField] private float randomRollRange = 30f;

        private static readonly Vector3[] LocalAxes =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
        };

        public GripperProfile Profile => GripperCatalog.CurrentOrStandard(catalog);

        private void Awake()
        {
            if (dataset == null) dataset = FindAnyObjectByType<GraspDataset>();
            if (catalog == null) catalog = FindAnyObjectByType<GripperCatalog>();
        }

        public GraspChoice ChooseGrasp(TrashItem item)
        {
            GripperProfile profile = Profile;
            if (TryGetLearnedGrasp(item.ItemType, profile, out GraspChoice learned))
            {
                return learned;
            }
            return new GraspChoice { LocalGrasp = RandomGrasp(item, profile), FromData = false };
        }

        /// <summary>The medoid of the successful grasps for an item type and the selected gripper.</summary>
        public bool TryGetLearnedGrasp(ItemType itemType, out GraspChoice choice)
        {
            return TryGetLearnedGrasp(itemType, Profile, out choice);
        }

        public bool TryGetLearnedGrasp(ItemType itemType, GripperProfile profile, out GraspChoice choice)
        {
            choice = default;
            List<GraspRecord> candidates = LearningGrasps(itemType, profile);
            if (candidates.Count == 0)
            {
                return false;
            }

            int humanCount = 0;
            var grasps = new List<GripperGrasp>(candidates.Count);
            foreach (GraspRecord record in candidates)
            {
                grasps.Add(record.LocalGrasp);
                if (record.source == GraspRecord.SourceHuman) humanCount++;
            }

            // Medoid: the grasp with the most agreeing grasps. Ties go to the tighter cluster.
            int bestIndex = 0;
            int bestSupport = 0;
            float bestSpread = float.MaxValue;
            for (int i = 0; i < grasps.Count; i++)
            {
                int support = 0;
                float spread = 0f;
                for (int j = 0; j < grasps.Count; j++)
                {
                    if (Agree(grasps[i], grasps[j], profile))
                    {
                        support++;
                        spread += Vector3.Distance(grasps[i].Position, grasps[j].Position);
                    }
                }
                if (support > bestSupport || (support == bestSupport && spread < bestSpread))
                {
                    bestIndex = i;
                    bestSupport = support;
                    bestSpread = spread;
                }
            }

            choice = new GraspChoice
            {
                LocalGrasp = grasps[bestIndex],
                FromData = true,
                Confidence = (float)bestSupport / grasps.Count,
                Support = bestSupport,
                GoodCount = grasps.Count,
                HumanCount = humanCount
            };
            return true;
        }

        /// <summary>
        /// The good grasps the robot learns from for this item and gripper: practiced variations checked against
        /// this gripper, plus (for two-finger grippers) good human grasps. The robot's own attempts are never used.
        /// </summary>
        public List<GraspRecord> LearningGrasps(ItemType itemType, GripperProfile profile)
        {
            var result = new List<GraspRecord>();
            if (dataset == null)
            {
                return result;
            }
            foreach (GraspRecord record in dataset.GoodGrasps(itemType))
            {
                bool humanForParallel = record.source == GraspRecord.SourceHuman && !profile.IsSuction;
                bool practicedForThisGripper = record.source == GraspRecord.SourceAugmented && record.gripper == profile.id;
                if (humanForParallel || practicedForThisGripper)
                {
                    result.Add(record);
                }
            }
            return result;
        }

        public bool Agree(GripperGrasp a, GripperGrasp b, GripperProfile profile)
        {
            if (profile.IsSuction)
            {
                return Vector3.Distance(a.Position, b.Position) <= suctionPositionTolerance &&
                       Vector3.Angle(a.Approach, b.Approach) <= suctionAngleTolerance;
            }
            return Vector3.Distance(a.Position, b.Position) <= positionTolerance &&
                   GraspAngle(a.Rotation, b.Rotation) <= angleTolerance;
        }

        /// <summary>
        /// Angle between two gripper orientations. A two-finger gripper turned 180 degrees around its
        /// approach direction makes the same grasp with the fingers swapped, so that counts as 0.
        /// </summary>
        public static float GraspAngle(Quaternion a, Quaternion b)
        {
            float direct = Quaternion.Angle(a, b);
            float swapped = Quaternion.Angle(a, b * Quaternion.Euler(0f, 0f, 180f));
            return Mathf.Min(direct, swapped);
        }

        /// <summary>
        /// A random grasp on the item's bounds, approached along one of the item's axes (never from below).
        /// Two-finger: a random point inside the bounds, closing along a perpendicular axis with a little tilt.
        /// Suction: a random point on the face the cup approaches, pressing straight in.
        /// </summary>
        private GripperGrasp RandomGrasp(TrashItem item, GripperProfile profile)
        {
            Bounds bounds = LocalBounds(item);
            Quaternion itemRotation = item.transform.rotation;

            var approaches = new List<Vector3>();
            foreach (Vector3 axis in LocalAxes)
            {
                // Approach points toward the item; a positive world y would mean coming up from below the belt.
                if ((itemRotation * axis).y < 0.3f)
                {
                    approaches.Add(axis);
                }
            }
            Vector3 approach = approaches[Random.Range(0, approaches.Count)];

            var perpendiculars = new List<Vector3>();
            foreach (Vector3 axis in LocalAxes)
            {
                if (Mathf.Abs(Vector3.Dot(axis, approach)) < 0.1f)
                {
                    perpendiculars.Add(axis);
                }
            }
            Vector3 side = perpendiculars[Random.Range(0, perpendiculars.Count)];

            if (profile.IsSuction)
            {
                // A point on the face the cup pushes into: the far side of the bounds along -approach.
                Vector3 onFace = Vector3.Scale(bounds.extents * 0.8f,
                    new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)));
                onFace -= approach * Vector3.Dot(onFace, approach);
                Vector3 facePoint = bounds.center + onFace - approach * Mathf.Abs(Vector3.Dot(bounds.extents, approach));
                return new GripperGrasp
                {
                    Position = facePoint,
                    Rotation = Quaternion.LookRotation(approach, side),
                    Width = profile.suction.cupDiameter
                };
            }

            Vector3 offset = Vector3.Scale(bounds.extents * 0.8f,
                new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)));
            Quaternion rotation = Quaternion.LookRotation(approach, Vector3.Cross(approach, side)) *
                                  Quaternion.Euler(0f, 0f, Random.Range(-randomRollRange, randomRollRange));
            return new GripperGrasp
            {
                Position = bounds.center + offset,
                Rotation = rotation,
                Width = Mathf.Abs(Vector3.Dot(bounds.size, side))
            };
        }

        /// <summary>The item's bounds in its own frame, in meters (scale applied, rotation not).</summary>
        public static Bounds LocalBounds(TrashItem item)
        {
            MeshFilter meshFilter = item.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                return new Bounds(Vector3.zero, Vector3.one * 0.05f);
            }
            Bounds meshBounds = meshFilter.sharedMesh.bounds;
            Vector3 scale = item.transform.lossyScale;
            Vector3 size = Vector3.Scale(meshBounds.size, scale);
            return new Bounds(Vector3.Scale(meshBounds.center, scale),
                new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)));
        }
    }
}
