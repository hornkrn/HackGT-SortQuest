using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Grab;
using Oculus.Interaction.HandGrab;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SortQuest
{
    /// <summary>
    /// Menu items that build the milestone 1 scene objects and trash item prefabs, add grasp recording (milestone 2),
    /// add the robot gripper (milestone 3), and add the game manager (milestone 4).
    /// Safe to run more than once: objects and assets that already exist (matched by name or path)
    /// are kept as they are, and only empty references are filled in.
    /// Never touches the camera rig or hand tracking building blocks.
    /// </summary>
    public static class Milestone1SceneBuilder
    {
        private const string PrefabFolder = "Assets/Prefabs";
        private const string MaterialFolder = "Assets/Materials";

        // Template_HandGrabInteraction.prefab in com.meta.xr.sdk.interaction (Templates.HandGrabInteractable).
        private const string HandGrabTemplateGuid = "6ee61821e0d5b094a8d732834b365b21";
        private const string HandGrabChildName = "ISDK_HandGrabInteraction";

        private struct ItemSpec
        {
            public string Name;
            public ItemType Type;
            public PrimitiveType Shape;
            public Vector3 Scale;
            public Vector3 Euler;
            public float Mass;
            public Color Color;
        }

        private static readonly ItemSpec[] Items =
        {
            new ItemSpec { Name = "AluminumCan", Type = ItemType.AluminumCan, Shape = PrimitiveType.Cylinder,
                Scale = new Vector3(0.066f, 0.061f, 0.066f), Mass = 0.015f, Color = new Color(0.75f, 0.78f, 0.8f) },
            new ItemSpec { Name = "PlasticBottle", Type = ItemType.PlasticBottle, Shape = PrimitiveType.Cylinder,
                Scale = new Vector3(0.07f, 0.11f, 0.07f), Mass = 0.03f, Color = new Color(0.55f, 0.8f, 0.95f) },
            new ItemSpec { Name = "CardboardBox", Type = ItemType.CardboardBox, Shape = PrimitiveType.Cube,
                Scale = new Vector3(0.25f, 0.15f, 0.2f), Mass = 0.2f, Color = new Color(0.65f, 0.47f, 0.28f) },
            new ItemSpec { Name = "CrumpledPaper", Type = ItemType.CrumpledPaper, Shape = PrimitiveType.Sphere,
                Scale = new Vector3(0.08f, 0.08f, 0.08f), Mass = 0.01f, Color = new Color(0.95f, 0.95f, 0.9f) },
            // 1.5x real AA size so it's easier to pinch; lies on its side.
            new ItemSpec { Name = "BatteryAA", Type = ItemType.BatteryAA, Shape = PrimitiveType.Cylinder,
                Scale = new Vector3(0.021f, 0.0375f, 0.021f), Euler = new Vector3(90f, 0f, 0f), Mass = 0.023f,
                Color = new Color(0.2f, 0.2f, 0.2f) },
            new ItemSpec { Name = "PowerBank", Type = ItemType.PowerBank, Shape = PrimitiveType.Cube,
                Scale = new Vector3(0.07f, 0.025f, 0.14f), Mass = 0.2f, Color = new Color(0.3f, 0.3f, 0.35f) },
        };

        private static readonly (string Name, BinType Type, Vector3 Position)[] Bins =
        {
            ("Bin_Metal", BinType.Metal, new Vector3(-1.0f, 0f, 0.2f)),
            ("Bin_Plastic", BinType.Plastic, new Vector3(-0.55f, 0f, 0.2f)),
            ("Bin_Paper", BinType.Paper, new Vector3(0.55f, 0f, 0.2f)),
            ("Bin_Hazardous", BinType.Hazardous, new Vector3(1.0f, 0f, 0.2f)),
        };

        private static readonly (string Name, Vector3 Position, Vector3 Scale)[] BinParts =
        {
            ("Bottom", new Vector3(0f, 0.02f, 0f), new Vector3(0.4f, 0.04f, 0.4f)),
            ("Wall_N", new Vector3(0f, 0.3f, 0.19f), new Vector3(0.4f, 0.6f, 0.02f)),
            ("Wall_S", new Vector3(0f, 0.3f, -0.19f), new Vector3(0.4f, 0.6f, 0.02f)),
            ("Wall_E", new Vector3(0.19f, 0.3f, 0f), new Vector3(0.02f, 0.6f, 0.4f)),
            ("Wall_W", new Vector3(-0.19f, 0.3f, 0f), new Vector3(0.02f, 0.6f, 0.4f)),
        };

        [MenuItem("SortQuest/Build Milestone 1 Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("SortQuest", "Exit Play mode first.", "OK");
                return;
            }
            Scene scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog("SortQuest", "Open and save the main scene first, then run this again.", "OK");
                return;
            }

            EnsureFolder(PrefabFolder);
            EnsureFolder(MaterialFolder);

            TrashItem[] prefabs = BuildItemPrefabs();
            if (prefabs == null)
            {
                return;
            }

            BuildFloor();
            ScoreBoard scoreBoard = BuildScoreBoard();
            ConveyorBelt belt = BuildBelt();
            foreach (var bin in Bins)
            {
                BuildBin(bin.Name, bin.Type, bin.Position, scoreBoard);
            }
            BuildSpawner(belt, prefabs, scoreBoard);
            BuildHeldItemLabel();

            if (Object.FindAnyObjectByType<HandGrabInteractor>(FindObjectsInactive.Include) == null)
            {
                Debug.LogWarning("[SortQuest] No HandGrabInteractor found in the scene, so hands can't grab items. " +
                                 "Add the Grab Interaction building block to the camera rig.");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SortQuest] Milestone 1 scene built and saved ({scene.path}).");
        }

        [MenuItem("SortQuest/Add Grasp Recording (Milestone 2)")]
        public static void AddGraspRecording()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("SortQuest", "Exit Play mode first.", "OK");
                return;
            }
            Scene scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog("SortQuest", "Open and save the main scene first, then run this again.", "OK");
                return;
            }
            TrashSpawner spawner = Object.FindAnyObjectByType<TrashSpawner>();
            if (spawner == null)
            {
                EditorUtility.DisplayDialog("SortQuest", "Run SortQuest > Build Milestone 1 Scene first.", "OK");
                return;
            }

            GameObject go = GetOrCreate("GraspRecording", null, null, out _);
            GraspDataset dataset = GetOrAdd<GraspDataset>(go, out _);
            GraspRecorder recorder = GetOrAdd<GraspRecorder>(go, out _);
            SetIfEmpty(recorder, "dataset", dataset);
            SetIfEmpty(recorder, "spawner", spawner);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[SortQuest] Grasp recording added and scene saved ({scene.path}).");
        }

        [MenuItem("SortQuest/Add Robot Gripper (Milestone 3)")]
        public static void AddRobotGripper()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("SortQuest", "Exit Play mode first.", "OK");
                return;
            }
            Scene scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog("SortQuest", "Open and save the main scene first, then run this again.", "OK");
                return;
            }
            TrashSpawner spawner = Object.FindAnyObjectByType<TrashSpawner>();
            ConveyorBelt belt = Object.FindAnyObjectByType<ConveyorBelt>();
            GraspDataset dataset = Object.FindAnyObjectByType<GraspDataset>();
            if (spawner == null || belt == null || dataset == null)
            {
                EditorUtility.DisplayDialog("SortQuest",
                    "Run Build Milestone 1 Scene and Add Grasp Recording (Milestone 2) first.", "OK");
                return;
            }

            // Root sits at the grasp point; rotated so its approach direction (+Z) points down at the belt.
            GameObject go = GetOrCreate("RobotGripper", null, null, out bool created);
            if (created)
            {
                go.transform.SetPositionAndRotation(new Vector3(0.9f, 1.3f, 0.6f), Quaternion.Euler(90f, 0f, 0f));
            }
            Rigidbody body = GetOrAdd<Rigidbody>(go, out bool bodyAdded);
            if (bodyAdded)
            {
                body.isKinematic = true;
                body.useGravity = false;
            }
            GraspPolicy policy = GetOrAdd<GraspPolicy>(go, out _);
            RobotGripper robot = GetOrAdd<RobotGripper>(go, out _);

            // Visual parts only: colliders are removed so the gripper never pushes items around.
            Material material = GetOrCreateMaterial("Mat_Robot", new Color(1f, 0.55f, 0.1f));
            Transform palm = BuildRobotPart(go.transform, "Palm", material);
            Transform fingerLeft = BuildRobotPart(go.transform, "FingerLeft", material);
            Transform fingerRight = BuildRobotPart(go.transform, "FingerRight", material);

            SetIfEmpty(policy, "dataset", dataset);
            SetIfEmpty(robot, "policy", policy);
            SetIfEmpty(robot, "dataset", dataset);
            SetIfEmpty(robot, "belt", belt);
            SetIfEmpty(robot, "spawner", spawner);
            SetIfEmpty(robot, "palm", palm);
            SetIfEmpty(robot, "fingerLeft", fingerLeft);
            SetIfEmpty(robot, "fingerRight", fingerRight);

            if (HasTmpEssentials())
            {
                GameObject status = GetOrCreate("RobotStatus", null, null, out bool statusCreated);
                TextMeshPro text = GetOrAdd<TextMeshPro>(status, out bool textAdded);
                GetOrAdd<Billboard>(status, out _);
                if (statusCreated || textAdded)
                {
                    status.transform.position = new Vector3(0.9f, 1.65f, 0.9f);
                    text.rectTransform.sizeDelta = new Vector2(1.6f, 0.5f);
                    text.fontSize = 0.9f;
                    text.alignment = TextAlignmentOptions.Center;
                    text.color = new Color(1f, 0.7f, 0.3f);
                    text.text = "ROBOT";
                }
                SetIfEmpty(robot, "statusText", text);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[SortQuest] Robot gripper added and scene saved ({scene.path}).");
        }

        [MenuItem("SortQuest/Add Game Manager (Milestone 4)")]
        public static void AddGameManager()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("SortQuest", "Exit Play mode first.", "OK");
                return;
            }
            Scene scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog("SortQuest", "Open and save the main scene first, then run this again.", "OK");
                return;
            }
            TrashSpawner spawner = Object.FindAnyObjectByType<TrashSpawner>();
            RobotGripper robot = Object.FindAnyObjectByType<RobotGripper>();
            if (spawner == null || robot == null)
            {
                EditorUtility.DisplayDialog("SortQuest", "Run the Milestone 1, 2, and 3 menu items first.", "OK");
                return;
            }

            GameObject go = GetOrCreate("GameManager", null, null, out _);
            GameManager manager = GetOrAdd<GameManager>(go, out _);
            SetIfEmpty(manager, "spawner", spawner);
            SetIfEmpty(manager, "robot", robot);
            SetIfEmpty(manager, "scoreBoard", Object.FindAnyObjectByType<ScoreBoard>());
            SetIfEmpty(manager, "dataset", Object.FindAnyObjectByType<GraspDataset>());

            if (HasTmpEssentials())
            {
                GameObject status = GetOrCreate("GameStatus", null, null, out bool statusCreated);
                TextMeshPro text = GetOrAdd<TextMeshPro>(status, out bool textAdded);
                GetOrAdd<Billboard>(status, out _);
                if (statusCreated || textAdded)
                {
                    status.transform.position = new Vector3(0f, 2.2f, 1.6f);
                    text.rectTransform.sizeDelta = new Vector2(3f, 0.9f);
                    text.fontSize = 1.3f;
                    text.alignment = TextAlignmentOptions.Center;
                    text.text = "SORTQUEST";
                }
                SetIfEmpty(manager, "statusText", text);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[SortQuest] Game manager added and scene saved ({scene.path}).");
        }

        private static Transform BuildRobotPart(Transform parent, string name, Material material)
        {
            GameObject part = GetOrCreate(name, parent, PrimitiveType.Cube, out bool created);
            if (created)
            {
                Object.DestroyImmediate(part.GetComponent<Collider>());
                part.GetComponent<Renderer>().sharedMaterial = material;
            }
            return part.transform;
        }

        [MenuItem("SortQuest/Open Grasp Data Folder")]
        public static void OpenGraspDataFolder()
        {
            EditorUtility.RevealInFinder(Application.persistentDataPath + "/");
        }

        // ---------- Item prefabs ----------

        private static TrashItem[] BuildItemPrefabs()
        {
            string templatePath = AssetDatabase.GUIDToAssetPath(HandGrabTemplateGuid);
            GameObject handGrabTemplate = AssetDatabase.LoadAssetAtPath<GameObject>(templatePath);
            if (handGrabTemplate == null)
            {
                EditorUtility.DisplayDialog("SortQuest",
                    "Couldn't find the Interaction SDK's HandGrabInteraction template. Nothing was built.", "OK");
                return null;
            }

            return Items.Select(spec => BuildItemPrefab(spec, handGrabTemplate)).ToArray();
        }

        private static TrashItem BuildItemPrefab(ItemSpec spec, GameObject handGrabTemplate)
        {
            string path = $"{PrefabFolder}/{spec.Name}.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                Debug.Log($"[SortQuest] Keeping existing prefab {path}");
                return existing.GetComponent<TrashItem>();
            }

            GameObject go = GameObject.CreatePrimitive(spec.Shape);
            go.name = spec.Name;
            go.transform.localScale = spec.Scale;
            go.transform.rotation = Quaternion.Euler(spec.Euler);
            go.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("Mat_" + spec.Name, spec.Color);

            // Cylinder primitives come with a capsule collider; use a convex mesh so they stand flat.
            if (spec.Shape == PrimitiveType.Cylinder)
            {
                Object.DestroyImmediate(go.GetComponent<Collider>());
                MeshCollider meshCollider = go.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
                meshCollider.convex = true;
            }

            Rigidbody body = go.AddComponent<Rigidbody>();
            body.mass = spec.Mass;
            body.useGravity = true;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            Grabbable grabbable = AddGrabInteraction(go, body, handGrabTemplate);

            TrashItem item = go.AddComponent<TrashItem>();
            var so = new SerializedObject(item);
            so.FindProperty("itemType").enumValueIndex = (int)spec.Type;
            so.FindProperty("grabbable").objectReferenceValue = grabbable;
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            Debug.Log($"[SortQuest] Created prefab {path}");
            return prefab.GetComponent<TrashItem>();
        }

        /// <summary>
        /// Mirrors GrabWizard.Create() in com.meta.xr.sdk.interaction (Editor/QuickActions/Scripts/GrabWizard.cs)
        /// with its defaults: Grabbable on the target, the HandGrabInteraction template as a child, and both
        /// interactables pointed at the Rigidbody and Grabbable. Skips AddInteractorsToRig, which edits the camera rig.
        /// </summary>
        private static Grabbable AddGrabInteraction(GameObject target, Rigidbody body, GameObject template)
        {
            Grabbable grabbable = target.AddComponent<Grabbable>();
            grabbable.InjectOptionalTargetTransform(target.transform);

            GameObject obj = Object.Instantiate(template);
            obj.name = HandGrabChildName;
            obj.transform.SetParent(target.transform, false);
            obj.transform.localPosition = Vector3.zero;
            obj.transform.localScale = Vector3.one;
            obj.transform.localRotation = Quaternion.identity;

            HandGrabInteractable handInteractable = obj.GetComponent<HandGrabInteractable>();
            handInteractable.InjectRigidbody(body);
            handInteractable.InjectSupportedGrabTypes(GrabTypeFlags.All);
            handInteractable.InjectOptionalPointableElement(grabbable);

            GrabInteractable grabInteractable = obj.GetComponent<GrabInteractable>();
            grabInteractable.InjectRigidbody(body);
            grabInteractable.InjectOptionalPointableElement(grabbable);

            return grabbable;
        }

        // ---------- Scene objects ----------

        private static void BuildFloor()
        {
            GameObject floor = GetOrCreate("Floor", null, PrimitiveType.Plane, out bool created);
            if (created)
            {
                floor.transform.localScale = new Vector3(2f, 1f, 2f);
                floor.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("Mat_Floor", new Color(0.45f, 0.45f, 0.45f));
            }
        }

        private static ScoreBoard BuildScoreBoard()
        {
            GameObject go = GetOrCreate("ScoreBoard", null, null, out _);
            ScoreBoard scoreBoard = GetOrAdd<ScoreBoard>(go, out _);

            if (!HasTmpEssentials())
            {
                Debug.LogWarning("[SortQuest] TMP Essentials aren't imported, so the score label was skipped. " +
                                 "Use Window > TextMeshPro > Import TMP Essential Resources, then run the builder again.");
                return scoreBoard;
            }

            GameObject label = GetOrCreate("ScoreLabel", go.transform, null, out bool created);
            TextMeshPro text = GetOrAdd<TextMeshPro>(label, out bool added);
            if (created || added)
            {
                label.transform.position = new Vector3(0f, 1.7f, 1.5f);
                text.rectTransform.sizeDelta = new Vector2(3f, 0.6f);
                text.fontSize = 1.2f;
                text.alignment = TextAlignmentOptions.Center;
                text.text = "Score: 0";
            }
            SetIfEmpty(scoreBoard, "scoreText", text);
            return scoreBoard;
        }

        private static ConveyorBelt BuildBelt()
        {
            GameObject go = GetOrCreate("ConveyorBelt", null, null, out bool created);
            if (created)
            {
                go.transform.position = new Vector3(0f, 0f, 0.6f);
            }
            ConveyorBelt belt = GetOrAdd<ConveyorBelt>(go, out _);

            GameObject surface = GetOrCreate("BeltSurface", go.transform, PrimitiveType.Cube, out bool surfaceCreated);
            if (surfaceCreated)
            {
                surface.transform.localPosition = new Vector3(0f, 0.85f, 0f);
                surface.transform.localScale = new Vector3(3f, 0.1f, 0.4f);
                surface.GetComponent<Renderer>().sharedMaterial = GetOrCreateMaterial("Mat_Belt", new Color(0.12f, 0.12f, 0.12f));
            }

            GameObject start = GetOrCreate("StartPoint", go.transform, null, out bool startCreated);
            if (startCreated)
            {
                start.transform.localPosition = new Vector3(-1.4f, 0.9f, 0f);
            }
            GameObject end = GetOrCreate("EndPoint", go.transform, null, out bool endCreated);
            if (endCreated)
            {
                end.transform.localPosition = new Vector3(1.4f, 0.9f, 0f);
            }

            SetIfEmpty(belt, "startPoint", start.transform);
            SetIfEmpty(belt, "endPoint", end.transform);
            return belt;
        }

        private static void BuildBin(string name, BinType type, Vector3 position, ScoreBoard scoreBoard)
        {
            GameObject go = GetOrCreate(name, null, null, out bool created);
            if (created)
            {
                go.transform.position = position;
            }

            Material material = GetOrCreateMaterial("Mat_" + name, TrashTypes.BinColor(type));
            var renderers = new Renderer[BinParts.Length];
            for (int i = 0; i < BinParts.Length; i++)
            {
                var part = BinParts[i];
                GameObject partGo = GetOrCreate(part.Name, go.transform, PrimitiveType.Cube, out bool partCreated);
                if (partCreated)
                {
                    partGo.transform.localPosition = part.Position;
                    partGo.transform.localScale = part.Scale;
                    partGo.GetComponent<Renderer>().sharedMaterial = material;
                }
                renderers[i] = partGo.GetComponent<Renderer>();
            }

            if (go.GetComponent<Collider>() == null)
            {
                BoxCollider trigger = Undo.AddComponent<BoxCollider>(go);
                trigger.center = new Vector3(0f, 0.25f, 0f);
                trigger.size = new Vector3(0.36f, 0.4f, 0.36f);
                trigger.isTrigger = true;
            }

            SortingBin bin = GetOrAdd<SortingBin>(go, out bool binAdded);
            if (created || binAdded)
            {
                var so = new SerializedObject(bin);
                so.FindProperty("binType").enumValueIndex = (int)type;
                so.ApplyModifiedProperties();
            }
            SetIfEmpty(bin, "scoreBoard", scoreBoard);
            SetArrayIfEmpty(bin, "tintRenderers", renderers);

            // Sign on the back edge of the bin; text is filled in at runtime from TrashTypes.
            if (HasTmpEssentials())
            {
                GameObject sign = GetOrCreate("Sign", go.transform, null, out bool signCreated);
                TextMeshPro signText = GetOrAdd<TextMeshPro>(sign, out bool signAdded);
                GetOrAdd<Billboard>(sign, out _);
                if (signCreated || signAdded)
                {
                    sign.transform.localPosition = new Vector3(0f, 0.8f, 0.19f);
                    signText.rectTransform.sizeDelta = new Vector2(0.5f, 0.3f);
                    signText.fontSize = 0.8f;
                    signText.alignment = TextAlignmentOptions.Center;
                    signText.color = TrashTypes.BinColor(type);
                    signText.text = TrashTypes.BinSignText(type);
                }
                SetIfEmpty(bin, "label", signText);
            }
        }

        private static void BuildHeldItemLabel()
        {
            if (!HasTmpEssentials())
            {
                return;
            }
            GameObject go = GetOrCreate("HeldItemLabel", null, null, out bool created);
            TextMeshPro text = GetOrAdd<TextMeshPro>(go, out bool added);
            GetOrAdd<Billboard>(go, out _);
            HeldItemLabel label = GetOrAdd<HeldItemLabel>(go, out _);
            if (created || added)
            {
                text.rectTransform.sizeDelta = new Vector2(0.5f, 0.1f);
                text.fontSize = 0.5f;
                text.alignment = TextAlignmentOptions.Center;
                text.text = "Item";
            }
            SetIfEmpty(label, "text", text);
            SetIfEmpty(label, "spawner", Object.FindAnyObjectByType<TrashSpawner>());
        }

        private static bool HasTmpEssentials()
        {
            return Resources.Load<TMP_Settings>("TMP Settings") != null;
        }

        private static void BuildSpawner(ConveyorBelt belt, TrashItem[] prefabs, ScoreBoard scoreBoard)
        {
            GameObject go = GetOrCreate("TrashSpawner", null, null, out _);
            TrashSpawner spawner = GetOrAdd<TrashSpawner>(go, out _);
            SetIfEmpty(spawner, "belt", belt);
            SetIfEmpty(spawner, "itemParent", go.transform);
            SetIfEmpty(spawner, "scoreBoard", scoreBoard);
            SetArrayIfEmpty(spawner, "itemPrefabs", prefabs);
        }

        // ---------- Helpers ----------

        /// <summary>Finds a root object (parent == null) or a direct child by name, or creates it.</summary>
        private static GameObject GetOrCreate(string name, Transform parent, PrimitiveType? shape, out bool created)
        {
            Transform found = parent != null
                ? parent.Find(name)
                : SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == name)?.transform;
            if (found != null)
            {
                created = false;
                return found.gameObject;
            }

            GameObject go = shape.HasValue ? GameObject.CreatePrimitive(shape.Value) : new GameObject();
            go.name = name;
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }
            Undo.RegisterCreatedObjectUndo(go, "Build Milestone 1 Scene");
            created = true;
            return go;
        }

        private static T GetOrAdd<T>(GameObject go, out bool added) where T : Component
        {
            T component = go.GetComponent<T>();
            added = component == null;
            if (added)
            {
                component = Undo.AddComponent<T>(go);
            }
            return component;
        }

        private static void SetIfEmpty(Component target, string propertyName, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(propertyName);
            if (prop.objectReferenceValue == null)
            {
                prop.objectReferenceValue = value;
                so.ApplyModifiedProperties();
            }
        }

        private static void SetArrayIfEmpty(Component target, string propertyName, Object[] values)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(propertyName);
            for (int i = 0; i < prop.arraySize; i++)
            {
                if (prop.GetArrayElementAtIndex(i).objectReferenceValue != null)
                {
                    return;
                }
            }
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            so.ApplyModifiedProperties();
        }

        private static Material GetOrCreateMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            material = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
            }
        }
    }
}
