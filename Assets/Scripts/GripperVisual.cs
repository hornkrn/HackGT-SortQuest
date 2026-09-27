using UnityEngine;
using System.Collections.Generic;

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

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private GripperProfile Profile => robot != null ? robot.Profile : GripperCatalog.CurrentOrStandard(catalog);
        private Transform model;
        private GripperProfile builtFor;
        private readonly List<GripperPart> parts = new List<GripperPart>();
        private readonly List<Transform> visuals = new List<Transform>();
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
            parts.Clear();
            Profile.Model.GetParts(parts, leftOffset, rightOffset);
            for (int i = 0; i < parts.Count && i < visuals.Count; i++) Place(visuals[i], parts[i]);
        }

        private static void Place(Transform target, GripperPart part)
        {
            target.localPosition = part.Center;
            target.localRotation = part.Cylinder ? Quaternion.Euler(90, 0, 0) : Quaternion.identity;
            target.localScale = part.Cylinder ? new Vector3(part.Size.x, part.Size.z * .5f, part.Size.y) : part.Size;
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
            visuals.Clear();
            var profile = Profile;
            builtFor = profile;
            model = new GameObject("GripperModel").transform;
            model.SetParent(transform, false);
            parts.Clear();
            profile.Model.GetParts(parts, profile.parallel.OpenOffset, profile.parallel.OpenOffset);
            Material[] materials = { bodyMaterial, metalMaterial, padMaterial, accentMaterial, lightMaterial };
            foreach (var part in parts)
            {
                var renderer = VizUtil.CreateShape(part.Name, model,
                    part.Cylinder ? VizUtil.CylinderMesh : VizUtil.CubeMesh, materials[part.Material]);
                Place(renderer.transform, part);
                visuals.Add(renderer.transform);
                if (part.Material == 3) Accent(renderer, profile);
                if (part.Material == 4) statusLight = renderer;
            }
        }

        private static void Accent(Renderer part, GripperProfile profile)
        {
            var block = new MaterialPropertyBlock();
            part.GetPropertyBlock(block);
            block.SetColor(BaseColorId, profile.accentColor);
            part.SetPropertyBlock(block);
        }

    }
}
