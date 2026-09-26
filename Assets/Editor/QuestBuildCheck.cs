using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SortQuest.Editor
{
    public static class QuestBuildCheck
    {
        [UnityEditor.MenuItem("SortQuest/Build Android Visual Check")]
        public static void Build()
        {
            string output=Environment.GetEnvironmentVariable("SORTQUEST_APK_PATH")??Path.Combine(Path.GetTempPath(),"SortQuest-VisualCheck.apk");
            BuildReport report=BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes=new[]{"Assets/Scenes/SampleScene.unity"},
                locationPathName=output,
                target=BuildTarget.Android,
                options=BuildOptions.Development
            });
            Debug.Log($"QUEST_APK_RESULT: {report.summary.result}; errors={report.summary.totalErrors}; bytes={report.summary.totalSize}");
            if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Android build did not succeed; see Unity build log.");
        }
    }
}
