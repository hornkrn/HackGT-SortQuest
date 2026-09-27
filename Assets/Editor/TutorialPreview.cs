using UnityEditor;
using UnityEngine;

namespace SortQuest.Editor
{
    /// <summary>Starts the real tutorial without needing to grab the VR menu block.</summary>
    [InitializeOnLoad]
    public static class TutorialPreview
    {
        private const string ShowChoice = "SortQuest.ShowTutorialChoice";
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
            SessionState.SetBool(ShowChoice, false);
            BeginPreview();
        }

        [MenuItem("SortQuest/Tutorial/Show Yes-No choice")]
        private static void PreviewChoice()
        {
            SessionState.SetBool(ShowChoice, true);
            BeginPreview();
        }

        private static void BeginPreview()
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
            var menu = Object.FindAnyObjectByType<MainMenu>();
            game.SetState(SessionState.GetBool(ShowChoice, false) && menu != null && menu.Ready
                ? GameState.TutorialChoice : GameState.Tutorial);
        }
    }
}
