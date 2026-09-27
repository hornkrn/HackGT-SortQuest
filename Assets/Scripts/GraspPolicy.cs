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

        /// <summary>
        /// 1 when the robot uses its most agreed grasp. Higher when better-agreed grasps would have collided with
        /// the belt or other items at the item's current pose, so a lower-ranked one was used. 0 when guessing.
        /// </summary>
        public int Rank;

        /// <summary>The record_id of the learned grasp that was chosen, or null for a guess.</summary>
        public string ParentRecordId;
    }

    /// <summary>
    /// Chooses the robot's grasp for the selected gripper. With no data it guesses a random grasp on the item's
    /// bounds. With data it uses the medoid: the successful grasp with the most other successful grasps nearby.
    /// Grasps are never averaged, since the average of two good grasps can be a grasp on empty air.
    ///
    /// Before each attempt, grasps are collision-checked at the item's current pose, best agreed first, and the
    /// first one that fits is used. Random guesses are collision-checked the same way.
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

        [Header("Random guesses (no data yet)")]
        [Tooltip("Guesses tilt the gripper by up to this many degrees around its approach direction. Keep it well " +
                 "under the fingers' 30 degree contact limit, or tilted pads never grip.")]
        [SerializeField] private float guessRoll = 10f;

        [Tooltip("Guesses come from above this often (0 to 1). Side approaches at belt height usually hit the belt.")]
        [SerializeField, Range(0f, 1f)] private float guessFromAbove = 0.6f;

        [Tooltip("How far a guess's center may stray from the item's middle, as a share of the item's half-size.")]
        [SerializeField, Range(0f, 1f)] private float guessSpread = 0.15f;

        [Tooltip("Before each attempt, at most this many learned grasps are collision-checked, best agreed first.")]
        [SerializeField] private int maxFeasibilityChecks = 24;

        [Tooltip("With no data, up to this many random guesses are collision-checked and the first that fits is used. " +
                 "1 = always use the first guess.")]
        [SerializeField, Min(1)] private int randomGuessTries = 6;

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

        /// <summary>
        /// Like ChooseGrasp, but uses the best-agreed grasp that the gripper can reach without hitting the belt or
        /// other items at the item's current pose. Checks run best agreed first, two per frame, and stop at the first
        /// grasp that fits, so a large dataset doesn't delay the robot. If none of the checked grasps fit, the most
        /// agreed one is used anyway and the robot's own plan check records why it can't be done.
        /// Calls done only if the item is still on the belt.
        /// </summary>
        public System.Collections.IEnumerator ChooseGraspAsync(TrashItem item, System.Action<GraspChoice> done)
        {
            GripperProfile profile = Profile;
            List<GraspRecord> candidates = LearningGrasps(item.ItemType, profile);
            List<int> ranking = RankByAgreement(candidates, profile, out int[] support);
            if (ranking.Count == 0)
            {
                // No data yet: guess, but skip guesses that would drive the gripper into the belt or other items.
                GripperGrasp guess = default;
                for (int tries = 0; tries < Mathf.Max(1, randomGuessTries); tries++)
                {
                    if (item == null || item.State != TrashItemState.OnBelt) yield break;
                    guess = RandomGrasp(item, profile);
                    GripperGrasp guessWorld = HandGripperPose.ToWorld(guess, item.transform);
                    if (GripperCollision.Feasible(profile, guessWorld, item.Body, null, out _)) break;
                    if (tries % 2 == 1) yield return null;
                }
                if (item == null || item.State != TrashItemState.OnBelt) yield break;
                done(new GraspChoice { LocalGrasp = guess, FromData = false });
                yield break;
            }

            int chosen = 0;
            int checks = Mathf.Min(ranking.Count, Mathf.Max(1, maxFeasibilityChecks));
            for (int rank = 0; rank < checks; rank++)
            {
                if (item == null || item.State != TrashItemState.OnBelt) yield break;
                GripperGrasp world = HandGripperPose.ToWorld(candidates[ranking[rank]].LocalGrasp, item.transform);
                if (GripperCollision.Feasible(profile, world, item.Body, null, out _))
                {
                    chosen = rank;
                    break;
                }
                if (rank % 2 == 1) yield return null;
            }
            if (item == null || item.State != TrashItemState.OnBelt) yield break;
            int index = ranking[chosen];
            done(LearnedChoice(candidates, index, support[index], chosen + 1));
        }

        /// <summary>The medoid of the successful grasps for an item type and the selected gripper.</summary>
        public bool TryGetLearnedGrasp(ItemType itemType, out GraspChoice choice)
        {
            return TryGetLearnedGrasp(itemType, Profile, out choice);
        }

        public bool TryGetLearnedGrasp(ItemType itemType, GripperProfile profile, out GraspChoice choice)
        {
            return SelectMedoid(LearningGrasps(itemType, profile), profile, out choice);
        }

        private bool SelectMedoid(List<GraspRecord> candidates, GripperProfile profile, out GraspChoice choice)
        {
            choice = default;
            List<int> ranking = RankByAgreement(candidates, profile, out int[] support);
            if (ranking.Count == 0)
            {
                return false;
            }
            choice = LearnedChoice(candidates, ranking[0], support[ranking[0]], 1);
            return true;
        }

        /// <summary>
        /// Candidate indices ordered by how many grasps agree with each one (the medoid first).
        /// Ties go to the tighter cluster.
        /// </summary>
        private List<int> RankByAgreement(List<GraspRecord> candidates, GripperProfile profile, out int[] support)
        {
            var grasps = new List<GripperGrasp>(candidates.Count);
            foreach (GraspRecord record in candidates)
            {
                grasps.Add(record.LocalGrasp);
            }

            support = new int[grasps.Count];
            var spread = new float[grasps.Count];
            var ranking = new List<int>(grasps.Count);
            for (int i = 0; i < grasps.Count; i++)
            {
                for (int j = 0; j < grasps.Count; j++)
                {
                    if (Agree(grasps[i], grasps[j], profile))
                    {
                        support[i]++;
                        spread[i] += Vector3.Distance(grasps[i].Position, grasps[j].Position);
                    }
                }
                ranking.Add(i);
            }

            int[] supportCopy = support;
            ranking.Sort((a, b) => supportCopy[a] != supportCopy[b]
                ? supportCopy[b].CompareTo(supportCopy[a])
                : spread[a] != spread[b] ? spread[a].CompareTo(spread[b]) : a.CompareTo(b));
            return ranking;
        }

        private static GraspChoice LearnedChoice(List<GraspRecord> candidates, int index, int support, int rank)
        {
            int humanCount = 0;
            foreach (GraspRecord record in candidates)
            {
                if (record.source == GraspRecord.SourceHuman) humanCount++;
            }
            return new GraspChoice
            {
                LocalGrasp = candidates[index].LocalGrasp,
                FromData = true,
                Confidence = (float)support / candidates.Count,
                Support = support,
                GoodCount = candidates.Count,
                HumanCount = humanCount,
                Rank = rank,
                ParentRecordId = candidates[index].record_id
            };
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
                bool practicedForThisGripper = record.source == GraspRecord.SourceAugmented && record.gripper == profile.id &&
                    record.checker_version == GripperCollision.Version && record.feasible == true;
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
        /// A random guess a real gripper could plausibly make, approached along one of the item's axes (never from
        /// below, usually from above). It knows nothing about where to grab; it only avoids guesses that can't work:
        /// Two-finger: near the item's middle, closing across a side that fits between the open fingers, only as
        /// deep as the fingers reach and not so deep the fingertips hit the belt, with a little tilt.
        /// Suction: near the middle of the face the cup approaches, pressing straight in.
        /// </summary>
        private GripperGrasp RandomGrasp(TrashItem item, GripperProfile profile)
        {
            Bounds bounds = LocalBounds(item);
            Vector3 approach = RandomApproach(item.transform.rotation);
            float depthExtent = Mathf.Abs(Vector3.Dot(bounds.extents, approach));
            Vector3 face = bounds.center - approach * depthExtent; // middle of the face the gripper approaches

            if (profile.IsSuction)
            {
                Vector3 across = RandomPerpendicular(approach);
                return new GripperGrasp
                {
                    Position = face + Spread(bounds, approach),
                    Rotation = Quaternion.LookRotation(approach, across),
                    Width = profile.suction.cupDiameter
                };
            }

            // Close across a side that fits between the open fingers, when the item has one.
            GripperShape fingers = profile.parallel;
            var fits = new List<Vector3>();
            var perpendiculars = new List<Vector3>();
            foreach (Vector3 axis in LocalAxes)
            {
                if (Mathf.Abs(Vector3.Dot(axis, approach)) > 0.1f) continue;
                perpendiculars.Add(axis);
                if (Mathf.Abs(Vector3.Dot(bounds.size, axis)) < fingers.maxOpening - 2f * fingers.touchSkin) fits.Add(axis);
            }
            List<Vector3> sides = fits.Count > 0 ? fits : perpendiculars;
            Vector3 side = sides[Random.Range(0, sides.Count)];

            // How deep past the approached face: no deeper than the fingers reach before the palm touches the item,
            // and shallow enough that the fingertips stay above the item's far side (the belt, when from above).
            float palmReach = fingers.fingerLength - fingers.fingertipPastGrasp - 0.005f;
            float tipLimit = 2f * depthExtent - fingers.fingertipPastGrasp - 0.003f;
            float deepest = Mathf.Max(0.002f, Mathf.Min(palmReach, tipLimit));
            float depth = Random.Range(0.5f * deepest, deepest);

            Quaternion rotation = Quaternion.LookRotation(approach, Vector3.Cross(approach, side)) *
                                  Quaternion.Euler(0f, 0f, Random.Range(-guessRoll, guessRoll));
            return new GripperGrasp
            {
                Position = face + approach * depth + Spread(bounds, approach),
                Rotation = rotation,
                Width = Mathf.Min(Mathf.Abs(Vector3.Dot(bounds.size, side)), fingers.maxOpening)
            };
        }

        /// <summary>An item axis to approach along: never from below, and from above with probability guessFromAbove.</summary>
        private Vector3 RandomApproach(Quaternion itemRotation)
        {
            var approaches = new List<Vector3>();
            Vector3 fromAbove = Vector3.zero;
            float lowest = float.MaxValue;
            foreach (Vector3 axis in LocalAxes)
            {
                // The approach points toward the item; a positive world y would mean coming up from below the belt.
                float y = (itemRotation * axis).y;
                if (y >= 0.3f) continue;
                approaches.Add(axis);
                if (y < lowest) { lowest = y; fromAbove = axis; }
            }
            if (lowest < -0.7f && Random.value < guessFromAbove) return fromAbove;
            return approaches[Random.Range(0, approaches.Count)];
        }

        private static Vector3 RandomPerpendicular(Vector3 approach)
        {
            var perpendiculars = new List<Vector3>();
            foreach (Vector3 axis in LocalAxes)
            {
                if (Mathf.Abs(Vector3.Dot(axis, approach)) < 0.1f) perpendiculars.Add(axis);
            }
            return perpendiculars[Random.Range(0, perpendiculars.Count)];
        }

        /// <summary>A small random offset from the item's middle, across the approach direction only.</summary>
        private Vector3 Spread(Bounds bounds, Vector3 approach)
        {
            Vector3 offset = Vector3.Scale(bounds.extents * guessSpread,
                new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)));
            return offset - approach * Vector3.Dot(offset, approach);
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
