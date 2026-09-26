using TMPro;
using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// A screen showing what the robot camera sees: the live color view, plus the depth image and item mask
    /// from the most recent grasp capture.
    /// </summary>
    public class RobotVisionScreen : MonoBehaviour
    {
        [Tooltip("Found automatically if left empty.")]
        [SerializeField] private RobotCamera robotCamera;

        [Tooltip("See-through material (Sprites/Default). Created if left empty.")]
        [SerializeField] private Material material;

        [Tooltip("Width and height of the live view, in meters.")]
        [SerializeField] private float size = 0.45f;

        private TextMeshPro caption;

        private void Awake()
        {
            if (robotCamera == null) robotCamera = FindAnyObjectByType<RobotCamera>();
            if (material == null) material = VizUtil.FallbackMaterial();
        }

        private void OnEnable()
        {
            if (robotCamera != null) robotCamera.Captured += HandleCaptured;
        }

        private void OnDisable()
        {
            if (robotCamera != null) robotCamera.Captured -= HandleCaptured;
        }

        // Built in Start so the camera's textures exist (they are created in its Awake).
        private void Start()
        {
            if (robotCamera == null)
            {
                return;
            }

            VizUtil.CreateText("Title", transform, new Vector3(0f, size * 0.5f + 0.06f, 0f),
                new Vector2(size + 0.3f, 0.1f), 0.45f, TextAlignmentOptions.Center).text = "Robot camera";

            AddImage("Live", robotCamera.ColorTexture, Vector3.zero, size);

            float small = size * 0.48f;
            float row = -size * 0.5f - small * 0.5f - 0.02f;
            AddImage("Depth", robotCamera.DepthPreview, new Vector3(-size * 0.26f, row, 0f), small);
            AddImage("Mask", robotCamera.MaskPreview, new Vector3(size * 0.26f, row, 0f), small);

            float labelY = row - small * 0.5f - 0.025f;
            VizUtil.CreateText("DepthLabel", transform, new Vector3(-size * 0.26f, labelY, 0f),
                new Vector2(small, 0.05f), 0.25f, TextAlignmentOptions.Center).text = "depth at last grasp";
            VizUtil.CreateText("MaskLabel", transform, new Vector3(size * 0.26f, labelY, 0f),
                new Vector2(small, 0.05f), 0.25f, TextAlignmentOptions.Center).text = "grasped item mask";

            caption = VizUtil.CreateText("Caption", transform, new Vector3(0f, labelY - 0.06f, 0f),
                new Vector2(size + 0.3f, 0.06f), 0.28f, TextAlignmentOptions.Center);
            UpdateCaption();
        }

        private void AddImage(string imageName, Texture texture, Vector3 localPosition, float width)
        {
            MeshRenderer quad = VizUtil.CreateShape(imageName, transform, VizUtil.QuadMesh, material, Color.white);
            quad.transform.localPosition = localPosition;
            quad.transform.localScale = new Vector3(width, width, 1f);
            VizUtil.SetTexture(quad, texture);
        }

        private void HandleCaptured(ImageData image)
        {
            UpdateCaption();
        }

        private void UpdateCaption()
        {
            if (caption != null)
            {
                caption.text = $"{robotCamera.ImagesSaved} grasp images saved this session";
            }
        }
    }
}
