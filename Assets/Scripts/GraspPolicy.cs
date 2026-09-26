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

        /// <summary>How many successful grasps exist for this item type.</summary>
        public int GoodCount;
    }

    /// <summary>
    /// Chooses the robot's grasp. With no data it guesses a random grasp on the item's bounds.
    /// With data it uses the medoid: the successful grasp with the most other successful grasps nearby.
    /// Grasps are never averaged, since the average of two good grasps can be a grasp on empty air.
    /// </summary>
    public class GraspPolicy : MonoBehaviour
    {
        [Tooltip("Found automatically if left empty.")]
        [SerializeField] private GraspDataset dataset;

        [Tooltip("Two grasps agree if their centers are this close (meters)...")]
        [SerializeField] private float positionTolerance = 0.02f;

        [Tooltip("...and their orientations are within this angle (degrees).")]
        [SerializeField] private float angleTolerance = 20f;

        [Tooltip("Random guesses tilt the gripper by up to this many degrees around its approach direction.")]
        [SerializeField] private float randomRollRange = 30f;

        private static readonly Vector3[] LocalAxes =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
        };

        private void Awake()
        {
            if (dataset == null)
            {
                dataset = FindAnyObjectByType<GraspDataset>();
            }
        }

        public GraspChoice ChooseGrasp(TrashItem item)
        {
            List<GraspRecord> good = dataset != null ? dataset.GoodGrasps(item.ItemType) : new List<GraspRecord>();
            if (good.Count == 0)
            {
                return new GraspChoice { LocalGrasp = RandomGrasp(item), FromData = false };
            }

            var grasps = new List<GripperGrasp>(good.Count);
            foreach (GraspRecord record in good)
            {
                grasps.Add(record.LocalGrasp);
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
                    if (Agree(grasps[i], grasps[j]))
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

            return new GraspChoice
            {
                LocalGrasp = grasps[bestIndex],
                FromData = true,
                Confidence = (float)bestSupport / grasps.Count,
                Support = bestSupport,
                GoodCount = grasps.Count
            };
        }

        public bool Agree(GripperGrasp a, GripperGrasp b)
        {
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
        /// A random grasp on the item's bounds: a random point inside them, approached along one of the
        /// item's axes (never from below), closing along a perpendicular axis with a little random tilt.
        /// </summary>
        private GripperGrasp RandomGrasp(TrashItem item)
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

            var closings = new List<Vector3>();
            foreach (Vector3 axis in LocalAxes)
            {
                if (Mathf.Abs(Vector3.Dot(axis, approach)) < 0.1f)
                {
                    closings.Add(axis);
                }
            }
            Vector3 closing = closings[Random.Range(0, closings.Count)];

            Vector3 offset = Vector3.Scale(bounds.extents * 0.8f,
                new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)));
            Quaternion rotation = Quaternion.LookRotation(approach, Vector3.Cross(approach, closing)) *
                                  Quaternion.Euler(0f, 0f, Random.Range(-randomRollRange, randomRollRange));

            return new GripperGrasp
            {
                Position = bounds.center + offset,
                Rotation = rotation,
                Width = Mathf.Abs(Vector3.Dot(bounds.size, closing))
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
