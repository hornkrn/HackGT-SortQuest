using System;
using System.Collections.Generic;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// Turns each human grab into a gripper grasp record. The grasp is captured when the grab starts,
    /// and the record is saved once the outcome is known: the item lands in a bin, is missed,
    /// or is grabbed again (the first grab then counts as not sorted).
    /// </summary>
    public class GraspRecorder : MonoBehaviour
    {
        [Tooltip("Found automatically if left empty.")]
        [SerializeField] private GraspDataset dataset;

        [Tooltip("Found automatically if left empty. New items from it are recorded.")]
        [SerializeField] private TrashSpawner spawner;

        [Tooltip("Optional overhead camera that saves what the robot would see at each grab. Found automatically if left empty.")]
        [SerializeField] private RobotCamera robotCamera;

        [SerializeField] private bool logRecords = true;

        [Header("Debug gizmo (Scene view)")]
        [SerializeField] private bool drawLastGrasp = true;

        /// <summary>Raised when a player's grab is converted to a gripper grasp (in the item's frame).</summary>
        public event Action<TrashItem, GripperGrasp> GraspCaptured;

        private readonly Dictionary<TrashItem, GraspRecord> pending = new Dictionary<TrashItem, GraspRecord>();
        private readonly List<HandGrabInteractor> handInteractors = new List<HandGrabInteractor>();
        private GripperGrasp lastWorldGrasp;
        private bool hasLastGrasp;

        private void Awake()
        {
            if (dataset == null)
            {
                dataset = FindAnyObjectByType<GraspDataset>();
            }
            if (spawner == null)
            {
                spawner = FindAnyObjectByType<TrashSpawner>();
            }
            if (robotCamera == null)
            {
                robotCamera = FindAnyObjectByType<RobotCamera>();
            }
            if (dataset == null)
            {
                Debug.LogError("[SortQuest] GraspRecorder needs a GraspDataset in the scene.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (spawner != null)
            {
                spawner.ItemSpawned += Track;
            }
        }

        private void OnDisable()
        {
            if (spawner != null)
            {
                spawner.ItemSpawned -= Track;
            }
        }

        private void Start()
        {
            // Items placed in the scene by hand, not spawned.
            foreach (TrashItem item in FindObjectsByType<TrashItem>(FindObjectsSortMode.None))
            {
                Track(item);
            }
        }

        public void Track(TrashItem item)
        {
            item.Grabbed -= HandleGrabbed;
            item.Released -= HandleReleased;
            item.Sorted -= HandleSorted;
            item.Missed -= HandleMissed;
            item.PickedByRobot -= HandlePickedByRobot;
            item.Grabbed += HandleGrabbed;
            item.Released += HandleReleased;
            item.Sorted += HandleSorted;
            item.Missed += HandleMissed;
            item.PickedByRobot += HandlePickedByRobot;
        }

        private void HandleGrabbed(TrashItem item)
        {
            // Grabbed again before landing anywhere: the earlier grab didn't sort it.
            if (pending.ContainsKey(item))
            {
                Finish(item, GraspRecord.NoBin, false, item.WasDropped);
            }

            IHand hand = FindHand(item.GrabberId, item.GrabberData);
            if (hand == null)
            {
                Debug.LogWarning($"[SortQuest] {item.ItemType} grabbed by a non-hand interactor; no grasp recorded.");
                return;
            }
            if (!HandGripperPose.TryCompute(hand, out GripperGrasp worldGrasp))
            {
                Debug.LogWarning($"[SortQuest] {item.ItemType} grabbed but hand joints weren't tracked; no grasp recorded.");
                return;
            }

            lastWorldGrasp = worldGrasp;
            hasLastGrasp = true;

            GripperGrasp local = HandGripperPose.ToLocal(worldGrasp, item.transform);
            string handName = hand.Handedness == Handedness.Left ? "left" : "right";
            GraspRecord record = GraspRecord.Create(dataset, GraspRecord.SourceHuman, item, local, handName);
            ImageData image = robotCamera != null ? robotCamera.Capture(item) : null;
            if (image != null)
            {
                record.image = image;
            }
            pending[item] = record;
            GraspCaptured?.Invoke(item, local);
        }

        private void HandleReleased(TrashItem item)
        {
            if (pending.TryGetValue(item, out GraspRecord record))
            {
                record.outcome.hold_s = GraspMath.Round(item.HoldSeconds, 2);
            }
        }

        private void HandleSorted(TrashItem item, SortingBin bin, bool correct)
        {
            Finish(item, TrashTypes.BinId(bin.BinType), correct, item.WasDropped);
        }

        private void HandleMissed(TrashItem item)
        {
            Finish(item, GraspRecord.NoBin, false, true);
        }

        // The robot picked up an item a person grabbed earlier: that person's grab didn't sort it.
        private void HandlePickedByRobot(TrashItem item)
        {
            Finish(item, GraspRecord.NoBin, false, item.WasDropped);
        }

        private void Finish(TrashItem item, string bin, bool correct, bool dropped)
        {
            if (!pending.TryGetValue(item, out GraspRecord record))
            {
                return;
            }
            pending.Remove(item);
            record.outcome.bin = bin;
            record.outcome.correct = correct;
            record.outcome.dropped = dropped;
            dataset.Add(record);

            if (logRecords)
            {
                Debug.Log($"[SortQuest] Grasp saved: {record.item_type} ({record.hand} hand, width {record.grasp.width_m * 100:F1} cm) " +
                          $"-> {bin}, {(record.IsGood ? "GOOD" : "not good")}. " +
                          $"{dataset.CountGood(item.ItemType)} good {record.item_type} grasps total.");
            }
        }

        /// <summary>Finds the hand behind a grab from the PointerEvent's interactor identifier.</summary>
        private IHand FindHand(int interactorId, object grabberData)
        {
            if (grabberData is HandGrabInteractor direct && direct.Identifier == interactorId)
            {
                return direct.Hand;
            }
            IHand hand = SearchHandInteractors(interactorId);
            if (hand == null)
            {
                // Interactors may have been created after the last search.
                handInteractors.Clear();
                handInteractors.AddRange(FindObjectsByType<HandGrabInteractor>(FindObjectsSortMode.None));
                hand = SearchHandInteractors(interactorId);
            }
            return hand;
        }

        private IHand SearchHandInteractors(int interactorId)
        {
            foreach (HandGrabInteractor interactor in handInteractors)
            {
                if (interactor != null && interactor.Identifier == interactorId)
                {
                    return interactor.Hand;
                }
            }
            return null;
        }

        private void OnDrawGizmos()
        {
            if (!drawLastGrasp || !hasLastGrasp)
            {
                return;
            }
            // Yellow line = closing axis (finger to finger), blue line = approach direction.
            Vector3 half = lastWorldGrasp.ClosingAxis * (lastWorldGrasp.Width * 0.5f);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(lastWorldGrasp.Position - half, lastWorldGrasp.Position + half);
            Gizmos.DrawWireSphere(lastWorldGrasp.Position, 0.005f);
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(lastWorldGrasp.Position - lastWorldGrasp.Approach * 0.08f, lastWorldGrasp.Position);
        }
    }
}
