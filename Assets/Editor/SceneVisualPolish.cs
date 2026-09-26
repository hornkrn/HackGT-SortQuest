using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SortQuest.Editor
{
    public static class SceneVisualPolish
    {
        [MenuItem("SortQuest/Polish Scene Visuals")]
        public static void Apply() => QuestSceneArt.Apply();

        public static void ApplyBatch()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            Apply();
        }

        public static void RenderPreview()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            // Instantiate only the existing visual robot model for the preview, without entering Play
            // mode, starting the game, connecting to the API, or saving these temporary objects.
            foreach(var visual in UnityEngine.Object.FindObjectsByType<GripperVisual>(FindObjectsSortMode.None))
                typeof(GripperVisual).GetMethod("Awake",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(visual,null);
            foreach(var arm in UnityEngine.Object.FindObjectsByType<RobotArmDisplay>(FindObjectsSortMode.None))
            {
                typeof(RobotArmDisplay).GetMethod("Awake",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(arm,null);
                typeof(RobotArmDisplay).GetMethod("LateUpdate",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(arm,null);
            }
            Capture(new Vector3(0,1.85f,-3.45f),new Vector3(0,1.4f,1.8f),"sortquest-scene-preview.png");
            Capture(new Vector3(-2.5f,2.05f,-2.5f),new Vector3(.15f,1.25f,1.5f),"sortquest-scene-angle.png");
            // Discard all preview-only objects and renderer changes from the editor scene.
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        }

        private static void Capture(Vector3 position,Vector3 target,string name)
        {
            var go=new GameObject("Temporary Preview Camera");var camera=go.AddComponent<Camera>();
            go.transform.position=position;go.transform.LookAt(target);camera.fieldOfView=66;camera.nearClipPlane=.05f;camera.farClipPlane=30;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.38f,.48f,.52f);
            camera.allowHDR=false;camera.allowMSAA=true;
            var rt=new RenderTexture(1800,1125,24){antiAliasing=4};camera.targetTexture=rt;
            bool prior=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
            camera.Render();RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
            RenderTexture.active=rt;
            var texture=new Texture2D(1800,1125,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1800,1125),0,0);texture.Apply();
            string folder=Environment.GetEnvironmentVariable("SORTQUEST_PREVIEW_DIR")??Path.GetTempPath();Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder,name),texture.EncodeToPNG());
            RenderTexture.active=null;camera.targetTexture=null;ShaderUtil.allowAsyncCompilation=prior;
            UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(go);
            Debug.Log("VISUAL_PREVIEW_SAVED "+name);
        }
        public static void FinalizeAndPreview(){ApplyBatch();RenderPreview();}
    }
}
