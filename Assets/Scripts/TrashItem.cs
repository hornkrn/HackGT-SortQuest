using System;
using Oculus.Interaction;
using UnityEngine;

namespace SortQuest
{
    public enum TrashItemState
    {
        OnBelt,  // kinematic, carried by the conveyor
        Held,    // grabbed by a hand (the Grabbable keeps it kinematic)
        Loose,   // released or fallen, dynamic physics
        Sorted,  // landed in a bin, about to despawn
        Missed   // fell off the belt or hit the floor, despawned
    }

    /// <summary>
    /// A piece of trash. Rides the conveyor kinematically, becomes dynamic when released,
    /// and reports grabs, releases, sorts, and misses through events.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class TrashItem : MonoBehaviour
    {
        [SerializeField] private ItemType itemType;

        [Tooltip("The Grabbable from the Grab Interaction setup. Found in children if left empty.")]
        [SerializeField] private Grabbable grabbable;

        [Tooltip("Seconds an item stays on the floor before it despawns and counts as missed.")]
        [SerializeField] private float despawnAfterDropSeconds = 3f;

        [Tooltip("Seconds an item stays in a bin before it despawns.")]
        [SerializeField] private float despawnAfterSortSeconds = 1.5f;

        [Tooltip("Items that fall below this height count as missed.")]
        [SerializeField] private float despawnBelowY = -5f;

        public event Action<TrashItem> Grabbed;
        public event Action<TrashItem> Released;
        public event Action<TrashItem, SortingBin, bool> Sorted;
        public event Action<TrashItem> Missed;

        public ItemType ItemType => itemType;
        public BinType CorrectBin => TrashTypes.CorrectBin(itemType);
        public TrashItemState State { get; private set; } = TrashItemState.OnBelt;
        public Rigidbody Body => body;
        public Grabbable Grabbable => grabbable;

        /// <summary>True if the item touched anything other than a bin after its last release.</summary>
        public bool WasDropped { get; private set; }

        /// <summary>How long the last grab lasted, in seconds.</summary>
        public float HoldSeconds { get; private set; }

        private Rigidbody body;
        private ConveyorBelt belt;
        private float grabTime;
        private float dropTime;
        private bool fellOffEnd;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.Interpolate;
            // The SDK's "Add Grab Interaction" wizard creates the Rigidbody with gravity off.
            body.useGravity = true;
            if (grabbable == null)
            {
                grabbable = GetComponentInChildren<Grabbable>();
            }
            if (grabbable == null)
            {
                Debug.LogWarning($"[SortQuest] {name} has no Grabbable, so it can't be picked up.", this);
            }
        }

        private void OnEnable()
        {
            // Subscribing here (before Grabbable.Start) means our Unselect handler runs before the SDK's
            // throw handler, so the body is already dynamic when the throw velocity is applied.
            if (grabbable != null)
            {
                grabbable.WhenPointerEventRaised += HandlePointerEvent;
            }
        }

        private void OnDisable()
        {
            if (grabbable != null)
            {
                grabbable.WhenPointerEventRaised -= HandlePointerEvent;
            }
        }

        private void Start()
        {
            // Items placed by hand in the scene (not spawned) just fall until they land on a belt.
            if (belt == null && State == TrashItemState.OnBelt)
            {
                State = TrashItemState.Loose;
                body.isKinematic = false;
            }
        }

        /// <summary>Called by the spawner right after the item is created.</summary>
        public void PlaceOnBelt(ConveyorBelt newBelt)
        {
            belt = newBelt;
            EnterBelt();
        }

        private void FixedUpdate()
        {
            if (State == TrashItemState.OnBelt && belt != null)
            {
                Vector3 next = body.position + belt.Velocity * Time.fixedDeltaTime;
                if (belt.IsPastEnd(next))
                {
                    FallOffBelt();
                }
                else
                {
                    body.MovePosition(next);
                }
            }
            else if (State == TrashItemState.Loose && WasDropped && Time.time - dropTime > despawnAfterDropSeconds)
            {
                Miss();
            }

            if (body.position.y < despawnBelowY)
            {
                Miss();
            }
        }

        private void HandlePointerEvent(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Select:
                    if (State == TrashItemState.OnBelt || State == TrashItemState.Loose)
                    {
                        BeginHold();
                    }
                    break;
                case PointerEventType.Unselect:
                case PointerEventType.Cancel:
                    // With two hands on the item, wait until the last one lets go.
                    if (State == TrashItemState.Held && grabbable.SelectingPointsCount == 0)
                    {
                        EndHold();
                    }
                    break;
            }
        }

        private void BeginHold()
        {
            State = TrashItemState.Held;
            grabTime = Time.time;
            WasDropped = false;
            fellOffEnd = false;
            Grabbed?.Invoke(this);
        }

        private void EndHold()
        {
            HoldSeconds = Time.time - grabTime;
            State = TrashItemState.Loose;
            // The Grabbable restores the kinematic flag it saw at grab time (true if taken off the belt),
            // so switch to dynamic physics here.
            body.isKinematic = false;
            Released?.Invoke(this);
        }

        private void EnterBelt()
        {
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
            State = TrashItemState.OnBelt;
        }

        private void FallOffBelt()
        {
            fellOffEnd = true;
            State = TrashItemState.Loose;
            body.isKinematic = false;
            body.linearVelocity = belt.Velocity;
            MarkDropped();
        }

        private void MarkDropped()
        {
            if (!WasDropped)
            {
                WasDropped = true;
                dropTime = Time.time;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (State != TrashItemState.Loose)
            {
                return;
            }

            // Bin walls and other trash don't count as a drop.
            if (collision.collider.GetComponentInParent<SortingBin>() != null)
            {
                return;
            }
            if (collision.rigidbody != null && collision.rigidbody.GetComponent<TrashItem>() != null)
            {
                return;
            }

            // Landing back on the belt: counts as a drop, but the item rides on.
            ConveyorBelt hitBelt = collision.collider.GetComponentInParent<ConveyorBelt>();
            if (hitBelt != null && !fellOffEnd)
            {
                MarkDropped();
                belt = hitBelt;
                EnterBelt();
                return;
            }

            MarkDropped();
        }

        /// <summary>Called by a SortingBin when the item lands in it.</summary>
        public void MarkSorted(SortingBin bin, bool correct)
        {
            if (State == TrashItemState.Sorted || State == TrashItemState.Missed)
            {
                return;
            }
            State = TrashItemState.Sorted;
            Sorted?.Invoke(this, bin, correct);
            Destroy(gameObject, despawnAfterSortSeconds);
        }

        private void Miss()
        {
            if (State == TrashItemState.Sorted || State == TrashItemState.Missed)
            {
                return;
            }
            State = TrashItemState.Missed;
            Missed?.Invoke(this);
            Destroy(gameObject);
        }
    }
}
