using System;
using TMPro;
using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// One bar per item type showing what the robot has learned: how many good grasps it has,
    /// and how sure it is (the share of those grasps that agree with the one it would choose).
    /// </summary>
    public class ConfidenceBars : MonoBehaviour
    {
        [Tooltip("Found automatically if left empty.")]
        [SerializeField] private GraspPolicy policy;

        [Tooltip("Found automatically if left empty.")]
        [SerializeField] private GraspDataset dataset;

        [Tooltip("Found automatically if left empty. The bars show what the robot learned for the selected gripper.")]
        [SerializeField] private GripperCatalog catalog;

        [Tooltip("See-through material (Sprites/Default). Created if left empty.")]
        [SerializeField] private Material material;

        [SerializeField] private float labelWidth = 0.6f;
        [SerializeField] private float barWidth = 0.45f;
        [SerializeField] private float rowHeight = 0.075f;
        [Tooltip("Seconds per six-item page, so the expanded catalog fits the existing display.")]
        [SerializeField, Min(3f)] private float pageSeconds = 8f;
        private const int PageSize = 6;
        private int page;
        private float nextPage;

        private ItemType[] types;
        private bool dirty = true;
        private TextMeshPro[] labels;
        private Transform[] fills;
        private TextMeshPro title;
        private TextMeshPro[] values;

        private void Awake()
        {
            if (policy == null) policy = FindAnyObjectByType<GraspPolicy>();
            if (dataset == null) dataset = FindAnyObjectByType<GraspDataset>();
            if (catalog == null) catalog = FindAnyObjectByType<GripperCatalog>();
            if (material == null) material = VizUtil.FallbackMaterial();
            Build();
        }

        private void OnEnable()
        {
            if (dataset != null) dataset.RecordAdded += HandleRecordAdded;
            if (catalog != null) catalog.Changed += HandleGripperChanged;
        }

        private void OnDisable()
        {
            if (dataset != null) dataset.RecordAdded -= HandleRecordAdded;
            if (catalog != null) catalog.Changed -= HandleGripperChanged;
        }

        private void HandleGripperChanged(GripperProfile profile)
        {
            dirty = true;
        }


        private void Build()
        {
            rowHeight = Mathf.Max(rowHeight, .105f);
            types = (ItemType[])Enum.GetValues(typeof(ItemType));
            int rows = Mathf.Min(PageSize, types.Length);
            values = new TextMeshPro[rows];
            labels = new TextMeshPro[rows];
            fills = new Transform[rows];

            float totalWidth = labelWidth + barWidth;
            float left = -totalWidth * 0.5f;
            float top = rowHeight * rows * 0.5f;

            FacilityUi.Panel(transform, new Vector2(totalWidth + .1f, rowHeight * rows + .28f), new Vector3(0, .035f, 0), material);
            title = VizUtil.CreateText("Title", transform, new Vector3(0f, top + 0.08f, 0f),
                new Vector2(totalWidth, 0.12f), 0.5f, TextAlignmentOptions.Center);
            FacilityUi.Style(title, .6f, true);
            title.text = "GRASP AGREEMENT";

            for (int i = 0; i < rows; i++)
            {
                float y = top - rowHeight * (i + 0.5f);
                labels[i] = VizUtil.CreateText(types[i] + "Label", transform, new Vector3(left + labelWidth * 0.5f, y, 0f),
                    new Vector2(labelWidth, rowHeight), 0.3f, TextAlignmentOptions.Left);

                FacilityUi.Style(labels[i], .45f);
                values[i] = VizUtil.CreateText(types[i] + "Value", transform, new Vector3(left + labelWidth + barWidth * .5f, y + .028f, -.005f),
                    new Vector2(barWidth, .05f), .25f, TextAlignmentOptions.Right);
                values[i].color = FacilityUi.Muted;
                MeshRenderer background = VizUtil.CreateShape(types[i] + "Background", transform, VizUtil.CubeMesh, material,
                    new Color(.15f, .23f, .26f));
                background.transform.localPosition = new Vector3(left + labelWidth + barWidth * 0.5f, y - .015f, 0.002f);
                background.transform.localScale = new Vector3(barWidth, rowHeight * 0.22f, 0.002f);

                Color binColor = TrashTypes.BinColor(TrashTypes.CorrectBin(types[i]));
                MeshRenderer fill = VizUtil.CreateShape(types[i] + "Fill", transform, VizUtil.CubeMesh, material, binColor);
                fills[i] = fill.transform;
            }
        }

        private void HandleRecordAdded(GraspRecord record)
        {
            // Records can arrive in bursts (the augmenter adds several at once), so redraw at most once per frame.
            dirty = true;
        }

        private void LateUpdate()
        {
            if (types.Length > PageSize && Time.unscaledTime >= nextPage)
            {
                if (nextPage > 0) page = (page + 1) % Mathf.CeilToInt((float)types.Length / PageSize);
                nextPage = Time.unscaledTime + Mathf.Max(3f, pageSeconds);
                dirty = true;
            }
            if (dirty)
            {
                dirty = false;
                Refresh();
            }
        }

        private void Refresh()
        {
            float left = -(labelWidth + barWidth) * 0.5f;
            float top = rowHeight * labels.Length * 0.5f;
            title.text = $"GRASP AGREEMENT\n<size=60%>{GripperCatalog.CurrentOrStandard(catalog).displayName} • page {page + 1}/{Mathf.CeilToInt((float)types.Length / PageSize)}</size>";
            for (int i = 0; i < labels.Length; i++)
            {
                int index = page * PageSize + i;
                bool visible = index < types.Length;
                labels[i].gameObject.SetActive(visible);
                values[i].gameObject.SetActive(visible);
                fills[i].gameObject.SetActive(visible);
                if (!visible) continue;
                ItemType type = types[index];
                VizUtil.SetColor(fills[i].GetComponent<Renderer>(), TrashTypes.BinColor(TrashTypes.CorrectBin(type)));
                float y = top - rowHeight * (i + 0.5f);
                string itemName = TrashTypes.DisplayName(type);
                float confidence = 0f;
                if (policy != null && policy.TryGetLearnedGrasp(type, out GraspChoice choice))
                {
                    confidence = choice.Confidence;
                    // People's grasps for this item; the suction cup learns from converted, practiced versions of them.
                    int taught = 0;
                    if (dataset != null)
                    {
                        foreach (GraspRecord record in dataset.GoodGrasps(type))
                        {
                            if (record.source == GraspRecord.SourceHuman) taught++;
                        }
                    }
                    int practiced = choice.GoodCount - choice.HumanCount;
                    labels[i].text = $"{itemName}\n<size=65%>{taught} taught • {practiced} practiced</size>";
                    values[i].text = $"{confidence * 100f:F0}%";
                }
                else
                {
                    labels[i].text = itemName;
                    values[i].text = "—";
                }

                float fillWidth = Mathf.Max(0.001f, barWidth * confidence);
                fills[i].localPosition = new Vector3(left + labelWidth + fillWidth * 0.5f, y - .015f, 0f);
                fills[i].localScale = new Vector3(fillWidth, rowHeight * 0.22f, 0.004f);
            }
        }
    }
}
