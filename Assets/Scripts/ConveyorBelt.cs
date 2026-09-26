using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// A straight conveyor from a start point to an end point. TrashItems read its velocity
    /// and move themselves along it.
    /// </summary>
    public class ConveyorBelt : MonoBehaviour
    {
        [Tooltip("Where items are placed. Put it on the top surface of the belt, at the upstream end.")]
        [SerializeField] private Transform startPoint;

        [Tooltip("Items that pass this point fall off the belt.")]
        [SerializeField] private Transform endPoint;

        [Tooltip("Belt speed in meters per second.")]
        [SerializeField] private float speed = 0.2f;

        [SerializeField] private bool running = true;

        [Header("Optional visuals")]
        [Tooltip("Renderer whose texture scrolls to show the belt moving. Leave empty to skip.")]
        [SerializeField] private Renderer beltRenderer;
        [SerializeField] private Vector2 textureScrollDirection = new Vector2(0f, 1f);
        [SerializeField] private float textureScrollScale = 1f;

        private Vector2 textureOffset;

        public Vector3 StartPosition => startPoint.position;
        public Vector3 Direction => (endPoint.position - startPoint.position).normalized;
        public Vector3 Velocity => running ? Direction * speed : Vector3.zero;

        public float Speed
        {
            get => speed;
            set => speed = value;
        }

        public bool Running
        {
            get => running;
            set => running = value;
        }

        public bool IsPastEnd(Vector3 position)
        {
            return Vector3.Dot(position - endPoint.position, Direction) > 0f;
        }

        private void Awake()
        {
            if (startPoint == null || endPoint == null)
            {
                Debug.LogError("[SortQuest] ConveyorBelt needs a Start Point and an End Point.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            if (beltRenderer == null || !running)
            {
                return;
            }
            textureOffset += textureScrollDirection * (speed * textureScrollScale * Time.deltaTime);
            beltRenderer.material.mainTextureOffset = textureOffset;
        }

        private void OnDrawGizmos()
        {
            if (startPoint == null || endPoint == null)
            {
                return;
            }
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(startPoint.position, endPoint.position);
            Gizmos.DrawWireSphere(startPoint.position, 0.03f);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(endPoint.position, 0.03f);
        }
    }
}
