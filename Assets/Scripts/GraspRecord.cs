using System;
using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// One recorded grasp, in the JSON format from CLAUDE.md. Field names are snake_case to match the JSON.
    /// Vectors are [x, y, z] and rotations are quaternions [x, y, z, w].
    /// Numbers are doubles so JsonUtility writes them cleanly (0.0608, not 0.06080000102519989).
    /// </summary>
    [Serializable]
    public class GraspRecord
    {
        public const string SourceHuman = "human";
        public const string SourceAugmented = "augmented";
        public const string SourceRobot = "robot";
        public const string NoBin = "none";

        public string session_id;
        public string player;
        public string timestamp;
        public string source;
        public string item_type;
        public string correct_bin;
        public GraspData grasp = new GraspData();
        public PoseData item_pose_world = new PoseData();
        public OutcomeData outcome = new OutcomeData();
        public string hand;

        /// <summary>Good data: landed in the correct bin without being dropped.</summary>
        public bool IsGood => outcome != null && outcome.correct && !outcome.dropped;

        public GripperGrasp LocalGrasp => new GripperGrasp
        {
            Position = grasp.PosLocal,
            Rotation = grasp.RotLocal,
            Width = (float)grasp.width_m
        };
    }

    [Serializable]
    public class GraspData
    {
        public double[] pos_local = new double[3];
        public double[] rot_local = { 0, 0, 0, 1 };
        public double width_m;

        public Vector3 PosLocal
        {
            get => GraspMath.ToVector3(pos_local);
            set => pos_local = GraspMath.FromVector3(value);
        }

        public Quaternion RotLocal
        {
            get => GraspMath.ToQuaternion(rot_local);
            set => rot_local = GraspMath.FromQuaternion(value);
        }
    }

    [Serializable]
    public class PoseData
    {
        public double[] pos = new double[3];
        public double[] rot = { 0, 0, 0, 1 };
    }

    [Serializable]
    public class OutcomeData
    {
        public string bin = GraspRecord.NoBin;
        public bool correct;
        public bool dropped;
        public double hold_s;
    }

    public static class GraspMath
    {
        public static double[] FromVector3(Vector3 v)
        {
            return new[] { Round(v.x), Round(v.y), Round(v.z) };
        }

        public static double[] FromQuaternion(Quaternion q)
        {
            return new[] { Round(q.x), Round(q.y), Round(q.z), Round(q.w) };
        }

        public static Vector3 ToVector3(double[] a)
        {
            return a != null && a.Length >= 3 ? new Vector3((float)a[0], (float)a[1], (float)a[2]) : Vector3.zero;
        }

        public static Quaternion ToQuaternion(double[] a)
        {
            return a != null && a.Length >= 4
                ? new Quaternion((float)a[0], (float)a[1], (float)a[2], (float)a[3]).normalized
                : Quaternion.identity;
        }

        // Keeps the JSON readable; 0.1 mm and 1e-4 rotation precision is plenty.
        public static double Round(double value, int decimals = 4)
        {
            return Math.Round(value, decimals);
        }
    }
}
