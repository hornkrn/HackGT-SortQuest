using UnityEditor;
using UnityEngine;

namespace SortQuest.Editor
{
    /// <summary>Starts the real tutorial without needing to grab the VR menu block.</summary>
    [InitializeOnLoad]
    public static class TutorialPreview
    {
        private const string Pending = "SortQuest.StartTutorialPreview";

        static TutorialPreview()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
                {
                    SessionState.SetBool(Pending, false);
                    EditorApplication.delayCall += StartTutorial;
                }
            };
        }

        [MenuItem("SortQuest/Tutorial/Start narrated tutorial")]
        private static void StartPreview()
        {
            if (EditorApplication.isPlaying) StartTutorial();
            else
            {
                SessionState.SetBool(Pending, true);
                EditorApplication.isPlaying = true;
            }
        }

        private static void StartTutorial()
        {
            if (!EditorApplication.isPlaying) return;
            var game = Object.FindAnyObjectByType<GameManager>();
            if (game == null) { Debug.LogError("Open SampleScene before starting the tutorial."); return; }
            game.SetState(GameState.Tutorial);
        }
    }
}
