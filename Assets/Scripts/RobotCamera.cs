using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace SortQuest
{
    /// <summary>
    /// A fixed overhead camera above the belt: what a real sorting robot would see.
    /// It renders a live color view, and at the moment of each grasp it saves a color image, a depth image,
    /// and a mask of which pixels belong to the grasped item, in persistentDataPath/images, named by an image id
    /// stored in the grasp record. Depth and mask come from physics raycasts against colliders, so they show
    /// the trash and the belt but never the player's hands. Saving happens off the main thread.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class RobotCamera : MonoBehaviour
    {
        public const byte MaskNothing = 0;
        public const byte MaskScene = 85;
        public const byte MaskTarget = 170;
        public const byte MaskOtherTrash = 255;

        [Tooltip("Width and height of the color image, in pixels.")]
        [SerializeField] private int colorSize = 256;

        [Tooltip("Width and height of the depth image and mask, in pixels (one raycast each).")]
        [SerializeField] private int depthSize = 128;

        [Tooltip("Depth beyond this many meters counts as nothing.")]
        [SerializeField] private float maxDepth = 5f;

        [SerializeField] private LayerMask depthLayers = ~0;
        [SerializeField] private bool saveImages = true;

        [Header("Visible camera body (optional)")]
        [SerializeField] private Material housingMaterial;
        [Tooltip("See-through material (Sprites/Default) for the light that blinks on each capture.")]
        [SerializeField] private Material lightMaterial;

        public event Action<ImageData> Captured;

        public RenderTexture ColorTexture { get; private set; }
        public Texture2D DepthPreview { get; private set; }
        public Texture2D MaskPreview { get; private set; }
        public int ImagesSaved { get; private set; }

        private Camera cam;
        private string imageFolder;
        private Vector3[] rayDirections;
        private float[] depth;
        private byte[] mask;
        private Color32[] previewPixels;
        private readonly Dictionary<Collider, byte> classCache = new Dictionary<Collider, byte>();
        private Renderer captureLight;
        private float lightOffTime;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            ColorTexture = new RenderTexture(colorSize, colorSize, 24, RenderTextureFormat.ARGB32) { name = "RobotCameraColor" };
            cam.targetTexture = ColorTexture;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.aspect = 1f;
            cam.enabled = true;

            DepthPreview = NewPreview();
            MaskPreview = NewPreview();
            depth = new float[depthSize * depthSize];
            mask = new byte[depthSize * depthSize];
            previewPixels = new Color32[depthSize * depthSize];
            PrecomputeRays();

            imageFolder = Path.Combine(Application.persistentDataPath, "images");
            try
            {
                Directory.CreateDirectory(imageFolder);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SortQuest] Can't create {imageFolder}, so images won't be saved: {e.Message}");
                saveImages = false;
            }
            BuildHousing();
        }

        private void OnDestroy()
        {
            if (ColorTexture != null)
            {
                cam.targetTexture = null;
                ColorTexture.Release();
                Destroy(ColorTexture);
            }
        }

        private void Update()
        {
            if (captureLight != null)
            {
                VizUtil.SetColor(captureLight, Time.time < lightOffTime ? new Color(1f, 0.15f, 0.1f) : new Color(0.25f, 0.05f, 0.05f));
            }
        }

        /// <summary>
        /// Takes and saves the robot's view with this item as the target. Returns null if the item isn't in view.
        /// </summary>
        public ImageData Capture(TrashItem target)
        {
            if (!isActiveAndEnabled || target == null)
            {
                return null;
            }
            if (RenderDepthAndMask(target) == 0)
            {
                return null;
            }

            var data = new ImageData
            {
                id = GraspRecord.NewId(),
                cam_pos = GraspMath.FromVector3(transform.position),
                cam_rot = GraspMath.FromQuaternion(transform.rotation),
                fov_y_deg = GraspMath.Round(cam.fieldOfView, 2),
                rgb_size = colorSize,
                depth_size = depthSize
            };
            UpdatePreviews();
            if (saveImages)
            {
                SaveFiles(data.id);
            }
            ImagesSaved++;
            lightOffTime = Time.time + 0.25f;
            Captured?.Invoke(data);
            return data;
        }

        // ---------- Depth and mask ----------

        private void PrecomputeRays()
        {
            rayDirections = new Vector3[depthSize * depthSize];
            float tan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            for (int v = 0; v < depthSize; v++)
            {
                for (int u = 0; u < depthSize; u++)
                {
                    float x = ((u + 0.5f) / depthSize * 2f - 1f) * tan;
                    float y = ((v + 0.5f) / depthSize * 2f - 1f) * tan;
                    rayDirections[v * depthSize + u] = new Vector3(x, y, 1f).normalized;
                }
            }
        }

        /// <summary>One raycast per pixel, run as a batch job. Returns how many pixels hit the target item.</summary>
        private int RenderDepthAndMask(TrashItem target)
        {
            int count = rayDirections.Length;
            var commands = new NativeArray<RaycastCommand>(count, Allocator.TempJob);
            var hits = new NativeArray<RaycastHit>(count, Allocator.TempJob);
            Vector3 origin = transform.position;
            Quaternion rotation = transform.rotation;
            var query = new QueryParameters(depthLayers, false, QueryTriggerInteraction.Ignore, false);
            for (int i = 0; i < count; i++)
            {
                commands[i] = new RaycastCommand(origin, rotation * rayDirections[i], query, maxDepth);
            }
            RaycastCommand.ScheduleBatch(commands, hits, 64, 1).Complete();

            classCache.Clear();
            int targetPixels = 0;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                Collider col = hit.collider;
                if (col == null)
                {
                    depth[i] = 0f;
                    mask[i] = MaskNothing;
                    continue;
                }
                // Distance along the camera's view axis, as depth cameras report it.
                depth[i] = hit.distance * rayDirections[i].z;
                mask[i] = Classify(col, target);
                if (mask[i] == MaskTarget)
                {
                    targetPixels++;
                }
            }
            commands.Dispose();
            hits.Dispose();
            return targetPixels;
        }

        private byte Classify(Collider col, TrashItem target)
        {
            if (classCache.TryGetValue(col, out byte value))
            {
                return value;
            }
            Rigidbody body = col.attachedRigidbody;
            TrashItem item = body != null ? body.GetComponent<TrashItem>() : null;
            value = item == null ? MaskScene : item == target ? MaskTarget : MaskOtherTrash;
            classCache[col] = value;
            return value;
        }

        // ---------- Previews for the in-game screen ----------

        private Texture2D NewPreview()
        {
            return new Texture2D(depthSize, depthSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        private void UpdatePreviews()
        {
            float near = float.MaxValue;
            float far = 0f;
            foreach (float d in depth)
            {
                if (d > 0f)
                {
                    near = Mathf.Min(near, d);
                    far = Mathf.Max(far, d);
                }
            }
            float range = Mathf.Max(0.01f, far - near);
            for (int i = 0; i < depth.Length; i++)
            {
                // Closer is brighter.
                byte gray = depth[i] > 0f ? (byte)(255f * (1f - 0.8f * (depth[i] - near) / range)) : (byte)0;
                previewPixels[i] = new Color32(gray, gray, gray, 255);
            }
            DepthPreview.SetPixels32(previewPixels);
            DepthPreview.Apply();

            for (int i = 0; i < mask.Length; i++)
            {
                switch (mask[i])
                {
                    case MaskTarget: previewPixels[i] = new Color32(60, 230, 90, 255); break;
                    case MaskOtherTrash: previewPixels[i] = new Color32(240, 150, 40, 255); break;
                    case MaskScene: previewPixels[i] = new Color32(60, 60, 70, 255); break;
                    default: previewPixels[i] = new Color32(0, 0, 0, 255); break;
                }
            }
            MaskPreview.SetPixels32(previewPixels);
            MaskPreview.Apply();
        }

        // ---------- Saving ----------

        private void SaveFiles(string id)
        {
            string basePath = Path.Combine(imageFolder, id);
            float[] depthCopy = (float[])depth.Clone();
            byte[] maskCopy = (byte[])mask.Clone();
            uint size = (uint)depthSize;
            Task.Run(() =>
            {
                try
                {
                    File.WriteAllBytes(basePath + "_depth.exr", ImageConversion.EncodeArrayToEXR(depthCopy,
                        GraphicsFormat.R32_SFloat, size, size, 0, Texture2D.EXRFlags.CompressZIP));
                    File.WriteAllBytes(basePath + "_mask.png", ImageConversion.EncodeArrayToPNG(maskCopy,
                        GraphicsFormat.R8_UNorm, size, size));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SortQuest] Couldn't save depth or mask for image {id}: {e.Message}");
                }
            });

            // The color view comes back from the GPU a frame or two later, without stalling.
            uint colorWidth = (uint)colorSize;
            AsyncGPUReadback.Request(ColorTexture, 0, TextureFormat.RGBA32, request =>
            {
                if (request.hasError)
                {
                    Debug.LogWarning($"[SortQuest] Couldn't read the color image for {id}.");
                    return;
                }
                byte[] pixels = request.GetData<byte>().ToArray();
                Task.Run(() =>
                {
                    try
                    {
                        File.WriteAllBytes(basePath + "_rgb.png", ImageConversion.EncodeArrayToPNG(pixels,
                            GraphicsFormat.R8G8B8A8_UNorm, colorWidth, colorWidth));
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[SortQuest] Couldn't save color image {id}: {e.Message}");
                    }
                });
            });
        }

        // ---------- Visible camera body ----------

        /// <summary>A small camera body on a pole so players can see where the robot's eye is. No colliders.</summary>
        private void BuildHousing()
        {
            if (housingMaterial == null)
            {
                return;
            }
            // The camera looks along its +Z; the body sits just behind the lens, inside the near clip distance.
            Transform body = VizUtil.CreateShape("CameraBody", transform, VizUtil.CubeMesh, housingMaterial).transform;
            body.localPosition = new Vector3(0f, 0f, -0.06f);
            body.localScale = new Vector3(0.12f, 0.08f, 0.1f);

            Transform pole = VizUtil.CreateShape("Pole", transform, VizUtil.CylinderMesh, housingMaterial).transform;
            VizUtil.PlaceBetween(pole, transform.position - transform.forward * 0.1f,
                transform.position - transform.forward * 0.7f, 0.015f);

            if (lightMaterial != null)
            {
                captureLight = VizUtil.CreateShape("CaptureLight", transform, VizUtil.SphereMesh, lightMaterial, Color.red);
                captureLight.transform.localPosition = new Vector3(0.045f, 0.03f, -0.005f);
                captureLight.transform.localScale = Vector3.one * 0.015f;
            }
        }
    }
}
