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
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static Mesh cubeMesh;
        private static Mesh sphereMesh;
        private static Mesh cylinderMesh;
        private static Mesh quadMesh;

        public static Mesh CubeMesh => cubeMesh != null ? cubeMesh : cubeMesh = PrimitiveMesh(PrimitiveType.Cube);
        public static Mesh SphereMesh => sphereMesh != null ? sphereMesh : sphereMesh = PrimitiveMesh(PrimitiveType.Sphere);
        public static Mesh CylinderMesh => cylinderMesh != null ? cylinderMesh : cylinderMesh = PrimitiveMesh(PrimitiveType.Cylinder);
        public static Mesh QuadMesh => quadMesh != null ? quadMesh : quadMesh = PrimitiveMesh(PrimitiveType.Quad);

        /// <summary>Used only if no material was assigned in the Inspector.</summary>
        public static Material FallbackMaterial()
        {
            return new Material(Shader.Find("Sprites/Default"));
        }

        /// <summary>A shape using the material's own color (for lit materials like the robot's metal and plastic).</summary>
        public static MeshRenderer CreateShape(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            return meshRenderer;
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

        public static void SetTexture(Renderer target, Texture texture)
        {
            var block = new MaterialPropertyBlock();
            target.GetPropertyBlock(block);
            block.SetTexture(MainTexId, texture);
            target.SetPropertyBlock(block);
        }

        /// <summary>Places a cylinder (Unity's is 2 units tall along Y) so it runs from a to b.</summary>
        public static void PlaceBetween(Transform cylinder, Vector3 a, Vector3 b, float radius)
        {
            Vector3 span = b - a;
            cylinder.position = (a + b) * 0.5f;
            cylinder.rotation = span.sqrMagnitude > 1e-8f ? Quaternion.FromToRotation(Vector3.up, span) : Quaternion.identity;
            cylinder.localScale = new Vector3(radius * 2f, span.magnitude * 0.5f, radius * 2f);
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
