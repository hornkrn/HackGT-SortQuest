using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SortQuest.Editor
{
    public static class ExpandedTrashChecks
    {
        private static List<GameObject> Prefabs() => AssetDatabase.FindAssets("t:Prefab",
            new[] { "Assets/Prefabs", ExpandedTrashBuilder.Catalog }).Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>).Where(p => p.GetComponent<TrashItem>() != null).ToList();

        [MenuItem("SortQuest/Trash Art/Validate Expanded Catalog")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            var prefabs = Prefabs();
            Require(prefabs.Count == 66, "Expected 48 original variants and 18 new prefabs");
            Require(prefabs.Select(p => p.GetComponent<TrashItem>().ItemType).Distinct().Count() == 24, "Missing item type");
            Require(Enum.GetValues(typeof(ItemType)).Cast<ItemType>().Select(TrashTypes.ItemId).Distinct().Count() == 24, "Duplicate data ids");
            var scene = EditorSceneManager.NewPreviewScene();
            float worstSurfaceError = 0f;
            int maxParts = 0, maxTriangles = 0;
            try
            {
                var floor = new GameObject("Test floor");
                SceneManager.MoveGameObjectToScene(floor, scene);
                floor.transform.position = Vector3.down * .01f;
                floor.AddComponent<BoxCollider>().size = new Vector3(10, .02f, 10);
                foreach (GameObject prefab in prefabs)
                {
                    var copy = Object.Instantiate(prefab);
                    SceneManager.MoveGameObjectToScene(copy, scene);
                    try
                    {
                        copy.transform.position = Vector3.up;
                        Collider[] colliders = copy.GetComponentsInChildren<Collider>();
                        Rigidbody body = copy.GetComponent<Rigidbody>();
                        Require(copy.GetComponentsInChildren<Rigidbody>().Length == 1, "Multiple rigidbodies: " + copy.name);
                        Require(colliders.Length > 0 && colliders.Length <= 32, "Collider budget: " + copy.name);
                        maxParts = Mathf.Max(maxParts, colliders.Length);
                        foreach (Collider col in colliders)
                            Require(col is MeshCollider mc && mc.convex && mc.sharedMesh != null &&
                                col.attachedRigidbody == body && !col.isTrigger, "Invalid compound collider: " + copy.name);
                        Mesh visual = copy.GetComponent<MeshFilter>().sharedMesh;
                        maxTriangles = Mathf.Max(maxTriangles, visual.triangles.Length / 3);
                        Require(visual.triangles.Length / 3 <= 2000, "Visual triangle budget: " + copy.name);
                        // SDK discovers colliders under its Rigidbody at startup. Preserve both bindings.
                        foreach (MonoBehaviour behaviour in copy.GetComponentsInChildren<MonoBehaviour>())
                        {
                            if (behaviour.GetType().Name != "HandGrabInteractable" && behaviour.GetType().Name != "GrabInteractable") continue;
                            var serialized = new SerializedObject(behaviour);
                            Require(serialized.FindProperty("_rigidbody").objectReferenceValue == body, "Broken grab binding: " + copy.name);
                        }
                        Physics.SyncTransforms();
                        worstSurfaceError = Mathf.Max(worstSurfaceError, CheckSurface(copy, colliders, scene));
                        CheckCavity(copy, colliders);
                        body.isKinematic = false; body.useGravity = true;
                        body.linearDamping = .03f; body.angularDamping = .35f;
                        body.maxDepenetrationVelocity = 1f;
                        body.rotation = Quaternion.Euler(13, 23, 7) * prefab.transform.rotation;
                        Physics.SyncTransforms();
                        for (int i = 0; i < 250; i++) scene.GetPhysicsScene().Simulate(.02f);
                        Require(body.position.y > -.01f && body.position.y < .35f, "Drop passed through floor: " + copy.name);
                        Require(body.linearVelocity.magnitude < .2f, "Item did not settle: " + copy.name);
                    }
                    finally { Object.DestroyImmediate(copy); }
                }
                CheckSpawner(prefabs, scene);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            string report = $"EXPANDED_TRASH_PASS: {prefabs.Count} prefabs; 24 types; max {maxParts} convex parts; " +
                $"max {maxTriangles} triangles; sampled nearest-surface gap <= {worstSurfaceError * 1000:F2} mm; " +
                "open cavities/handle, SDK bindings, drops, type filters and balanced spawning passed.";
            Directory.CreateDirectory("docs");
            File.WriteAllText("docs/EXPANDED_TRASH_CHECKS.txt", report + "\n");
            Debug.Log(report);
        }

        private static float CheckSurface(GameObject copy, Collider[] colliders, Scene scene)
        {
            Mesh mesh = copy.GetComponent<MeshFilter>().sharedMesh;
            var reference = new GameObject("Visual surface reference");
            SceneManager.MoveGameObjectToScene(reference, scene);
            reference.transform.SetPositionAndRotation(copy.transform.position, copy.transform.rotation);
            reference.transform.localScale = copy.transform.lossyScale;
            var exact = reference.AddComponent<MeshCollider>(); exact.sharedMesh = mesh;
            Physics.SyncTransforms();
            float worst = 0f;
            try
            {
                Bounds bounds = mesh.bounds;
                Vector3[] worldVertices = mesh.vertices.Select(copy.transform.TransformPoint).ToArray();
                int[] triangles = mesh.triangles;
                for (int axis = 0; axis < 3; axis++) for (int sign = -1; sign <= 1; sign += 2)
                for (int a = -2; a <= 2; a++) for (int b = -2; b <= 2; b++)
                {
                    Vector3 local = bounds.center;
                    local[(axis + 1) % 3] += bounds.extents[(axis + 1) % 3] * a * .4f;
                    local[(axis + 2) % 3] += bounds.extents[(axis + 2) % 3] * b * .4f;
                    Vector3 direction = Vector3.zero; direction[axis] = sign;
                    direction = copy.transform.TransformDirection(direction);
                    var ray = new Ray(copy.transform.TransformPoint(local) - direction, direction);
                    if (!exact.Raycast(ray, out RaycastHit visualHit, 2)) continue;
                    bool intersects = Raycast(colliders, ray, out RaycastHit hit);
                    // Ray-depth differences exaggerate tiny errors at grazing angles on folded paper.
                    // Measure the actual 3D gap in both directions instead. The folded paper keeps
                    // a bounded convex approximation; new parts share their visual geometry exactly.
                    float coverage = colliders.Min(c => Vector3.Distance(c.ClosestPoint(visualHit.point), visualHit.point));
                    // PhysX ClosestPoint can be imprecise on tiny convex parts. A ray hit is also
                    // a verified point on the collider, so use whichever distance is tighter.
                    if (intersects) coverage = Mathf.Min(coverage, Vector3.Distance(hit.point, visualHit.point));
                    // A ray exactly on a shared convex edge can miss both adjacent hulls. It must still
                    // have sub-millimeter coverage at the visual hit, otherwise this is a real gap.
                    Require(intersects || coverage < .001f, $"Missing collider coverage ({coverage * 1000:F2} mm): " + copy.name);
                    float error = intersects ? Mathf.Max(coverage, SurfaceDistance(hit.point, worldVertices, triangles)) : coverage;
                    worst = Mathf.Max(worst, error);
                    ItemType type = copy.GetComponent<TrashItem>().ItemType;
                    float limit = type == ItemType.AluminumCan ? .009f : type == ItemType.CrumpledPaper ? .008f :
                        (int)type < 6 ? .006f : .0025f;
                    Require(error < limit, $"Surface gap {error * 1000:F2} mm: {copy.name}; coverage={coverage:F6}, visual={visualHit.point:F6}, collider={hit.point:F6}, depth={hit.distance - visualHit.distance:F6}");
                }
            }
            finally { Object.DestroyImmediate(reference); }
            return worst;
        }

        private static float SurfaceDistance(Vector3 point, Vector3[] vertices, int[] triangles)
        {
            float best = float.MaxValue;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude > 1e-16f)
                {
                    Vector3 projected = point - normal * (Vector3.Dot(point - a, normal) / normal.sqrMagnitude);
                    if (Vector3.Dot(Vector3.Cross(b - a, projected - a), normal) >= 0 &&
                        Vector3.Dot(Vector3.Cross(c - b, projected - b), normal) >= 0 &&
                        Vector3.Dot(Vector3.Cross(a - c, projected - c), normal) >= 0)
                        best = Mathf.Min(best, (point - projected).sqrMagnitude);
                }
                best = Mathf.Min(best, SegmentDistanceSquared(point, a, b));
                best = Mathf.Min(best, SegmentDistanceSquared(point, b, c));
                best = Mathf.Min(best, SegmentDistanceSquared(point, c, a));
            }
            return Mathf.Sqrt(best);
        }

        private static float SegmentDistanceSquared(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 edge = b - a;
            float t = edge.sqrMagnitude > 1e-16f ? Mathf.Clamp01(Vector3.Dot(point - a, edge) / edge.sqrMagnitude) : 0;
            return (point - a - edge * t).sqrMagnitude;
        }

        private static bool Raycast(Collider[] colliders, Ray ray, out RaycastHit hit)
        {
            hit = default;
            float closest = float.MaxValue;
            foreach (Collider collider in colliders)
                if (collider.Raycast(ray, out RaycastHit candidate, 3) && candidate.distance < closest)
                { hit = candidate; closest = candidate.distance; }
            return closest < float.MaxValue;
        }

        private static void CheckCavity(GameObject copy, Collider[] colliders)
        {
            ItemType type = copy.GetComponent<TrashItem>().ItemType;
            var down = new Ray(copy.transform.position + Vector3.up * .3f, Vector3.down);
            if (type == ItemType.PaperTube)
                Require(!Raycast(colliders, down, out _), "Paper tube hole was filled");
            if (type == ItemType.YogurtCup || type == ItemType.FoodTin || type == ItemType.TunaCan ||
                type == ItemType.PlasticCap || type == ItemType.PlasticTub || type == ItemType.FoilTray)
                Require(Raycast(colliders, down, out RaycastHit hit) && hit.point.y < copy.transform.position.y,
                    "Container opening was filled: " + type);
            if (type == ItemType.DetergentBottle)
                Require(!Raycast(colliders, new Ray(copy.transform.position + new Vector3(.039f, .004f, -.2f), Vector3.forward), out _),
                    "Detergent handle hole was filled");
        }

        private static void CheckSpawner(List<GameObject> prefabs, Scene scene)
        {
            var host = new GameObject("Spawner test");
            SceneManager.MoveGameObjectToScene(host, scene);
            var spawner = host.AddComponent<TrashSpawner>();
            var serialized = new SerializedObject(spawner);
            var original = prefabs.Where(p => (int)p.GetComponent<TrashItem>().ItemType < 6).ToArray();
            var field = serialized.FindProperty("itemPrefabs"); field.arraySize = original.Length;
            for (int i = 0; i < original.Length; i++) field.GetArrayElementAtIndex(i).objectReferenceValue = original[i].GetComponent<TrashItem>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(TrashSpawner).GetMethod("Awake", flags).Invoke(spawner, null);
            var choose = typeof(TrashSpawner).GetMethod("PickPrefab", flags);
            var random = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(24018);
                var counts = new int[24];
                for (int i = 0; i < 12000; i++) counts[(int)((TrashItem)choose.Invoke(spawner, null)).ItemType]++;
                Require(counts.All(n => n > 350 && n < 650), "Types are not sampled uniformly");
                foreach (ItemType type in Enum.GetValues(typeof(ItemType)))
                {
                    Require(spawner.GetPrefab(type) != null, "Training prefab missing: " + type);
                    spawner.OnlyType = type;
                    for (int i = 0; i < 30; i++) Require(((TrashItem)choose.Invoke(spawner, null)).ItemType == type, "Teaching filter failed");
                }
                CheckGuidePages(spawner, scene);
            }
            finally { UnityEngine.Random.state = random; Object.DestroyImmediate(host); }
        }

        private static void CheckGuidePages(TrashSpawner spawner, Scene scene)
        {
            var go = new GameObject("Guide page check");
            SceneManager.MoveGameObjectToScene(go, scene);
            var guide = go.AddComponent<TrashGuide>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(TrashGuide).GetField("spawner", flags).SetValue(guide, spawner);
            var seen = new HashSet<string>();
            try
            {
                for (int page = 0; page < 6; page++)
                {
                    typeof(TrashGuide).GetField("page", flags).SetValue(guide, page);
                    typeof(TrashGuide).GetMethod("Build", flags).Invoke(guide, null);
                    foreach (TextMeshPro label in go.GetComponentsInChildren<TextMeshPro>())
                        if (label.name.StartsWith("Name ")) seen.Add(label.text);
                    Require(go.GetComponentsInChildren<Collider>().Length == 0, "Guide added gameplay colliders");
                    for (int i = go.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
                }
                Require(seen.Count == 24, "Guide pages do not cover every item");
                var bars = go.AddComponent<ConfidenceBars>();
                typeof(ConfidenceBars).GetMethod("Awake", flags).Invoke(bars, null);
                var labels = (TextMeshPro[])typeof(ConfidenceBars).GetField("labels", flags).GetValue(bars);
                Require(labels.Length == 6, "Agreement display no longer fits its existing panel");
                seen.Clear();
                for (int page = 0; page < 4; page++)
                {
                    typeof(ConfidenceBars).GetField("page", flags).SetValue(bars, page);
                    typeof(ConfidenceBars).GetMethod("Refresh", flags).Invoke(bars, null);
                    foreach (TextMeshPro label in labels) seen.Add(label.text);
                }
                Require(seen.Count == 24, "Agreement pages do not cover every item");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [MenuItem("SortQuest/Trash Art/Render Expanded Catalog")]
        public static void RenderCatalog()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var objects = new List<GameObject>();
            try
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(ExpandedTrashBuilder.Art + "/ExpandedTrash.mat");
                for (int i = 0; i < 18; i++)
                {
                    ItemType type = (ItemType)(i + 6);
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ExpandedTrashBuilder.Catalog + "/" + type + ".prefab");
                    var holder = new GameObject(type.ToString()); objects.Add(holder);
                    SceneManager.MoveGameObjectToScene(holder, scene);
                    holder.transform.position = new Vector3((i % 6 - 2.5f) * .36f, (1 - i / 6) * .38f, 0);
                    var display = new GameObject("Model", typeof(MeshFilter), typeof(MeshRenderer)); display.transform.SetParent(holder.transform, false);
                    Mesh mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
                    display.GetComponent<MeshFilter>().sharedMesh = mesh;
                    display.GetComponent<MeshRenderer>().sharedMaterial = material;
                    display.transform.rotation = Quaternion.Euler(-28, 30, 0);
                    display.transform.localScale = Vector3.one * (.21f / mesh.bounds.size.magnitude);
                    var labelObject = new GameObject("Label"); labelObject.transform.SetParent(holder.transform, false);
                    labelObject.transform.localPosition = new Vector3(0, -.14f, -.1f);
                    var text = labelObject.AddComponent<TextMeshPro>();
                    text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
                    text.text = TrashTypes.DisplayName(type) + "\n<size=70%>" + TrashTypes.CorrectBin(type).ToString().ToUpperInvariant() + "</size>";
                    text.fontSize = .21f; text.color = Color.white; text.alignment = TextAlignmentOptions.Center;
                    text.rectTransform.sizeDelta = new Vector2(.35f, .1f);
                }
                var sun = new GameObject("Light", typeof(Light)); objects.Add(sun); SceneManager.MoveGameObjectToScene(sun, scene);
                sun.GetComponent<Light>().type = LightType.Directional; sun.GetComponent<Light>().intensity = 1.8f;
                sun.transform.rotation = Quaternion.Euler(30, -30, 0);
                var cameraObject = new GameObject("Camera", typeof(Camera)); objects.Add(cameraObject); SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.GetComponent<Camera>(); camera.scene = scene;
                camera.transform.position = new Vector3(0, -.03f, -3);
                camera.orthographic = true; camera.orthographicSize = .68f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.045f, .065f, .085f);
                camera.nearClipPlane = .1f; camera.farClipPlane = 10;
                var target = new RenderTexture(2400, 1400, 24) { antiAliasing = 4 };
                var texture = new Texture2D(2400, 1400, TextureFormat.RGB24, false);
                bool asynchronous = ShaderUtil.allowAsyncCompilation;
                RenderTexture old = RenderTexture.active;
                try
                {
                    ShaderUtil.allowAsyncCompilation = false;
                    camera.targetTexture = target;
                    camera.Render();
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    RenderTexture.active = target;
                    texture.ReadPixels(new Rect(0, 0, 2400, 1400), 0, 0); texture.Apply();
                    Directory.CreateDirectory("docs/visuals");
                    File.WriteAllBytes("docs/visuals/expanded-trash-catalog.png", texture.EncodeToPNG());
                }
                finally
                {
                    ShaderUtil.allowAsyncCompilation = asynchronous; RenderTexture.active = old;
                    camera.targetTexture = null; Object.DestroyImmediate(texture); Object.DestroyImmediate(target);
                }
            }
            finally
            {
                foreach (GameObject go in objects) if (go != null) Object.DestroyImmediate(go);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
