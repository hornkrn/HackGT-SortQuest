using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// A detailed look for the robot gripper, built at runtime: a housing with a slide rail, aluminum fingers
    /// with rubber pads, a wrist, and a status light (cyan idle, amber working, green success, red miss).
    /// Visual only: the grasp checks still use the GripperShape boxes, and the fingers here match them exactly.
    /// Put it on the RobotGripper object; the simple placeholder boxes are hidden at runtime.
    /// </summary>
    public class GripperVisual : MonoBehaviour
    {
        [Tooltip("Found on this object if left empty.")]
        [SerializeField] private RobotGripper robot;

        [Header("Materials")]
        [SerializeField] private Material bodyMaterial;
        [SerializeField] private Material metalMaterial;
        [SerializeField] private Material padMaterial;
        [SerializeField] private Material accentMaterial;
        [Tooltip("See-through material (Sprites/Default) for the status light.")]
        [SerializeField] private Material lightMaterial;

        [Tooltip("Simple placeholder parts to hide at runtime.")]
        [SerializeField] private Renderer[] hideAtRuntime;

        [Header("Status light colors")]
        [SerializeField] private Color idleColor = new Color(0.2f, 0.8f, 1f);
        [SerializeField] private Color busyColor = new Color(1f, 0.7f, 0.1f);
        [SerializeField] private Color successColor = new Color(0.2f, 1f, 0.3f);
        [SerializeField] private Color failColor = new Color(1f, 0.15f, 0.1f);

        /// <summary>Distance from the grasp point back to the top of the wrist, where an arm attaches.</summary>
        public float MountOffset => 0.185f;

        private const float PadThickness = 0.004f;
        private const float LinkThickness = 0.008f;

        private GripperShape shape;
        private Transform leftCarriage;
        private Transform rightCarriage;
        private Transform leftLink;
        private Transform rightLink;
        private Transform leftPad;
        private Transform rightPad;
        private Renderer statusLight;
        private bool busy;
        private Color flashColor;
        private float flashUntil;

        private void Awake()
        {
            if (robot == null) robot = GetComponent<RobotGripper>();
            shape = robot != null ? robot.Shape : new GripperShape();
            if (hideAtRuntime != null)
            {
                foreach (Renderer placeholder in hideAtRuntime)
                {
                    if (placeholder != null) placeholder.enabled = false;
                }
            }
            Build();
        }

        public void SetBusy(bool isBusy)
        {
            busy = isBusy;
        }

        public void Flash(bool success)
        {
            flashColor = success ? successColor : failColor;
            flashUntil = Time.time + 0.8f;
        }

        /// <summary>Moves the fingers; offsets are from the grasp point to each finger's inner face.</summary>
        public void Apply(float leftOffset, float rightOffset)
        {
            PlaceFinger(leftCarriage, leftLink, leftPad, -1f, leftOffset);
            PlaceFinger(rightCarriage, rightLink, rightPad, 1f, rightOffset);
        }

        private void Update()
        {
            if (statusLight == null)
            {
                return;
            }
            Color color = Time.time < flashUntil ? flashColor : busy ? busyColor : idleColor;
            VizUtil.SetColor(statusLight, color);
        }

        private void Build()
        {
            var root = new GameObject("GripperModel").transform;
            root.SetParent(transform, false);

            float tipZ = shape.fingertipPastGrasp;
            float palmFrontZ = tipZ - shape.fingerLength;
            float housingWidth = shape.maxOpening + 2f * shape.fingerThickness + 0.03f;

            // Housing that the fingers slide along, with a rail across its face and an accent stripe on top.
            Box(root, "Housing", bodyMaterial, new Vector3(0f, 0f, palmFrontZ - 0.02f), new Vector3(housingWidth, 0.055f, 0.04f));
            Box(root, "Rail", metalMaterial, new Vector3(0f, 0f, palmFrontZ - 0.002f), new Vector3(housingWidth - 0.01f, 0.014f, 0.004f));
            Box(root, "Stripe", accentMaterial, new Vector3(0f, 0.0285f, palmFrontZ - 0.02f), new Vector3(housingWidth + 0.002f, 0.003f, 0.02f));

            // Flange, status light ring, and wrist; cylinders turned so their axis runs along the gripper's Z.
            float flangeZ = palmFrontZ - 0.05f;
            Disc(root, "Flange", metalMaterial, flangeZ, 0.075f, 0.02f);
            statusLight = Disc(root, "StatusLight", lightMaterial, flangeZ - 0.012f, 0.08f, 0.004f);
            VizUtil.SetColor(statusLight, idleColor);
            Disc(root, "Wrist", bodyMaterial, flangeZ - 0.05f, 0.05f, 0.07f);
            Disc(root, "WristRing", accentMaterial, flangeZ - 0.05f, 0.054f, 0.006f);

            leftCarriage = Box(root, "LeftCarriage", bodyMaterial, Vector3.zero, new Vector3(0.024f, 0.04f, 0.02f)).transform;
            rightCarriage = Box(root, "RightCarriage", bodyMaterial, Vector3.zero, new Vector3(0.024f, 0.04f, 0.02f)).transform;
            var linkSize = new Vector3(LinkThickness, shape.fingerWidth, shape.fingerLength);
            leftLink = Box(root, "LeftFinger", metalMaterial, Vector3.zero, linkSize).transform;
            rightLink = Box(root, "RightFinger", metalMaterial, Vector3.zero, linkSize).transform;
            var padSize = new Vector3(PadThickness, shape.fingerWidth * 1.1f, shape.fingerLength * 0.55f);
            leftPad = Box(root, "LeftPad", padMaterial, Vector3.zero, padSize).transform;
            rightPad = Box(root, "RightPad", padMaterial, Vector3.zero, padSize).transform;
            Apply(shape.OpenOffset, shape.OpenOffset);
        }

        /// <summary>Pad on the inner face, aluminum link behind it; together they fill the finger's check box.</summary>
        private void PlaceFinger(Transform carriage, Transform link, Transform pad, float side, float offset)
        {
            float tipZ = shape.fingertipPastGrasp;
            pad.localPosition = new Vector3(side * (offset + PadThickness * 0.5f), 0f, tipZ - shape.fingerLength * 0.3f);
            link.localPosition = new Vector3(side * (offset + PadThickness + LinkThickness * 0.5f), 0f, tipZ - shape.fingerLength * 0.5f);
            carriage.localPosition = new Vector3(side * (offset + PadThickness + LinkThickness * 0.5f), 0f,
                tipZ - shape.fingerLength + 0.006f);
        }

        private static MeshRenderer Box(Transform parent, string partName, Material material, Vector3 position, Vector3 size)
        {
            MeshRenderer part = VizUtil.CreateShape(partName, parent, VizUtil.CubeMesh, material);
            part.transform.localPosition = position;
            part.transform.localScale = size;
            return part;
        }

        private static MeshRenderer Disc(Transform parent, string partName, Material material, float z, float diameter, float length)
        {
            MeshRenderer part = VizUtil.CreateShape(partName, parent, VizUtil.CylinderMesh, material);
            part.transform.localPosition = new Vector3(0f, 0f, z);
            part.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            part.transform.localScale = new Vector3(diameter, length * 0.5f, diameter);
            return part;
        }
    }
}
