using System;
using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// A bin with a trigger volume. A released item that enters it is sorted and scored.
    /// Put this on the GameObject that has the trigger collider.
    /// </summary>
    public class SortingBin : MonoBehaviour
    {
        [SerializeField] private BinType binType;

        [Tooltip("Found automatically if left empty.")]
        [SerializeField] private ScoreBoard scoreBoard;

        [Tooltip("Renderers tinted with this bin's color at start. Leave empty to skip.")]
        [SerializeField] private Renderer[] tintRenderers;

        public event Action<SortingBin, TrashItem, bool> ItemSorted;

        public BinType BinType => binType;
        public int CorrectCount { get; private set; }
        public int WrongCount { get; private set; }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Reset()
        {
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = true;
            }
        }

        private void Awake()
        {
            Collider col = GetComponent<Collider>();
            if (col == null || !col.isTrigger)
            {
                Debug.LogWarning($"[SortQuest] {name}: SortingBin needs a trigger collider on the same GameObject.", this);
            }
            if (scoreBoard == null)
            {
                scoreBoard = FindAnyObjectByType<ScoreBoard>();
            }
            ApplyTint();
        }

        // OnTriggerStay (not Enter) so an item released while already inside the bin still counts.
        private void OnTriggerStay(Collider other)
        {
            Rigidbody body = other.attachedRigidbody;
            if (body == null)
            {
                return;
            }
            TrashItem item = body.GetComponent<TrashItem>();
            if (item == null || item.State != TrashItemState.Loose)
            {
                return;
            }

            bool correct = item.CorrectBin == binType;
            if (correct)
            {
                CorrectCount++;
            }
            else
            {
                WrongCount++;
            }

            item.MarkSorted(this, correct);
            if (scoreBoard != null)
            {
                scoreBoard.RegisterSort(item, this, correct);
            }
            ItemSorted?.Invoke(this, item, correct);
        }

        private void ApplyTint()
        {
            if (tintRenderers == null)
            {
                return;
            }
            Color color = TrashTypes.BinColor(binType);
            var block = new MaterialPropertyBlock();
            foreach (Renderer rend in tintRenderers)
            {
                if (rend == null)
                {
                    continue;
                }
                rend.GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                block.SetColor(ColorId, color);
                rend.SetPropertyBlock(block);
            }
        }
    }
}
