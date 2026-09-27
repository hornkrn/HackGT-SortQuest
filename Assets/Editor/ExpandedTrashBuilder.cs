using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SortQuest.Editor
{
    /// <summary>Builds assets through Unity APIs. Never saves or rewires the active scene.</summary>
    public static class ExpandedTrashBuilder
    {
        internal const string Art = "Assets/Art/ExpandedTrash";
        internal const string Catalog = "Assets/Resources/TrashCatalog";
        internal const string Collision = "Assets/Physics/TrashColliders";
        private const string ColliderRoot = "_TrashColliders";

        [MenuItem("SortQuest/Trash Art/Build Expanded Catalog and Accurate Colliders")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            Folder(Art); Folder(Catalog); Folder(Collision);
            Texture2D atlas = TrashArtAtlas.Build(Art, true);
            var material = AssetDatabase.LoadAssetAtPath<Material>(Art + "/ExpandedTrash.mat");
            if (material == null)
            {
                material = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/UsedTrash/UsedTrash.mat"));
                AssetDatabase.CreateAsset(material, Art + "/ExpandedTrash.mat");
            }
            material.SetTexture("_BaseMap", atlas);
            EditorUtility.SetDirty(material);
            foreach (ItemType type in Enum.GetValues(typeof(ItemType)))
            {
                if ((int)type < 6) continue;
                BuildNew(type, material);
            }
            // The old visual variants retain their transforms, GUIDs, interaction settings and local grasp frames.
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" })
                .OrderBy(g => AssetDatabase.GUIDToAssetPath(g).Contains("TrashVariants") ? 1 : 0))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab.GetComponent<TrashItem>() != null) UpgradeLegacy(path);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("EXPANDED_TRASH_BUILT: 18 new types, 48 legacy variants refitted. Scene unchanged.");
        }

        private static void BuildNew(ItemType type, Material material)
        {
            var model = ExpandedTrashGeometry.Build(type);
            string path = Catalog + "/" + type + ".prefab";
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
            GameObject root = exists ? PrefabUtility.LoadPrefabContents(path) :
                Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/CardboardBox.prefab"));
            try
            {
                if (!exists && PrefabUtility.IsPartOfPrefabInstance(root))
                    PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                root.name = type.ToString();
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.transform.localScale = Vector3.one;
                var serialized = new SerializedObject(root.GetComponent<TrashItem>());
                serialized.FindProperty("itemType").intValue = (int)type;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                root.GetComponent<Rigidbody>().mass = model.Mass;
                root.GetComponent<MeshFilter>().sharedMesh = SaveMesh(model.Visual(type.ToString()), Art + "/" + type + ".asset");
                root.GetComponent<MeshRenderer>().sharedMaterial = material;
                SetColliders(root, model.Pieces, type.ToString(), PhysicsFor(type));
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                foreach (Mesh detail in model.Details) Object.DestroyImmediate(detail);
                if (exists) PrefabUtility.UnloadPrefabContents(root); else Object.DestroyImmediate(root);
            }
        }

        private static PhysicsMaterial PhysicsFor(ItemType type)
        {
            string path = "Assets/Physics/" + type + ".physicMaterial";
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (material == null) { material = new PhysicsMaterial(type.ToString()); AssetDatabase.CreateAsset(material, path); }
            BinType bin = TrashTypes.CorrectBin(type);
            material.staticFriction = bin == BinType.Paper ? .65f : .45f;
            material.dynamicFriction = bin == BinType.Paper ? .5f : .3f;
            material.bounciness = bin == BinType.Plastic ? .08f : .02f;
            material.frictionCombine = PhysicsMaterialCombine.Average;
            material.bounceCombine = PhysicsMaterialCombine.Average;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void UpgradeLegacy(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var item = root.GetComponent<TrashItem>();
                Mesh visual = root.GetComponent<MeshFilter>().sharedMesh;
                Vector3 scale = root.transform.localScale;
                float[] cuts = item.ItemType == ItemType.PlasticBottle ? new[] { .061f, .097f } :
                    item.ItemType == ItemType.AluminumCan ? new[] { -.04f, .04f } :
                    item.ItemType == ItemType.BatteryAA ? new[] { .034f } : Array.Empty<float>();
                var boundaries = new List<float> { visual.bounds.min.y - .001f };
                boundaries.AddRange(cuts.Select(y => y / scale.y));
                boundaries.Add(visual.bounds.max.y + .001f);
                var pieces = new List<Mesh>();
                if (item.ItemType == ItemType.CrumpledPaper)
                {
                    for (int octant = 0; octant < 32; octant++)
                    {
                        Mesh piece = visual;
                        Vector3 anchor = Vector3.zero;
                        for (int axis = 0; axis < 3; axis++)
                        {
                            bool positive = (octant & (1 << axis)) != 0;
                            float min = positive ? 0 : visual.bounds.min[axis] - .001f;
                            float max = positive ? visual.bounds.max[axis] + .001f : 0;
                            if (axis == 1)
                            {
                                float middle = (min + max) * .5f;
                                if ((octant & 16) == 0) max = middle; else min = middle;
                                anchor.y = Mathf.Clamp(0, min, max);
                            }
                            // Slightly overlap sections so exact seam rays cannot slip between hulls.
                            float seam = .0003f / scale[axis];
                            Mesh clipped = ClipBand(piece, min - seam, max + seam, axis);
                            if (piece != visual) Object.DestroyImmediate(piece);
                            piece = clipped;
                        }
                        Vector3 divider = new Vector3((octant & 1) != 0 ? 1 : -1, 0, (octant & 4) != 0 ? -1 : 1);
                        bool firstWedge = (octant & 8) == 0;
                        float wedgeSeam = .0006f / scale.x;
                        Mesh half = ClipBand(piece, firstWedge ? -100 : -wedgeSeam, firstWedge ? wedgeSeam : 100, 0, divider);
                        Object.DestroyImmediate(piece);
                        piece = half;
                        // Include the interior in every convex section so the compound remains solid.
                        var vertices = piece.vertices.ToList();
                        var triangles = piece.triangles.ToList();
                        triangles.Add(vertices.Count); triangles.Add(0); triangles.Add(1);
                        vertices.Add(anchor);
                        piece.SetVertices(vertices); piece.SetTriangles(triangles, 0);
                        pieces.Add(piece);
                    }
                }
                else for (int i = 0; i < boundaries.Count - 1; i++)
                    pieces.Add(ClipBand(visual, boundaries[i], boundaries[i + 1]));
                SetColliders(root, pieces, Path.GetFileNameWithoutExtension(path), PhysicsFor(item.ItemType));
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void SetColliders(GameObject root, List<Mesh> pieces, string name, PhysicsMaterial material)
        {
            foreach (Collider col in root.GetComponentsInChildren<Collider>(true))
                if (!col.isTrigger) Object.DestroyImmediate(col);
            Transform old = root.transform.Find(ColliderRoot);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var holder = new GameObject(ColliderRoot);
            holder.transform.SetParent(root.transform, false);
            for (int i = 0; i < pieces.Count; i++)
            {
                Mesh mesh = SaveMesh(pieces[i], Collision + "/" + name + "_" + i + ".asset");
                Physics.BakeMesh(mesh.GetEntityId(), true);
                var piece = new GameObject("Contact " + i);
                piece.layer = root.layer;
                piece.transform.SetParent(holder.transform, false);
                var collider = piece.AddComponent<MeshCollider>();
                collider.convex = true;
                collider.sharedMesh = mesh;
                collider.sharedMaterial = material;
            }
        }

        // Clip triangles at real visual-mesh cross sections. Cooking makes one convex hull per band,
        // preserving narrow necks, shoulders and battery terminals instead of enclosing the whole item.
        private static Mesh ClipBand(Mesh source, float min, float max, int axis = 1, Vector3? plane = null)
        {
            Vector3[] vertices = source.vertices;
            int[] indices = source.triangles;
            var output = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < indices.Length; i += 3)
            {
                var polygon = new List<Vector3> { vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]] };
                polygon = Clip(polygon, min, true, axis, plane);
                polygon = Clip(polygon, max, false, axis, plane);
                if (polygon.Count < 3) continue;
                int start = output.Count;
                output.AddRange(polygon);
                for (int j = 1; j < polygon.Count - 1; j++)
                { triangles.Add(start); triangles.Add(start + j); triangles.Add(start + j + 1); }
            }
            var mesh = new Mesh { name = source.name + " collision band" };
            mesh.SetVertices(output); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            return mesh;
        }

        private static List<Vector3> Clip(List<Vector3> input, float height, bool above, int axis, Vector3? plane)
        {
            var result = new List<Vector3>();
            if (input.Count == 0) return result;
            Vector3 previous = input[input.Count - 1];
            float previousHeight = plane.HasValue ? Vector3.Dot(previous, plane.Value) : previous[axis];
            bool previousInside = above ? previousHeight >= height : previousHeight <= height;
            foreach (Vector3 current in input)
            {
                float currentHeight = plane.HasValue ? Vector3.Dot(current, plane.Value) : current[axis];
                bool inside = above ? currentHeight >= height : currentHeight <= height;
                if (inside != previousInside)
                    result.Add(Vector3.Lerp(previous, current, (height - previousHeight) / (currentHeight - previousHeight)));
                if (inside) result.Add(current);
                previous = current; previousInside = inside; previousHeight = currentHeight;
            }
            return result;
        }

        private static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        internal static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        public static void BuildBatch()
        {
            try { Build(); ExpandedTrashChecks.RenderCatalog(); ExpandedTrashChecks.Run(); GripperContactChecks.Run(); EditorApplication.Exit(0); }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
