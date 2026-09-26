using System;
using System.Linq;
using System.Text;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using Object = UnityEngine.Object;

namespace SortQuest.Editor
{
    /// <summary>Read-only diagnostics for the PC running Quest Link. Never included in an APK.</summary>
    public sealed class HandTrackingDiagnostics : EditorWindow
    {
        private string report = "";
        private Vector2 scroll;

        [MenuItem("SortQuest/Diagnose Hand Tracking")]
        public static void Open()
        {
            var window = GetWindow<HandTrackingDiagnostics>("Hand Tracking");
            window.minSize = new Vector2(650, 430);
            window.Refresh();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("While the game is running, put both controllers down and hold your hands in front of the headset. Click Refresh, then Copy Report. This tool does not change tracking settings or the scene.", MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh")) Refresh();
            if (GUILayout.Button("Copy Report")) { Refresh(); EditorGUIUtility.systemCopyBuffer = report; }
            EditorGUILayout.EndHorizontal();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.TextArea(report, new GUIStyle(EditorStyles.textArea) { wordWrap = true }, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void Refresh() { report = Capture(); Repaint(); }

        public static string Capture()
        {
            var text = new StringBuilder("SORTQUEST HAND TRACKING REPORT\n");
            text.AppendLine($"Unity={Application.unityVersion}; platform={Application.platform}; playing={EditorApplication.isPlaying}");
            text.AppendLine($"Scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            foreach (var target in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
            {
                var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(target);
                var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(target);
                text.AppendLine($"{target}: initializeXR={general != null && general.InitManagerOnStart}; MetaXR={settings != null && settings.GetFeature<Meta.XR.MetaXRFeature>()?.enabled == true}; TouchProfile={settings != null && settings.GetFeature<OculusTouchControllerProfile>()?.enabled == true}");
            }

            var rigs = Find<OVRCameraRig>();
            var hands = Find<OVRHand>();
            var references = Find<OVRCameraRigRef>();
            var visuals = Find<HandVisual>();
            text.AppendLine($"Scene objects: cameraRigs={rigs.Length}; OVRHands={hands.Length}; interactionRigReferences={references.Length}; handVisuals={visuals.Length}");
            text.AppendLine($"Hand skeleton format={OVRRuntimeSettings.GetRuntimeSettings().HandSkeletonVersion}");
            foreach (var rig in rigs)
                text.AppendLine($"Camera rig {Path(rig.transform)}: active={rig.isActiveAndEnabled}; leftAnchor={rig.leftHandAnchor != null}; rightAnchor={rig.rightHandAnchor != null}");
            foreach (var reference in references)
                text.AppendLine($"Interaction rig {Path(reference.transform)}: active={reference.isActiveAndEnabled}; cameraRigAssigned={reference.CameraRig != null}");
            foreach (var manager in Find<OVRManager>())
                text.AppendLine($"OVRManager: active={manager.isActiveAndEnabled}; simultaneousHandsControllers={manager.SimultaneousHandsAndControllersEnabled}");

            bool running = EditorApplication.isPlaying && XRGeneralSettings.Instance?.Manager?.activeLoader != null;
            if (running)
            {
                Safe(text, () =>
                {
                    text.AppendLine($"Active loader={XRGeneralSettings.Instance.Manager.activeLoader.name}; runtime={OpenXRRuntime.name} {OpenXRRuntime.version}");
                    text.AppendLine($"Head tracked={Tracked(XRNode.Head)}; left device tracked={Tracked(XRNode.LeftHand)}; right device tracked={Tracked(XRNode.RightHand)}");
                    text.AppendLine($"XR_EXT_hand_tracking={OpenXRRuntime.IsExtensionEnabled("XR_EXT_hand_tracking")}; XR_FB_hand_tracking_aim={OpenXRRuntime.IsExtensionEnabled("XR_FB_hand_tracking_aim")}");
                    text.AppendLine($"OVR connected={OVRInput.GetConnectedControllers()}; active={OVRInput.GetActiveController()}");
                });
            }
            else text.AppendLine("Live input unavailable: enter Play mode with Quest Link connected, then Refresh.");

            int trackedHands = 0, visibleHands = 0;
            foreach (var hand in hands)
            {
                var skeleton = hand.GetComponent<OVRSkeleton>();
                var type = new SerializedObject(hand).FindProperty("HandType");
                string side = type == null ? hand.name : type.enumDisplayNames[type.enumValueIndex];
                text.AppendLine($"OVR {side} ({Path(hand.transform)}): active={hand.isActiveAndEnabled}; valid={hand.IsDataValid}; tracked={hand.IsTracked}; highConfidence={hand.IsDataHighConfidence}; skeletonInitialized={skeleton != null && skeleton.IsInitialized}");
                if (hand.isActiveAndEnabled && hand.IsTracked && hand.IsDataValid) trackedHands++;
            }
            foreach (var visual in visuals)
            {
                // The SDK intentionally disables its legacy OVR mesh renderer when ISDK visuals are used.
                // Inspect the actual OpenXR HandVisual renderer instead of treating those legacy flags as a fault.
                var serialized = new SerializedObject(visual);
                var renderer = serialized.FindProperty("_openXRSkinnedMeshRenderer")?.objectReferenceValue as SkinnedMeshRenderer;
                text.AppendLine($"Visual {Path(visual.transform)}: active={visual.isActiveAndEnabled}; rendererAssigned={renderer != null}; meshAssigned={renderer != null && renderer.sharedMesh != null}");
                if (!running || !visual.isActiveAndEnabled) continue;
                Safe(text, () =>
                {
                    var hand = visual.Hand;
                    text.AppendLine($"  ISDK hand assigned={hand != null}; connected={hand != null && hand.IsConnected}; highConfidence={hand != null && hand.IsHighConfidence}; visible={visual.IsVisible}");
                    if (visual.IsVisible) visibleHands++;
                    if (renderer != null)
                    {
                        var cameras = Find<Camera>().Where(c => c.isActiveAndEnabled && (c.cullingMask & (1 << renderer.gameObject.layer)) != 0).Select(c => c.name);
                        text.AppendLine($"  Renderer active={renderer.gameObject.activeInHierarchy}; enabled={renderer.enabled}; layer={renderer.gameObject.layer}; camerasIncludingLayer={string.Join(",", cameras)}");
                    }
                });
            }

            text.AppendLine("\nINTERPRETATION");
            if (hands.Length < 2 || references.Any(r => r.CameraRig == null))
                text.AppendLine("Missing hand objects or an unassigned interaction camera rig: inspect the scene wiring.");
            else if (!running)
                text.AppendLine("Scene wiring can be inspected above; no conclusion about live hand tracking until Play mode.");
            else if (trackedHands == 0)
                text.AppendLine("Neither active OVRHand has valid tracked data. Check headset hand tracking, controller-to-hand switching, Link developer runtime features, and Console errors. Changing renderer visibility cannot restore missing input.");
            else if (visibleHands == 0)
                text.AppendLine("OVR receives hand data but no active ISDK hand visual reports visible. Inspect ISDK hand connection/confidence, visual references and Console errors above.");
            else
                text.AppendLine("Tracked hand data and visible hand renderers are reported. If hands remain unseen, inspect camera layers, hand transforms, materials and occlusion.");
            text.AppendLine("This is a diagnostic snapshot, not a successful on-device tracking test.");
            return text.ToString();
        }

        private static T[] Find<T>() where T : Component => Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(c => c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded).ToArray();

        private static bool Tracked(XRNode node)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            return device.isValid && device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
        }

        private static void Safe(StringBuilder text, Action read)
        {
            try { read(); }
            catch (Exception error) { text.AppendLine($"Read failed: {error.GetType().Name}: {error.Message}"); }
        }

        private static string Path(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null) { transform = transform.parent; path = transform.name + "/" + path; }
            return path;
        }

        [MenuItem("SortQuest/Validate Hand Scene Wiring")]
        public static void ValidateScene()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode to validate saved scene wiring.");
            var rigs = Find<OVRCameraRig>();
            var hands = Find<OVRHand>();
            var references = Find<OVRCameraRigRef>();
            var visuals = Find<HandVisual>();
            if (rigs.Length != 1 || !rigs[0].isActiveAndEnabled || hands.Count(h => h.isActiveAndEnabled) < 2)
                throw new InvalidOperationException("Expected one active camera rig and at least two active OVRHands.");
            // The comprehensive rig includes its own OVR visual sources as well as the anchor hands.
            // Multiple OVRHands are valid; verify the anchor sources that OVRCameraRigRef actually resolves.
            foreach (var anchor in new[] { rigs[0].leftHandAnchor, rigs[0].rightHandAnchor })
                if (anchor == null || anchor.GetComponentInChildren<OVRHand>(true)?.isActiveAndEnabled != true)
                    throw new InvalidOperationException("A camera hand anchor is missing its active OVRHand source.");
            if (references.Length == 0 || references.Any(r => !r.isActiveAndEnabled || r.CameraRig != rigs[0]))
                throw new InvalidOperationException("Interaction rig camera reference is missing or disabled.");
            if (visuals.Count(v => v.isActiveAndEnabled && new SerializedObject(v).FindProperty("_openXRSkinnedMeshRenderer")?.objectReferenceValue != null) < 2)
                throw new InvalidOperationException("OpenXR hand visual renderer reference is missing.");
            Debug.Log("HAND_SCENE_WIRING_PASS\n" + Capture());
        }

        public static void ValidateBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            ValidateScene();
        }
    }
}
