using System;
using TMPro;
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

        [Tooltip("Optional sign that shows the bin's name and which items go in it.")]
        [SerializeField] private TMP_Text label;

        public event Action<SortingBin, TrashItem, bool> ItemSorted;

        public BinType BinType => binType;
        public int CorrectCount { get; private set; }
        public int WrongCount { get; private set; }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private LineRenderer tutorialOutline;
        private LineRenderer tutorialOutlineCore;
        private Color tutorialHighlightColor;
        private static Material tutorialLineMaterial;

        /// <summary>Draws a bright, non-colliding outline around this bin during the guided tutorial.</summary>
        public void SetTutorialHighlighted(bool highlighted)
        {
            if (highlighted && tutorialOutline == null)
            {
                Collider binCollider = GetComponent<Collider>();
                if (binCollider == null) return;

                var outlineObject = new GameObject("Tutorial bin highlight");
                outlineObject.transform.SetParent(transform, true);
                tutorialOutline = CreateTutorialLine(outlineObject, 13);
                var coreObject = new GameObject("White highlight core");
                coreObject.transform.SetParent(outlineObject.transform, false);
                tutorialOutlineCore = CreateTutorialLine(coreObject, 13);
                tutorialOutlineCore.widthMultiplier = 0.028f;
                tutorialHighlightColor = Color.Lerp(TrashTypes.BinColor(binType), Color.white, 0.22f);

                Bounds bounds = binCollider.bounds;
                Vector3 c = bounds.center;
                Vector3 e = bounds.extents * 1.06f;
                Vector3[] points =
                {
                    new Vector3(c.x - e.x, c.y + e.y, c.z - e.z),
                    new Vector3(c.x + e.x, c.y + e.y, c.z - e.z),
                    new Vector3(c.x + e.x, c.y + e.y, c.z + e.z),
                    new Vector3(c.x - e.x, c.y + e.y, c.z + e.z),
                    new Vector3(c.x - e.x, c.y + e.y, c.z - e.z),
                    new Vector3(c.x - e.x, c.y - e.y, c.z - e.z),
                    new Vector3(c.x - e.x, c.y + e.y, c.z - e.z),
                    new Vector3(c.x + e.x, c.y + e.y, c.z - e.z),
                    new Vector3(c.x + e.x, c.y - e.y, c.z - e.z),
                    new Vector3(c.x + e.x, c.y + e.y, c.z - e.z),
                    new Vector3(c.x + e.x, c.y + e.y, c.z + e.z),
                    new Vector3(c.x + e.x, c.y - e.y, c.z + e.z),
                    new Vector3(c.x + e.x, c.y + e.y, c.z + e.z)
                };
                tutorialOutline.SetPositions(points);
                tutorialOutlineCore.SetPositions(points);
            }
            if (tutorialOutline != null)
            {
                tutorialOutline.enabled = highlighted;
                if (tutorialOutlineCore != null) tutorialOutlineCore.enabled = highlighted;
            }
        }

        private static LineRenderer CreateTutorialLine(GameObject parent, int pointCount)
        {
            if (tutorialLineMaterial == null)
            {
                Shader shader = Resources.Load<Shader>("TutorialHighlight");
                if (shader != null) tutorialLineMaterial = new Material(shader);
            }

            LineRenderer line = parent.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = pointCount;
            line.widthMultiplier = 0.085f;
            line.sharedMaterial = tutorialLineMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.numCapVertices = 4;
            line.numCornerVertices = 2;
            line.startColor = Color.white;
            line.endColor = Color.white;
            return line;
        }

        private void Update()
        {
            if (tutorialOutline == null || !tutorialOutline.enabled) return;

            // A broad, animated bin-color band is visible from a distance; its white center keeps
            // the edges legible against both the bin and the environment.
            float pulse = (Mathf.Sin(Time.unscaledTime * 5f) + 1f) * 0.5f;
            tutorialOutline.widthMultiplier = Mathf.Lerp(0.075f, 0.105f, pulse);
            Color color = Color.Lerp(tutorialHighlightColor, Color.white, pulse * 0.22f);
            tutorialOutline.startColor = color;
            tutorialOutline.endColor = color;
            // The inner line can be absent after an editor domain reload or if its GameObject was
            // removed independently. Keep the main highlight running without throwing every frame.
            if (tutorialOutlineCore != null)
            {
                tutorialOutlineCore.startColor = Color.white;
                tutorialOutlineCore.endColor = Color.white;
            }
        }

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
            if (label != null)
            {
                label.text = TrashTypes.BinSignText(binType);
            }
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
            if (item.LastHeldByRobot)
            {
                Debug.Log($"[SortQuest] Robot dropped {TrashTypes.DisplayName(item.ItemType)} into the {binType} bin: " +
                          (correct ? "correct" : "wrong"));
            }
            // The player's score only counts the player's own sorts.
            if (scoreBoard != null && !item.LastHeldByRobot)
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
