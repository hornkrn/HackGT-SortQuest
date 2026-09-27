using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SortQuest.Editor
{
    public static class DropPhysicsSetup
    {
        [MenuItem("SortQuest/Apply Natural Drop Physics")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            if (!AssetDatabase.IsValidFolder("Assets/Physics")) AssetDatabase.CreateFolder("Assets", "Physics");
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs", "Assets/Resources/TrashCatalog" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var item = root.GetComponent<TrashItem>();
                    if (item == null) continue;
                    string materialPath = "Assets/Physics/" + item.ItemType + ".physicMaterial";
                    var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(materialPath);
                    if (material == null) { material = new PhysicsMaterial(item.ItemType.ToString()); AssetDatabase.CreateAsset(material, materialPath); }
                    // Dry, used packaging: paper grips, metal slides, hollow plastic gives slightly.
                    bool paper = item.CorrectBin == BinType.Paper;
                    bool plastic = item.CorrectBin == BinType.Plastic;
                    material.staticFriction = paper ? .65f : plastic ? .45f : .4f;
                    material.dynamicFriction = paper ? .5f : plastic ? .3f : .25f;
                    material.bounciness = paper ? .015f : plastic ? .12f : .045f;
                    material.frictionCombine = PhysicsMaterialCombine.Average;
                    material.bounceCombine = PhysicsMaterialCombine.Average;
                    EditorUtility.SetDirty(material);
                    foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                        if (!collider.isTrigger && collider.attachedRigidbody == item.GetComponent<Rigidbody>()) collider.sharedMaterial = material;
                    var serialized = new SerializedObject(item);
                    serialized.FindProperty("maxReleaseSpeed").floatValue = 4.5f;
                    serialized.FindProperty("maxReleaseSpin").floatValue = 16f;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    count++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("NATURAL_DROP_SETUP: " + count + " trash prefabs updated.");
        }

        [MenuItem("SortQuest/Validate Drop Physics")]
        public static void Validate()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            var scene = EditorSceneManager.NewPreviewScene();
            int count = 0;
            try
            {
                Require(scene.GetPhysicsScene() != Physics.defaultPhysicsScene, "Physics check must be isolated from the game scene");
                var floor = new GameObject("Thin floor", typeof(BoxCollider));
                SceneManager.MoveGameObjectToScene(floor, scene);
                floor.GetComponent<BoxCollider>().size = new Vector3(20, .02f, 20);
                floor.transform.position = new Vector3(0, -.01f, 0);
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs", "Assets/Resources/TrashCatalog" }))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                    if (prefab.GetComponent<TrashItem>() == null) continue;
                    var clone = Object.Instantiate(prefab);
                    SceneManager.MoveGameObjectToScene(clone, scene);
                    try
                    {
                        var item = clone.GetComponent<TrashItem>();
                        Invoke(item, "Awake");
                        Invoke(item, "EndHold");
                        var body = item.Body;
                        body.position = new Vector3(0, 1.2f, 0);
                        body.rotation = Quaternion.Euler(17, 28, 11);
                        body.linearVelocity = new Vector3(.8f, .2f, .1f);
                        body.angularVelocity = new Vector3(0, 3, 0);
                        Invoke(item, "FixedUpdate");
                        Require((body.linearVelocity - new Vector3(.8f, .2f, .1f)).sqrMagnitude < .00001f, "Normal release momentum changed: " + clone.name);
                        Invoke(item, "EndHold");
                        body.linearVelocity = Vector3.right * 40;
                        body.angularVelocity = Vector3.up * 90;
                        Invoke(item, "FixedUpdate");
                        Require(body.linearVelocity.magnitude <= 4.501f && body.angularVelocity.magnitude <= 16.001f, "Release safety limits failed");
                        body.linearVelocity = Vector3.zero;
                        body.angularVelocity = Vector3.zero;
                        Physics.SyncTransforms();
                        var physics = scene.GetPhysicsScene();
                        for (int step = 0; step < 10; step++) physics.Simulate(.02f);
                        Require(body.position.y < 1.1f && body.position.y > .85f, "Gravity response incorrect: " + clone.name);
                        for (int step = 0; step < 140; step++) physics.Simulate(.02f);
                        Require(body.position.y > -.02f && body.position.y < .4f, "Thin-floor collision failed: " + clone.name);
                        Require(body.linearVelocity.magnitude < .15f, "Drop did not settle: " + clone.name);
                        Require(body.interpolation == RigidbodyInterpolation.Interpolate, "Interpolation missing");
                        count++;
                    }
                    finally { Object.DestroyImmediate(clone); }
                }
                Require(count >= 48, "Expected all 48 trash variants");
                Debug.Log("DROP_PHYSICS_PASS: " + count + " prefabs; preserved normal throws, capped outliers, gravity, thin-floor landing and settling.");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        private static void Invoke(TrashItem item, string method) => typeof(TrashItem).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(item, null);
        private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
