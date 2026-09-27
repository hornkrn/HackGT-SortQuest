using System;
using System.Globalization;
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

        /// <summary>Current record format. 1 = before gripper, input_device, and schema_version existed.</summary>
        public const int CurrentSchemaVersion = 3;

        // input_device values: how the demonstration was made.
        public const string InputHands = "hands";                 // real hand tracking
        public const string InputControllers = "controllers";     // hand pose driven by Touch controllers
        public const string InputSimulator = "simulator";         // Meta XR Simulator's synthetic hands
        public const string InputUnknown = "unknown";              // records saved before this field existed
        public const string InputNone = "none";                    // robot attempts

        /// <summary>Unique id, set once when the record is created, so a server can ignore duplicate uploads.</summary>
        public string record_id;
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

        /// <summary>Gripper id this grasp is for (see GripperCatalog), for example "parallel_100mm".</summary>
        public string gripper;

        /// <summary>How the demonstration was made: hands, controllers, simulator, unknown, or none (robot).</summary>
        public string input_device;

        /// <summary>Record format version; 0 means loaded from an older file and not yet migrated.</summary>
        public int schema_version;
        public int checker_version = 1;
        public bool? feasible;
        public string parent_record_id;
        public bool? hand_penetration;

        /// <summary>What the robot camera saw at the moment of the grasp. Empty id if no image was taken.</summary>
        public ImageData image = new ImageData();

        /// <summary>Good data: landed in the correct bin without being dropped.</summary>
        public bool IsGood => outcome != null && outcome.correct && !outcome.dropped;

        /// <summary>A new record for a grasp on an item, stored in the item's frame. The outcome is filled in later.</summary>
        public static GraspRecord Create(GraspDataset dataset, string source, TrashItem item, GripperGrasp localGrasp, string hand,
            string gripperId, string inputDevice)
        {
            Transform itemTransform = item.transform;
            var record = new GraspRecord
            {
                record_id = NewId(),
                session_id = dataset.SessionId,
                player = source == SourceRobot ? "robot" : dataset.PlayerId,
                timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                source = source,
                item_type = TrashTypes.ItemId(item.ItemType),
                correct_bin = TrashTypes.BinId(item.CorrectBin),
                hand = hand,
                gripper = gripperId,
                input_device = inputDevice,
                schema_version = CurrentSchemaVersion
            };
            record.grasp.PosLocal = localGrasp.Position;
            record.grasp.RotLocal = localGrasp.Rotation;
            record.grasp.width_m = GraspMath.Round(localGrasp.Width);
            record.item_pose_world.pos = GraspMath.FromVector3(itemTransform.position);
            record.item_pose_world.rot = GraspMath.FromQuaternion(itemTransform.rotation);
            return record;
        }

        /// <summary>
        /// A checked variation of a good grasp. It keeps the original's item, pose, and outcome,
        /// with a new grasp and source "augmented".
        /// </summary>
        public static GraspRecord CreateAugmented(GraspRecord original, GripperGrasp localGrasp, string gripperId)
        {
            var record = new GraspRecord
            {
                record_id = NewId(),
                session_id = original.session_id,
                player = original.player,
                timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                source = SourceAugmented,
                item_type = original.item_type,
                correct_bin = original.correct_bin,
                hand = original.hand,
                gripper = gripperId,
                input_device = original.input_device,
                schema_version = CurrentSchemaVersion,
                checker_version = GripperCollision.Version,
                feasible = true,
                parent_record_id = original.record_id,
                item_pose_world = original.item_pose_world,
                image = original.image, // Same scene and item pose, so the original's images apply.
                outcome = new OutcomeData
                {
                    bin = original.outcome.bin,
                    correct = original.outcome.correct,
                    dropped = original.outcome.dropped
                }
            };
            record.grasp.PosLocal = localGrasp.Position;
            record.grasp.RotLocal = localGrasp.Rotation;
            record.grasp.width_m = GraspMath.Round(localGrasp.Width);
            return record;
        }

        /// <summary>
        /// Fills fields that older files lack. Returns true if anything changed. Everything recorded before
        /// grippers existed was for the standard gripper, and the input device wasn't known.
        /// </summary>
        public bool MigrateToCurrentSchema()
        {
            bool changed = false;
            if (string.IsNullOrEmpty(record_id)) { record_id = NewId(); changed = true; }
            if (string.IsNullOrEmpty(gripper)) { gripper = GripperCatalog.StandardId; changed = true; }
            if (string.IsNullOrEmpty(input_device))
            {
                input_device = source == SourceRobot ? InputNone : InputUnknown;
                changed = true;
            }
            if (checker_version <= 0) { checker_version = 1; changed = true; }
            // schema_version keeps the format the record was created in; only fill it when missing.
            if (schema_version <= 0) { schema_version = 1; changed = true; }
            if (outcome != null)
            {
                if (string.IsNullOrEmpty(outcome.failure_reason))
                {
                    outcome.failure_reason = "none";
                    changed = true;
                }
                // Robot failures from before failure reasons existed: the reason was never saved.
                if (source == SourceRobot && checker_version == 1 && !outcome.correct && outcome.failure_reason == "none")
                {
                    outcome.failure_reason = "unknown";
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>A new unique record id (32 hex characters).</summary>
        public static string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }

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

    /// <summary>
    /// Robot camera images for a grasp, saved as files named by id in persistentDataPath/images:
    /// {id}_rgb.png (color), {id}_depth.exr (depth in meters along the camera's view axis, 0 = nothing),
    /// and {id}_mask.png (0 nothing, 85 scene, 170 the grasped item, 255 other trash).
    /// </summary>
    [Serializable]
    public class ImageData
    {
        public string id = "";
        public double[] cam_pos = new double[3];
        public double[] cam_rot = { 0, 0, 0, 1 };
        public double fov_y_deg;
        public int rgb_size;
        public int depth_size;
    }

    [Serializable]
    public class OutcomeData
    {
        public string bin = GraspRecord.NoBin;
        public bool correct;
        public bool dropped;
        public double hold_s;
        public string failure_reason = "none";
        public string stage;
        public bool? grasp_success;
        public bool? motion_success;
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
