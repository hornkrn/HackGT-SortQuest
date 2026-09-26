using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// A detailed look for the robot gripper, built at runtime for the selected gripper and rebuilt when it
    /// changes. Two-finger: a housing with a slide rail, aluminum fingers with rubber pads, and a wrist.
    /// Suction: a rubber cup on a stem. Both have a status light (cyan idle, amber working, green success, red miss)
    /// and an accent in the gripper's color. Visual only: the checks use GripperShape and SuctionShape, and the
    /// parts here match them. Put it on the RobotGripper object; the simple placeholder boxes are hidden at runtime.
    /// </summary>
    public class GripperVisual : MonoBehaviour
    {
        [Tooltip("Found on this object if left empty.")]
        [SerializeField] private RobotGripper robot;

        [Tooltip("Found automatically if left empty.")]
        [SerializeField] private GripperCatalog catalog;

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
        public float MountOffset => Profile.MountOffset;

        private const float PadThickness = 0.004f;
        private const float LinkThickness = 0.008f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private GripperProfile Profile => robot != null ? robot.Profile : GripperCatalog.CurrentOrStandard(catalog);

        private Transform model;
        private GripperProfile builtFor;
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
            if (catalog == null) catalog = FindAnyObjectByType<GripperCatalog>();
            if (hideAtRuntime != null)
            {
                foreach (Renderer placeholder in hideAtRuntime)
                {
                    if (placeholder != null) placeholder.enabled = false;
                }
            }
            Rebuild();
        }

        private void OnEnable()
        {
            if (catalog != null) catalog.Changed += HandleGripperChanged;
        }

        private void OnDisable()
        {
            if (catalog != null) catalog.Changed -= HandleGripperChanged;
        }

        private void HandleGripperChanged(GripperProfile profile)
        {
            Rebuild();
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
            if (leftLink == null)
            {
                return; // Suction cup: nothing moves.
            }
            PlaceFinger(leftCarriage, leftLink, leftPad, -1f, leftOffset);
            PlaceFinger(rightCarriage, rightLink, rightPad, 1f, rightOffset);
        }

        private void Update()
        {
            if (builtFor != Profile)
            {
                Rebuild();
            }
            if (statusLight == null)
            {
                return;
            }
            Color color = Time.time < flashUntil ? flashColor : busy ? busyColor : idleColor;
            VizUtil.SetColor(statusLight, color);
        }

        private void Rebuild()
        {
            if (model != null)
            {
                Destroy(model.gameObject);
            }
            leftCarriage = rightCarriage = leftLink = rightLink = leftPad = rightPad = null;

            GripperProfile profile = Profile;
            builtFor = profile;
            model = new GameObject("GripperModel").transform;
            model.SetParent(transform, false);
            if (profile.IsSuction)
            {
                BuildSuction(profile);
            }
            else
            {
                BuildParallel(profile);
            }
        }

        private void BuildParallel(GripperProfile profile)
        {
            GripperShape shape = profile.parallel;
            float tipZ = shape.fingertipPastGrasp;
            float palmFrontZ = tipZ - shape.fingerLength;
            float housingWidth = shape.maxOpening + 2f * shape.fingerThickness + 0.03f;

            // Housing that the fingers slide along, with a rail across its face and an accent stripe on top.
            Box("Housing", bodyMaterial, new Vector3(0f, 0f, palmFrontZ - 0.02f), new Vector3(housingWidth, 0.055f, 0.04f));
            Box("Rail", metalMaterial, new Vector3(0f, 0f, palmFrontZ - 0.002f), new Vector3(housingWidth - 0.01f, 0.014f, 0.004f));
            Accent(Box("Stripe", accentMaterial, new Vector3(0f, 0.0285f, palmFrontZ - 0.02f),
                new Vector3(housingWidth + 0.002f, 0.003f, 0.02f)), profile);

            // Flange, status light ring, and wrist; together they end at MountOffset behind the grasp point.
            float flangeZ = palmFrontZ - 0.05f;
            BuildWrist(profile, flangeZ);

            leftCarriage = Box("LeftCarriage", bodyMaterial, Vector3.zero, new Vector3(0.024f, 0.04f, 0.02f)).transform;
            rightCarriage = Box("RightCarriage", bodyMaterial, Vector3.zero, new Vector3(0.024f, 0.04f, 0.02f)).transform;
            var linkSize = new Vector3(LinkThickness, shape.fingerWidth, shape.fingerLength);
            leftLink = Box("LeftFinger", metalMaterial, Vector3.zero, linkSize).transform;
            rightLink = Box("RightFinger", metalMaterial, Vector3.zero, linkSize).transform;
            var padSize = new Vector3(PadThickness, shape.fingerWidth * 1.1f, shape.fingerLength * 0.55f);
            leftPad = Box("LeftPad", padMaterial, Vector3.zero, padSize).transform;
            rightPad = Box("RightPad", padMaterial, Vector3.zero, padSize).transform;
            Apply(shape.OpenOffset, shape.OpenOffset);
        }

        private void BuildSuction(GripperProfile profile)
        {
            SuctionShape cup = profile.suction;
            // Cup face at the grasp point (z = 0), rubber bellows, then a stem back to the flange and wrist.
            Disc("Cup", padMaterial, -0.004f, cup.cupDiameter, 0.008f);
            Disc("Bellows", padMaterial, -0.010f, cup.cupDiameter * 0.7f, 0.006f);
            float stemTop = -(0.012f + cup.stemLength);
            Disc("Stem", metalMaterial, (-0.012f + stemTop) * 0.5f, 0.024f, cup.stemLength);
            Accent(Disc("StemBand", accentMaterial, -0.012f - cup.stemLength * 0.3f, 0.028f, 0.01f), profile);
            BuildWrist(profile, stemTop - 0.01f);
        }

        /// <summary>Flange centered at flangeZ, status light, and wrist; the wrist top is 0.085 m behind flangeZ.</summary>
        private void BuildWrist(GripperProfile profile, float flangeZ)
        {
            Disc("Flange", metalMaterial, flangeZ, 0.075f, 0.02f);
            statusLight = Disc("StatusLight", lightMaterial, flangeZ - 0.012f, 0.08f, 0.004f);
            VizUtil.SetColor(statusLight, idleColor);
            Disc("Wrist", bodyMaterial, flangeZ - 0.05f, 0.05f, 0.07f);
            Accent(Disc("WristRing", accentMaterial, flangeZ - 0.05f, 0.054f, 0.006f), profile);
        }

        /// <summary>Pad on the inner face, aluminum link behind it; together they fill the finger's check box.</summary>
        private void PlaceFinger(Transform carriage, Transform link, Transform pad, float side, float offset)
        {
            GripperShape shape = builtFor.parallel;
            float tipZ = shape.fingertipPastGrasp;
            pad.localPosition = new Vector3(side * (offset + PadThickness * 0.5f), 0f, tipZ - shape.fingerLength * 0.3f);
            link.localPosition = new Vector3(side * (offset + PadThickness + LinkThickness * 0.5f), 0f, tipZ - shape.fingerLength * 0.5f);
            carriage.localPosition = new Vector3(side * (offset + PadThickness + LinkThickness * 0.5f), 0f,
                tipZ - shape.fingerLength + 0.006f);
        }

        private static void Accent(Renderer part, GripperProfile profile)
        {
            var block = new MaterialPropertyBlock();
            part.GetPropertyBlock(block);
            block.SetColor(BaseColorId, profile.accentColor);
            part.SetPropertyBlock(block);
        }

        private MeshRenderer Box(string partName, Material material, Vector3 position, Vector3 size)
        {
            MeshRenderer part = VizUtil.CreateShape(partName, model, VizUtil.CubeMesh, material);
            part.transform.localPosition = position;
            part.transform.localScale = size;
            return part;
        }

        private MeshRenderer Disc(string partName, Material material, float z, float diameter, float length)
        {
            MeshRenderer part = VizUtil.CreateShape(partName, model, VizUtil.CylinderMesh, material);
            part.transform.localPosition = new Vector3(0f, 0f, z);
            part.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            part.transform.localScale = new Vector3(diameter, length * 0.5f, diameter);
            return part;
        }
    }
}
