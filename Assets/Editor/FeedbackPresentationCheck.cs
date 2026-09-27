using System;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SortQuest.Editor
{
    public static class FeedbackPresentationCheck
    {
        [MenuItem("SortQuest/Preview and Check Signs and Audio")]
        public static void Run()
        {
            if (EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Exit Play mode and save the scene before running the preview check.");
            string path = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Open a saved scene first.");
            var go = new GameObject("Temporary feedback check");
            var feedback = go.AddComponent<InteractionFeedback>();
            try
            {
                Invoke(feedback, "Start");
                Require(go.GetComponentsInChildren<AudioSource>().Length == 8, "Expected eight pooled voices");
                foreach (var bin in Object.FindObjectsByType<SortingBin>(FindObjectsSortMode.None))
                {
                    var label = bin.GetComponentInChildren<TMP_Text>();
                    Require(label != null, bin.name + " has no label");
                    Vector3 local = bin.transform.InverseTransformPoint(label.transform.position);
                    Require(label.GetComponent<Billboard>() == null && local.z < -.2f && local.y < .6f,
                        bin.name + " label is not on the bin's front edge");
                }
                foreach (var sign in Object.FindObjectsByType<ReadableSign>(FindObjectsSortMode.None))
                    Require(sign.GetComponentsInChildren<Collider>().Length == 0, "Sign changed collision geometry");
                var guide = go.GetComponentInChildren<TrashGuide>();
                Require(guide != null, "Trash guide missing");
                Invoke(guide, "Start");
                Require(guide.ModelCount >= 6, "Trash guide shows too few models");
                Require(guide.GetComponentsInChildren<Collider>().Length == 0, "Trash guide added collision geometry");
                Require(guide.GetComponentsInChildren<TMP_Text>().Length == 12, "Trash guide text missing");
                var clip = InteractionFeedback.Tone("Validation", .3f, 660, 990);
                var data = new float[clip.samples];
                clip.GetData(data, 0);
                float peak = 0;
                foreach (float sample in data)
                {
                    Require(!float.IsNaN(sample) && Mathf.Abs(sample) < 1, "Invalid or clipped audio");
                    peak = Mathf.Max(peak, Mathf.Abs(sample));
                }
                Require(peak > .1f && Mathf.Abs(data[0]) < .001f && Mathf.Abs(data[data.Length - 1]) < .001f, "Silent or discontinuous cue");
                Object.DestroyImmediate(clip);
                var sampleObject = new GameObject("Temporary item", typeof(Rigidbody), typeof(TrashItem));
                var item = sampleObject.GetComponent<TrashItem>();
                typeof(InteractionFeedback).GetMethod("Track", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(feedback, new object[] { item });
                foreach (string eventName in new[] { "Grabbed", "Released", "PickedByRobot", "Missed" })
                {
                    var callback = (Action<TrashItem>)typeof(TrashItem).GetField(eventName, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(item);
                    Require(callback != null, eventName + " audio subscription missing");
                    callback(item);
                }
                var sorted = (Action<TrashItem, SortingBin, bool>)typeof(TrashItem).GetField("Sorted", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(item);
                var targetBin = Object.FindAnyObjectByType<SortingBin>();
                sorted(item, targetBin, true);
                sorted(item, targetBin, false);
                var voices = go.GetComponentsInChildren<AudioSource>();
                Require(Array.Exists(voices, source => source.clip != null && source.clip.name == "Correct sort"), "Correct cue missing");
                Require(Array.Exists(voices, source => source.clip != null && source.clip.name == "Wrong sort"), "Wrong cue missing");
                Object.DestroyImmediate(sampleObject);
                foreach (var visual in Object.FindObjectsByType<GripperVisual>(FindObjectsSortMode.None)) Invoke(visual, "Awake");
                foreach (var arm in Object.FindObjectsByType<RobotArmDisplay>(FindObjectsSortMode.None)) { Invoke(arm, "Awake"); Invoke(arm, "LateUpdate"); }
                var capture = typeof(SceneVisualPolish).GetMethod("Capture", BindingFlags.NonPublic | BindingFlags.Static);
                capture.Invoke(null, new object[] { new Vector3(0, 1.85f, -3.45f), new Vector3(0, 1.35f, 1.6f), "sortquest-signs-preview.png" });
                Debug.Log("FEEDBACK_PRESENTATION_PASS: bin labels on bin fronts, trash guide models and text, collider isolation, eight voices, finite unclipped mono cue with silent endpoints.");
            }
            finally
            {
                Object.DestroyImmediate(go);
                EditorSceneManager.OpenScene(path);
            }
        }
        private static void Invoke(object obj, string method) => obj.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(obj, null);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
