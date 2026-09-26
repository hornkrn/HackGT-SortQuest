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

        [Header("Release")]
        [Tooltip("Caps the throw speed (m/s) applied on release, so jittery hand motion doesn't fling items.")]
        [SerializeField] private float maxReleaseSpeed = 1.5f;

        [Tooltip("Caps the spin (rad/s) applied on release.")]
        [SerializeField] private float maxReleaseSpin = 10f;

        [Tooltip("Caps how fast physics pushes overlapping objects apart (Unity's default is 10 m/s).")]
        [SerializeField] private float maxDepenetrationSpeed = 1f;

        [Tooltip("Under the Meta XR Simulator, released items just drop: its hands swing with the view and fling items.")]
        [SerializeField] private bool noThrowInSimulator = true;

        public event Action<TrashItem> Grabbed;
        public event Action<TrashItem> Released;
        public event Action<TrashItem, SortingBin, bool> Sorted;
        public event Action<TrashItem> Missed;
        public event Action<TrashItem> PickedByRobot;

        public ItemType ItemType => itemType;
        public BinType CorrectBin => TrashTypes.CorrectBin(itemType);
        public TrashItemState State { get; private set; } = TrashItemState.OnBelt;
        public Rigidbody Body => body;
        public Grabbable Grabbable => grabbable;

        /// <summary>True if the item touched anything other than a bin after its last release.</summary>
        public bool WasDropped { get; private set; }

        /// <summary>How long the last grab lasted, in seconds.</summary>
        public float HoldSeconds { get; private set; }

        /// <summary>Identifier of the interactor that started the current grab (PointerEvent.Identifier).</summary>
        public int GrabberId { get; private set; }

        /// <summary>PointerEvent.Data of the grab; by default the interactor itself.</summary>
        public object GrabberData { get; private set; }

        /// <summary>True if the robot, not a person, was the last to pick this item up.</summary>
        public bool LastHeldByRobot { get; private set; }

        private Rigidbody body;
        private ConveyorBelt belt;
        private float grabTime;
        private float dropTime;
        private bool fellOffEnd;
        private bool clampAfterRelease;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.Interpolate;
            // The SDK's "Add Grab Interaction" wizard creates the Rigidbody with gravity off.
            body.useGravity = true;
            body.maxDepenetrationVelocity = maxDepenetrationSpeed;
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
            IgnorePlayerCollisions();

            // Items placed by hand in the scene (not spawned) just fall until they land on a belt.
            if (belt == null && State == TrashItemState.OnBelt)
            {
                State = TrashItemState.Loose;
                body.isKinematic = false;
            }
        }

        private static bool IsSimulatorRuntime()
        {
            return UnityEngine.XR.OpenXR.OpenXRRuntime.name.Contains("Simulator");
        }

        // The camera rig's locomotion adds a body capsule tagged "Player"; trash dropped near
        // the player would otherwise bounce off it.
        private void IgnorePlayerCollisions()
        {
            Collider[] ownColliders = GetComponentsInChildren<Collider>();
            foreach (GameObject player in GameObject.FindGameObjectsWithTag("Player"))
            {
                foreach (Collider playerCollider in player.GetComponentsInChildren<Collider>())
                {
                    foreach (Collider own in ownColliders)
                    {
                        Physics.IgnoreCollision(own, playerCollider);
                    }
                }
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
            // The SDK applies its throw velocity right after our release handler, so cap it on the next step.
            if (clampAfterRelease)
            {
                clampAfterRelease = false;
                if (State == TrashItemState.Loose && !body.isKinematic)
                {
                    if (noThrowInSimulator && IsSimulatorRuntime())
                    {
                        body.linearVelocity = Vector3.zero;
                        body.angularVelocity = Vector3.zero;
                    }
                    else
                    {
                        body.linearVelocity = Vector3.ClampMagnitude(body.linearVelocity, maxReleaseSpeed);
                        body.angularVelocity = Vector3.ClampMagnitude(body.angularVelocity, maxReleaseSpin);
                    }
                }
            }

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
                        BeginHold(evt);
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

        private void BeginHold(PointerEvent evt)
        {
            State = TrashItemState.Held;
            GrabberId = evt.Identifier;
            GrabberData = evt.Data;
            LastHeldByRobot = false;
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
            clampAfterRelease = true;
            Released?.Invoke(this);
        }

        /// <summary>Called by the RobotGripper when its fingers close on the item; it then holds it with a joint.</summary>
        public void BeginRobotHold()
        {
            if (State != TrashItemState.OnBelt && State != TrashItemState.Loose)
            {
                return;
            }
            State = TrashItemState.Held;
            LastHeldByRobot = true;
            WasDropped = false;
            fellOffEnd = false;
            body.isKinematic = false;
            PickedByRobot?.Invoke(this);
        }

        /// <summary>Called by the RobotGripper when it lets go.</summary>
        public void EndRobotHold()
        {
            if (State == TrashItemState.Held && LastHeldByRobot)
            {
                State = TrashItemState.Loose;
            }
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
            HandleContact(collision, true);
        }

        // Also checked while touching, so an item that slides from the belt's edge onto its top gets picked up.
        private void OnCollisionStay(Collision collision)
        {
            HandleContact(collision, false);
        }

        private void HandleContact(Collision collision, bool isNewContact)
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

            // Landing on top of the belt: counts as a drop, but the item rides on.
            // Touching the belt's side doesn't count as being on it.
            ConveyorBelt hitBelt = collision.collider.GetComponentInParent<ConveyorBelt>();
            if (hitBelt != null && !fellOffEnd && IsRestingOnTop(collision))
            {
                MarkDropped();
                belt = hitBelt;
                EnterBelt();
                return;
            }

            if (isNewContact)
            {
                MarkDropped();
            }
        }

        private bool IsRestingOnTop(Collision collision)
        {
            // Center must be over the surface, not hanging past its edge.
            Bounds surface = collision.collider.bounds;
            Vector3 center = body.worldCenterOfMass;
            if (center.x < surface.min.x || center.x > surface.max.x ||
                center.z < surface.min.z || center.z > surface.max.z)
            {
                return false;
            }

            // At least one contact must push up, meaning the item sits on the top face.
            for (int i = 0; i < collision.contactCount; i++)
            {
                if (collision.GetContact(i).normal.y > 0.7f)
                {
                    return true;
                }
            }
            return false;
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
