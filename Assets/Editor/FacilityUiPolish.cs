using System;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SortQuest.Editor
{
    public static class FacilityUiPolish
    {
        [MenuItem("SortQuest/Polish Facility UI and Surroundings")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/SampleScene.unity") throw new InvalidOperationException("Open SampleScene first.");
            var material = DisplayMaterial("Assets/Resources/FacilityDisplay.mat", Color.white);
            var frame = DisplayMaterial("Assets/Materials/UIFrame.mat", FacilityUi.Frame);
            var face = DisplayMaterial("Assets/Materials/UIFace.mat", FacilityUi.Ink);
            var accent = DisplayMaterial("Assets/Materials/UIAccent.mat", FacilityUi.Accent);
            Text("GameStatus", new Vector3(0, 2.34f, 3.7f), new Vector2(2.7f, .85f), 1.6f, 0);
            Text("ScoreLabel", new Vector3(0, 2.94f, 3.7f), new Vector2(2.7f, .22f), 1.3f, 0);
            Text("RobotStatus", new Vector3(2.65f, 1.23f, 2.55f), new Vector2(1.3f, .46f), .85f, 25);
            Move("RobotAccuracyChart", new Vector3(2.05f, 2.05f, 2.55f), 20);
            Move("LearningBars", new Vector3(-2.05f, 2.12f, 2.55f), -20);
            Move("RobotVisionScreen", new Vector3(-2.05f, 1.19f, 2.55f), -20);
            QuestSceneArt.AddPeripheralDetails();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("FACILITY_UI_SAVED: fixed side displays, compact main status and peripheral scenery.");

            void Text(string name, Vector3 position, Vector2 size, float fontSize, float yaw)
            {
                var go = Move(name, position, yaw);
                var text = go.GetComponent<TMP_Text>();
                text.rectTransform.sizeDelta = size;
                FacilityUi.Style(text, fontSize);
                text.margin = new Vector4(.04f, .015f, .04f, .015f);
                // These panels are generated in the editor and saved, so edit mode matches Play.
                foreach (Transform child in go.transform.Cast<Transform>().ToArray())
                    if (child.name.StartsWith("Display ")) Object.DestroyImmediate(child.gameObject);
                FacilityUi.Panel(go.transform, size + new Vector2(.08f, .04f), Vector3.zero, material);
                foreach (var renderer in go.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!renderer.name.StartsWith("Display ")) continue;
                    renderer.sharedMaterial = renderer.name == "Display face" ? face : renderer.name == "Display frame" ? frame : accent;
                    renderer.SetPropertyBlock(null);
                }
            }
        }
        private static Material DisplayMaterial(string path, Color color)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("SortQuest/Facility Display")); AssetDatabase.CreateAsset(material, path); }
            material.SetColor("_Color", color);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject Move(string name, Vector3 position, float yaw)
        {
            var go = GameObject.Find(name);
            if (go == null) throw new InvalidOperationException("Missing display: " + name);
            Undo.RecordObject(go.transform, "Place facility display");
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            var billboard = go.GetComponent<Billboard>();
            if (billboard != null) billboard.enabled = false;
            return go;
        }

        [MenuItem("SortQuest/Preview Facility UI")]
        public static void Preview()
        {
            if (EditorApplication.isPlaying || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the scene and exit Play mode first.");
            string path = SceneManager.GetActiveScene().path;
            try
            {
                foreach (var catalog in Object.FindObjectsByType<GripperCatalog>(FindObjectsSortMode.None)) Invoke(catalog, "Awake");
                foreach (var chart in Object.FindObjectsByType<AccuracyChart>(FindObjectsSortMode.None))
                {
                    Invoke(chart, "Awake");
                    // Preview-only samples, never added to the dataset or uploaded.
                    foreach (bool success in new[] { false, true, false, true, true, true, false, true, true, true })
                        Invoke(chart, "HandleAttempt", new RobotGripper.Attempt { Success = success, Counts = true });
                    var title = chart.GetComponentsInChildren<TMP_Text>().First(t => t.name == "Title");
                    if (!title.text.Contains("80%")) throw new InvalidOperationException("Chart rolling rate changed");
                }
                foreach (var bars in Object.FindObjectsByType<ConfidenceBars>(FindObjectsSortMode.None)) { Invoke(bars, "Awake"); Invoke(bars, "Refresh"); }
                foreach (var visual in Object.FindObjectsByType<GripperVisual>(FindObjectsSortMode.None)) Invoke(visual, "Awake");
                foreach (var arm in Object.FindObjectsByType<RobotArmDisplay>(FindObjectsSortMode.None)) { Invoke(arm, "Awake"); Invoke(arm, "LateUpdate"); }
                foreach (var menu in Object.FindObjectsByType<MainMenu>(FindObjectsSortMode.None))
                {
                    Invoke(menu, "Awake"); Invoke(menu, "Build");
                    foreach (var block in menu.GetComponentsInChildren<ChoiceBlock>(true)) Invoke(block, "Awake");
                    Invoke(menu, "RefreshLabels");
                    menu.transform.Find("ResultsChoices").gameObject.SetActive(false);
                }
                foreach (var screen in Object.FindObjectsByType<RobotVisionScreen>(FindObjectsSortMode.None)) { Invoke(screen, "Awake"); Invoke(screen, "Start"); }
                Set("GameStatus", "YOUR TURN   0:45\n<size=55%>Sort by material. Every good grasp teaches the robot.</size>");
                Set("ScoreLabel", "SCORE 120   <size=60%>12 correct • 1 wrong • 0 missed</size>");
                Set("RobotStatus", "STANDARD GRIPPER\n<size=60%>8 / 10 successful grasps\nReady for the next item</size>");
                foreach (var text in Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None)) text.ForceMeshUpdate(true, true);
                // The menu is hidden during sorting; render that unobstructed view first.
                var menuForPreview = Object.FindAnyObjectByType<MainMenu>();
                if (menuForPreview != null) menuForPreview.gameObject.SetActive(false);
                Capture(new Vector3(0, 1.7f, -1.45f), new Vector3(0, 1.8f, 2.0f), "sortquest-ui-gameplay.png");
                if (menuForPreview != null) menuForPreview.gameObject.SetActive(true);
                var main = menuForPreview;
                if (main != null) Capture(main.transform.position + new Vector3(0, .5f, -.95f), main.transform.position + new Vector3(0, .15f, 0), "sortquest-ui-menu.png");
                // Exercise real status formatting without entering a game state or writing records.
                var game = Object.FindAnyObjectByType<GameManager>();
                Invoke(game, "Awake");
                var status = GameObject.Find("GameStatus").GetComponent<TMP_Text>();
                foreach (GameState state in Enum.GetValues(typeof(GameState)))
                {
                    typeof(GameManager).GetProperty("State").SetValue(game, state);
                    Invoke(game, "RefreshStatus");
                    status.ForceMeshUpdate(true, true);
                    if (status.isTextOverflowing) throw new InvalidOperationException("Status text clips in " + state);
                }
                if (main != null)
                {
                    main.transform.Find("MenuChoices").gameObject.SetActive(false);
                    main.transform.Find("ResultsChoices").gameObject.SetActive(true);
                    foreach (var text in main.GetComponentsInChildren<TMP_Text>())
                    {
                        text.ForceMeshUpdate(true, true);
                        if (text.isTextOverflowing) throw new InvalidOperationException("Result control text clips: " + text.text);
                    }
                }
                Capture(new Vector3(0, 1.7f, -1.45f), new Vector3(0, 1.8f, 2.0f), "sortquest-ui-results.png");
                Debug.Log("FACILITY_UI_PREVIEW_PASS: chart 80% rolling rate; all status states fit; preview data is temporary.");
            }
            finally { EditorSceneManager.OpenScene(path); }
        }
        private static void Set(string name, string content) { GameObject.Find(name).GetComponent<TMP_Text>().text = content; }
        private static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
        private static void Capture(Vector3 p, Vector3 target, string file) => typeof(SceneVisualPolish).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { p, target, file });
    }
}
