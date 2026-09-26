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

        [Tooltip("See-through material (Sprites/Default). Created if left empty.")]
        [SerializeField] private Material material;

        [SerializeField] private float labelWidth = 0.6f;
        [SerializeField] private float barWidth = 0.45f;
        [SerializeField] private float rowHeight = 0.075f;

        private ItemType[] types;
        private bool dirty = true;
        private TextMeshPro[] labels;
        private Transform[] fills;

        private void Awake()
        {
            if (policy == null) policy = FindAnyObjectByType<GraspPolicy>();
            if (dataset == null) dataset = FindAnyObjectByType<GraspDataset>();
            if (material == null) material = VizUtil.FallbackMaterial();
            Build();
        }

        private void OnEnable()
        {
            if (dataset != null) dataset.RecordAdded += HandleRecordAdded;
        }

        private void OnDisable()
        {
            if (dataset != null) dataset.RecordAdded -= HandleRecordAdded;
        }


        private void Build()
        {
            types = (ItemType[])Enum.GetValues(typeof(ItemType));
            labels = new TextMeshPro[types.Length];
            fills = new Transform[types.Length];

            float totalWidth = labelWidth + barWidth;
            float left = -totalWidth * 0.5f;
            float top = rowHeight * types.Length * 0.5f;

            TextMeshPro title = VizUtil.CreateText("Title", transform, new Vector3(0f, top + 0.08f, 0f),
                new Vector2(totalWidth, 0.12f), 0.5f, TextAlignmentOptions.Center);
            title.text = "What the robot has learned";

            for (int i = 0; i < types.Length; i++)
            {
                float y = top - rowHeight * (i + 0.5f);
                labels[i] = VizUtil.CreateText(types[i] + "Label", transform, new Vector3(left + labelWidth * 0.5f, y, 0f),
                    new Vector2(labelWidth, rowHeight), 0.3f, TextAlignmentOptions.Left);

                MeshRenderer background = VizUtil.CreateShape(types[i] + "Background", transform, VizUtil.CubeMesh, material,
                    new Color(1f, 1f, 1f, 0.15f));
                background.transform.localPosition = new Vector3(left + labelWidth + barWidth * 0.5f, y, 0.002f);
                background.transform.localScale = new Vector3(barWidth, rowHeight * 0.5f, 0.002f);

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
            if (dirty)
            {
                dirty = false;
                Refresh();
            }
        }

        private void Refresh()
        {
            float left = -(labelWidth + barWidth) * 0.5f;
            float top = rowHeight * types.Length * 0.5f;
            for (int i = 0; i < types.Length; i++)
            {
                float y = top - rowHeight * (i + 0.5f);
                string itemName = TrashTypes.DisplayName(types[i]);
                float confidence = 0f;
                if (policy != null && policy.TryGetLearnedGrasp(types[i], out GraspChoice choice))
                {
                    confidence = choice.Confidence;
                    int taught = 0;
                    foreach (GraspRecord record in dataset != null ? dataset.GoodGrasps(types[i]) : new System.Collections.Generic.List<GraspRecord>())
                    {
                        if (record.source == GraspRecord.SourceHuman)
                        {
                            taught++;
                        }
                    }
                    int practiced = choice.GoodCount - taught;
                    labels[i].text = $"{itemName}  <size=75%>{taught} taught, {practiced} practiced, " +
                                     $"{confidence * 100f:F0}% sure</size>";
                }
                else
                {
                    labels[i].text = $"{itemName}  <size=75%>no data yet</size>";
                }

                float fillWidth = Mathf.Max(0.001f, barWidth * confidence);
                fills[i].localPosition = new Vector3(left + labelWidth + fillWidth * 0.5f, y, 0f);
                fills[i].localScale = new Vector3(fillWidth, rowHeight * 0.5f, 0.004f);
            }
        }
    }
}
