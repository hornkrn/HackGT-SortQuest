using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace SortQuest
{
    /// <summary>
    /// Helpers for the learning visuals: simple colored shapes and text built at runtime.
    /// Shapes have no colliders, so they never affect physics or grabbing.
    /// Colors are set per renderer, so one see-through material (Sprites/Default) serves everything.
    /// </summary>
    public static class VizUtil
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static Mesh cubeMesh;
        private static Mesh sphereMesh;

        public static Mesh CubeMesh => cubeMesh != null ? cubeMesh : cubeMesh = PrimitiveMesh(PrimitiveType.Cube);
        public static Mesh SphereMesh => sphereMesh != null ? sphereMesh : sphereMesh = PrimitiveMesh(PrimitiveType.Sphere);

        /// <summary>Used only if no material was assigned in the Inspector.</summary>
        public static Material FallbackMaterial()
        {
            return new Material(Shader.Find("Sprites/Default"));
        }

        public static MeshRenderer CreateShape(string name, Transform parent, Mesh mesh, Material material, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            SetColor(meshRenderer, color);
            return meshRenderer;
        }

        public static void SetColor(Renderer target, Color color)
        {
            var block = new MaterialPropertyBlock();
            target.GetPropertyBlock(block);
            block.SetColor(ColorId, color);
            target.SetPropertyBlock(block);
        }

        public static TextMeshPro CreateText(string name, Transform parent, Vector3 localPosition, Vector2 size,
            float fontSize, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshPro text = go.AddComponent<TextMeshPro>();
            text.rectTransform.sizeDelta = size;
            text.rectTransform.localPosition = localPosition;
            text.fontSize = fontSize;
            text.alignment = alignment;
            return text;
        }

        public static LineRenderer CreateLine(string name, Transform parent, Material material, Color color, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = material;
            line.startColor = color;
            line.endColor = color;
            line.widthMultiplier = width;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = 0;
            return line;
        }

        private static Mesh PrimitiveMesh(PrimitiveType type)
        {
            GameObject temp = GameObject.CreatePrimitive(type);
            Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temp);
            return mesh;
        }
    }
}
