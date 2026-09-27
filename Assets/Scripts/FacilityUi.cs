using TMPro;
using UnityEngine;

namespace SortQuest
{
    /// <summary>Shared collider-free display styling for facility screens and menu cards.</summary>
    public static class FacilityUi
    {
        public static readonly Color Ink = new Color(.025f, .045f, .055f);
        public static readonly Color Frame = new Color(.12f, .2f, .22f);
        public static readonly Color Accent = new Color(.35f, .85f, .72f);
        public static readonly Color Muted = new Color(.66f, .77f, .8f);

        public static void Panel(Transform parent, Vector2 size, Vector3 center, Material material)
        {
            material = Resources.Load<Material>("FacilityDisplay") ?? material;
            Shape("Display frame", size + Vector2.one * .025f, center + Vector3.forward * .016f, Frame);
            Shape("Display face", size, center + Vector3.forward * .012f, Ink);
            Shape("Display accent", new Vector2(size.x - .04f, .006f), center + new Vector3(0, size.y * .5f - .016f, .008f), Accent);
            void Shape(string name, Vector2 dimensions, Vector3 position, Color color)
            {
                var renderer = VizUtil.CreateShape(name, parent, VizUtil.QuadMesh, material, color);
                renderer.transform.localPosition = position;
                renderer.transform.localScale = new Vector3(dimensions.x, dimensions.y, 1);
            }
        }

        public static void Style(TMP_Text text, float size, bool heading = false)
        {
            text.color = heading ? Accent : Color.white;
            text.fontSize = size;
            text.enableAutoSizing = true;
            text.fontSizeMin = size * .8f;
            text.fontSizeMax = size;
            text.fontStyle = heading ? FontStyles.Bold : FontStyles.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }
    }
}
