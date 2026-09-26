using TMPro;
using UnityEngine;

namespace SortQuest
{
    /// <summary>Unlit, collider-free backing for world text. Shares the scene's panel material.</summary>
    public sealed class ReadableSign : MonoBehaviour
    {
        private TMP_Text label;
        private GameObject backing;

        public static void Apply(TMP_Text text, Vector2 size, float fontSize, Color accent, Material material)
        {
            if (text == null || text.GetComponent<ReadableSign>() != null) return;
            var sign = text.gameObject.AddComponent<ReadableSign>();
            sign.label = text;
            text.color = new Color(.96f, .98f, 1f);
            text.fontStyle = FontStyles.Bold;
            text.fontSize = fontSize;
            text.enableAutoSizing = true;
            text.fontSizeMin = fontSize * .72f;
            text.fontSizeMax = fontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.sizeDelta = size;
            text.margin = new Vector4(.02f, .015f, .02f, .015f);
            sign.backing = new GameObject("Sign backing (visual only)");
            sign.backing.transform.SetParent(text.transform, false);
            Panel("Frame", new Vector3(0, 0, .008f), size + new Vector2(.025f, .025f), accent);
            Panel("Face", new Vector3(0, 0, .005f), size, new Color(.025f, .042f, .055f));
            Panel("Category stripe", new Vector3(-size.x * .5f + .007f, 0, .002f), new Vector2(.014f, size.y), accent);
            sign.backing.SetActive(text.enabled);

            void Panel(string name, Vector3 position, Vector2 dimensions, Color color)
            {
                var renderer = VizUtil.CreateShape(name, sign.backing.transform, VizUtil.QuadMesh, material, color);
                renderer.transform.localPosition = position;
                renderer.transform.localScale = new Vector3(dimensions.x, dimensions.y, 1);
            }
        }

        private void LateUpdate()
        {
            if (label != null && backing != null && backing.activeSelf != label.enabled)
                backing.SetActive(label.enabled);
        }
    }
}
