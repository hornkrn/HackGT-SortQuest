using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace SortQuest
{
    /// <summary>
    /// The trash guide board to the player's left: every kind of trash, grouped under the bin it goes in, with
    /// display models and clear names, so the items on the belt don't need their own labels.
    /// The board itself is scene art (SortQuest > Polish Scene Visuals); this adds the models and text once at
    /// start. Presentation only: no colliders, physics, or per-frame work. Created by InteractionFeedback.
    /// </summary>
    public sealed class TrashGuide : MonoBehaviour
    {
        [Tooltip("Found automatically if left empty. Display models are copied from its trash prefabs.")]
        [SerializeField] private TrashSpawner spawner;

        [Tooltip("How many different looks of each item to show side by side.")]
        [SerializeField, Range(1, 2)] private int looksPerItem = 2;

        [Tooltip("Each display model is scaled so its longest side is this long, in meters.")]
        [SerializeField] private float modelSize = 0.13f;

        [Tooltip("Turns each model this many degrees for a three-quarter view.")]
        [SerializeField] private float modelTurn = 30f;

        private static readonly Color Ink = new Color32(0x17, 0x26, 0x2C, 0xFF);
        private static readonly Color Teal = new Color32(0x61, 0xB8, 0xA0, 0xFF);

        /// <summary>How many display models were created (for checks).</summary>
        public int ModelCount { get; private set; }

        private void Start()
        {
            if (spawner == null) spawner = FindAnyObjectByType<TrashSpawner>();
            transform.SetPositionAndRotation(TrashGuideLayout.BoardPosition, TrashGuideLayout.BoardRotation);
            Build();
        }

        private void Build()
        {
            // Text sits just in front of the surface it's printed on.
            float onFace = -TrashGuideLayout.FaceDepth - 0.002f;
            float onHeader = -0.021f;
            float width = TrashGuideLayout.BoardWidth;

            Text("Title", new Vector3(0f, TrashGuideLayout.TitleY + 0.01f, onFace), new Vector2(width - 0.1f, 0.12f),
                0.8f, Color.white, FontStyles.Bold, "TRASH GUIDE");
            Text("Subtitle", new Vector3(0f, TrashGuideLayout.TitleY - 0.065f, onFace), new Vector2(width - 0.1f, 0.05f),
                0.34f, Teal, FontStyles.Normal, "Which bin does each item go in?");

            for (int column = 0; column < TrashGuideLayout.Columns.Length; column++)
            {
                BinType bin = TrashGuideLayout.Columns[column];
                Color accent = TrashGuideLayout.BinAccent(bin);
                float x = TrashGuideLayout.ColumnX(column);
                Text("Header " + bin, new Vector3(x, TrashGuideLayout.HeaderY, onHeader),
                    new Vector2(TrashGuideLayout.ColumnWidth - 0.06f, TrashGuideLayout.HeaderHeight),
                    0.52f, Ink, FontStyles.Bold, bin.ToString().ToUpperInvariant());

                List<ItemType> items = TrashGuideLayout.ItemsFor(bin);
                for (int i = 0; i < items.Count; i++)
                {
                    Vector3 slot = TrashGuideLayout.Slot(column, i, items.Count);
                    Text("Name " + items[i], new Vector3(slot.x, TrashGuideLayout.LabelY(slot), onFace),
                        new Vector2(TrashGuideLayout.ColumnWidth - 0.04f, 0.06f),
                        0.4f, Color.Lerp(accent, Color.white, 0.25f), FontStyles.Bold, TrashTypes.DisplayName(items[i]));
                    ShowModels(items[i], slot);
                }
            }
        }

        private void ShowModels(ItemType item, Vector3 slot)
        {
            if (spawner == null)
            {
                return;
            }
            List<TrashItem> prefabs = spawner.PrefabsOf(item);
            int count = Mathf.Min(looksPerItem, prefabs.Count);
            float shelf = TrashGuideLayout.ShelfY(slot);
            float depth = -TrashGuideLayout.FaceDepth - TrashGuideLayout.ShelfDepth * 0.5f;
            for (int i = 0; i < count; i++)
            {
                float x = slot.x + (i - (count - 1) * 0.5f) * (modelSize + 0.03f);
                ShowModel(prefabs[i], new Vector3(x, shelf, depth));
            }
        }

        /// <summary>
        /// A look-only copy of a trash prefab (its meshes and materials, nothing else), standing on the shelf at
        /// <paramref name="standPoint"/> in board space.
        /// </summary>
        private void ShowModel(TrashItem prefab, Vector3 standPoint)
        {
            var model = new GameObject(prefab.name + " (display)").transform;
            model.SetParent(transform, false);

            // Parts keep their shape relative to the prefab root, including the root's rotation and scale.
            Transform root = prefab.transform;
            Matrix4x4 toModel = Matrix4x4.TRS(Vector3.zero, root.rotation, root.localScale) * root.worldToLocalMatrix;
            var bounds = new Bounds();
            bool any = false;
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var source = filter.GetComponent<MeshRenderer>();
                if (source == null || !source.enabled || filter.sharedMesh == null)
                {
                    continue;
                }
                Matrix4x4 partMatrix = toModel * filter.transform.localToWorldMatrix;
                var part = new GameObject(filter.name, typeof(MeshFilter), typeof(MeshRenderer)).transform;
                part.SetParent(model, false);
                part.localPosition = partMatrix.GetPosition();
                part.localRotation = partMatrix.rotation;
                part.localScale = partMatrix.lossyScale;
                part.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var partRenderer = part.GetComponent<MeshRenderer>();
                partRenderer.sharedMaterials = source.sharedMaterials;
                partRenderer.shadowCastingMode = ShadowCastingMode.Off;
                partRenderer.receiveShadows = false;
                partRenderer.lightProbeUsage = LightProbeUsage.Off;
                partRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                Encapsulate(ref bounds, ref any, partMatrix, filter.sharedMesh.bounds);
            }
            if (!any)
            {
                if (Application.isPlaying) Destroy(model.gameObject);
                else DestroyImmediate(model.gameObject);
                return;
            }

            // Same size for every item, so small ones like the battery are easy to see.
            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float scale = longest > 1e-4f ? modelSize / longest : 1f;
            Quaternion turn = Quaternion.Euler(0f, modelTurn, 0f);
            var bottomCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            model.localRotation = turn;
            model.localScale = Vector3.one * scale;
            model.localPosition = standPoint - turn * (bottomCenter * scale);
            ModelCount++;
        }

        private static void Encapsulate(ref Bounds bounds, ref bool any, Matrix4x4 matrix, Bounds local)
        {
            for (int corner = 0; corner < 8; corner++)
            {
                var point = new Vector3(
                    (corner & 1) == 0 ? local.min.x : local.max.x,
                    (corner & 2) == 0 ? local.min.y : local.max.y,
                    (corner & 4) == 0 ? local.min.z : local.max.z);
                point = matrix.MultiplyPoint3x4(point);
                if (any) bounds.Encapsulate(point);
                else { bounds = new Bounds(point, Vector3.zero); any = true; }
            }
        }

        private void Text(string name, Vector3 position, Vector2 size, float fontSize, Color color, FontStyles style,
            string value)
        {
            TextMeshPro text = VizUtil.CreateText(name, transform, position, size, fontSize, TextAlignmentOptions.Center);
            text.color = color;
            text.fontStyle = style;
            text.enableAutoSizing = true;
            text.fontSizeMax = fontSize;
            text.fontSizeMin = fontSize * 0.6f;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.text = value;
        }
    }
}
