using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SortQuest.Editor
{
    /// <summary>Editor-only art generation. Prefab variants inherit the original gameplay setup.</summary>
    public static class TrashArtBuilder
    {
        private const string Folder="Assets/Art/UsedTrash",VariantFolder="Assets/Prefabs/TrashVariants";
        private const string ScenePath="Assets/Scenes/SampleScene.unity";
        private const int Variants=8,TriangleBudget=1600;

        [MenuItem("SortQuest/Trash Art/Rebuild Models and Wire Scene")]
        public static void Build()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play mode first.");
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.path!=ScenePath)throw new InvalidOperationException("Open SampleScene before rebuilding trash art.");
            EnsureFolder("Assets/Art");EnsureFolder(Folder);EnsureFolder(VariantFolder);
            var texture=TrashArtAtlas.Build(Folder);
            var material=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/UsedTrash.mat");
            if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Simple Lit")){name="Used Trash - Shared Atlas"};AssetDatabase.CreateAsset(material,Folder+"/UsedTrash.mat");}
            material.SetTexture("_BaseMap",texture);material.SetColor("_BaseColor",Color.white);
            material.SetColor("_SpecColor",new Color(.14f,.14f,.14f,1));
            material.SetFloat("_Surface",0);material.SetFloat("_AlphaClip",0);material.SetFloat("_SmoothnessSource",1);
            material.SetFloat("_Smoothness",1);material.SetFloat("_SpecularHighlights",0);
            material.enableInstancing=true;
            UnityEditor.Rendering.Universal.ShaderGUI.SimpleLitGUI.SetMaterialKeywords(material);
            var prefabs=new List<TrashItem>();var rows=new List<string>();int maxTriangles=0,totalTriangles=0;
            foreach(ItemType type in Enum.GetValues(typeof(ItemType)))
            {
                string basePath="Assets/Prefabs/"+type+".prefab";
                var original=AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
                if(original==null)throw new InvalidOperationException("Missing original prefab: "+basePath);
                string originalState=Fingerprint(original);
                Vector3 scale=original.transform.localScale;
                for(int v=0;v<Variants;v++)
                {
                    string name=type+"_"+v.ToString("00")+"_"+TrashArtAtlas.Names[(int)type][v].Replace(' ','_');
                    var mesh=TrashArtMeshes.Build(type,v,scale,name);
                    int triangles=mesh.triangles.Length/3;
                    if(triangles>TriangleBudget)throw new InvalidOperationException("Triangle budget exceeded: "+name);
                    ValidateMesh(mesh,scale,type);
                    string meshPath=Folder+"/"+name+".asset";
                    var existing=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if(existing==null)AssetDatabase.CreateAsset(mesh,meshPath);
                    else{EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;}
                    string prefabPath=v==0?basePath:VariantFolder+"/"+name+".prefab";
                    GameObject root=v==0?PrefabUtility.LoadPrefabContents(basePath):(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(basePath));
                    try
                    {
                        root.GetComponent<MeshFilter>().sharedMesh=mesh;
                        root.GetComponent<MeshRenderer>().sharedMaterial=material;
                        if(v>0)root.name=name;
                        AssertFingerprint(originalState,Fingerprint(root),name);
                        var saved=PrefabUtility.SaveAsPrefabAsset(root,prefabPath);
                        if(saved==null)throw new InvalidOperationException("Could not save "+prefabPath);
                        prefabs.Add(saved.GetComponent<TrashItem>());
                        AssertFingerprint(originalState,Fingerprint(saved),"Saved "+name);
                    }
                    finally{if(v==0)PrefabUtility.UnloadPrefabContents(root);else Object.DestroyImmediate(root);}
                    rows.Add($"| {TrashArtAtlas.Names[(int)type][v]} | {type} | {triangles} |");
                    maxTriangles=Math.Max(maxTriangles,triangles);totalTriangles+=triangles;
                }
            }
            var spawner=Object.FindAnyObjectByType<TrashSpawner>();
            if(spawner==null)throw new InvalidOperationException("SampleScene has no TrashSpawner.");
            var serialized=new SerializedObject(spawner);var array=serialized.FindProperty("itemPrefabs");array.arraySize=prefabs.Count;
            for(int i=0;i<prefabs.Count;i++)array.GetArrayElementAtIndex(i).objectReferenceValue=prefabs[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            CheckSelection(spawner,prefabs);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            Directory.CreateDirectory("docs");
            File.WriteAllText("docs/TRASH_ART_AUDIT.md",
                "# Used trash art audit\n\nGenerated and validated in Unity.\n\n"+
                $"- **{prefabs.Count} models**: eight per existing ItemType.\n- One opaque URP Simple Lit material, one 2048 × 2048 atlas with mipmaps and Android ASTC 6×6 compression. Alpha stores surface smoothness; it is not transparency.\n"+
                $"- **{maxTriangles:N0} triangles maximum per item**, **{maxTriangles*8:N0}** for eight copies of the heaviest item (geometry only, before shadow passes). All 48 meshes total {totalTriangles:N0} triangles.\n"+
                "- One mesh renderer per item. No new runtime scripts, lights, colliders, rigidbodies, or packages.\n- Original six prefab GUIDs retained; 42 Unity prefab variants inherit their gameplay components.\n- Serialized transforms, interaction components, rigidbodies, item fields, collision meshes, layers, tags, and internal references compared with each original before and after saving.\n- Spawner selection exercised 4,800 times across the pool and 200 times per item filter; every variant was reachable and filters returned only the requested ItemType.\n- Eight variants per type preserve the existing per-type random weighting. The original prefab remains first for GetPrefab and training.\n\n"+
                "## Limits\n\nThe existing simplified collision envelopes are intentionally retained, including the bottle's cylindrical collider. Dents, bottle shoulders, and paper folds are visual details; this preserves recorded local grasp frames and robot collision behavior. Standalone Quest 2 frame time and hand interaction still need verification on the headset.\n\n"+
                "| Model | Existing type | Triangles |\n|---|---|---:|\n"+string.Join("\n",rows)+"\n");
            Debug.Log($"TRASH_ART_PASS: {prefabs.Count} models; {maxTriangles} max triangles; unchanged gameplay fingerprints; selection checks passed.");
        }

        public static void BuildBatch(){EditorSceneManager.OpenScene(ScenePath);Build();}
        public static void BuildAndPreview(){BuildBatch();RenderPreview();}

        private static void CheckSelection(TrashSpawner spawner,List<TrashItem> prefabs)
        {
            var method=typeof(TrashSpawner).GetMethod("PickPrefab",BindingFlags.NonPublic|BindingFlags.Instance);
            var state=UnityEngine.Random.state;var originalFilter=spawner.OnlyType;
            try
            {
                UnityEngine.Random.InitState(18071);spawner.OnlyType=null;
                var seen=new HashSet<TrashItem>();
                for(int i=0;i<4800;i++)seen.Add((TrashItem)method.Invoke(spawner,null));
                if(seen.Count!=48||seen.Contains(null))throw new InvalidOperationException("Incomplete random pool.");
                foreach(ItemType type in Enum.GetValues(typeof(ItemType)))
                {
                    if(prefabs.Count(p=>p.ItemType==type)!=Variants)throw new InvalidOperationException("Unequal type weighting.");
                    spawner.OnlyType=type;seen.Clear();
                    for(int i=0;i<200;i++){var p=(TrashItem)method.Invoke(spawner,null);if(p.ItemType!=type)throw new InvalidOperationException("Type filter mismatch.");seen.Add(p);}
                    if(seen.Count!=Variants||spawner.GetPrefab(type)!=prefabs.First(p=>p.ItemType==type))throw new InvalidOperationException("Variant selection or training prefab mismatch.");
                }
            }
            finally{spawner.OnlyType=originalFilter;UnityEngine.Random.state=state;}
        }
        private static void ValidateMesh(Mesh mesh,Vector3 scale,ItemType type)
        {
            if(mesh.subMeshCount!=1||mesh.normals.Length!=mesh.vertexCount||mesh.uv.Length!=mesh.vertexCount)throw new InvalidOperationException("Incomplete mesh channels.");
            Vector3 extents=type==ItemType.AluminumCan?new Vector3(.034f,.062f,.034f):
                type==ItemType.PlasticBottle?new Vector3(.036f,.111f,.036f):type==ItemType.CardboardBox?new Vector3(.126f,.076f,.101f):
                type==ItemType.CrumpledPaper?Vector3.one*.041f:type==ItemType.BatteryAA?new Vector3(.011f,.038f,.011f):new Vector3(.036f,.014f,.071f);
            foreach(var vertex in mesh.vertices)
            {
                var p=Vector3.Scale(vertex,scale);
                if(!float.IsFinite(p.x)||!float.IsFinite(p.y)||!float.IsFinite(p.z)||Mathf.Abs(p.x)>extents.x||Mathf.Abs(p.y)>extents.y||Mathf.Abs(p.z)>extents.z)
                    throw new InvalidOperationException("Mesh outside the original item envelope: "+mesh.name);
            }
        }

        // Object references are compared by local hierarchy path or asset GUID, never by transient instance ID.
        private static string Fingerprint(GameObject root)
        {
            var result=new StringBuilder();
            foreach(var tr in root.GetComponentsInChildren<Transform>(true))
            {
                result.Append(PathOf(tr,root.transform)).Append('|').Append(tr.gameObject.layer).Append('|').Append(tr.gameObject.tag).Append('|').Append(tr.gameObject.activeSelf);
                foreach(var component in tr.GetComponents<Component>())
                {
                    if(component==null)throw new InvalidOperationException("Missing script in "+root.name);
                    if(component is Renderer||component is MeshFilter)continue;
                    result.Append('\n').Append(component.GetType().FullName);
                    var iterator=new SerializedObject(component).GetIterator();
                    bool enterChildren=true;
                    while(iterator.Next(enterChildren))
                    {
                        enterChildren=iterator.propertyType!=SerializedPropertyType.ObjectReference;
                        string path=iterator.propertyPath;
                        if(path.StartsWith("m_CorrespondingSourceObject")||path.StartsWith("m_PrefabInstance")||path.StartsWith("m_PrefabAsset")||path=="m_RootOrder")continue;
                        result.Append('|').Append(path).Append('=');
                        switch(iterator.propertyType)
                        {
                            case SerializedPropertyType.ObjectReference:
                                var reference=iterator.objectReferenceValue;
                                if(reference==null){result.Append("null");break;}
                                Transform target=reference is Component c?c.transform:(reference as GameObject)?.transform;
                                if(target!=null&&(target==root.transform||target.IsChildOf(root.transform)))result.Append(reference.GetType().Name).Append(':').Append(PathOf(target,root.transform));
                                else result.Append(AssetDatabase.GetAssetPath(reference)).Append(':').Append(reference.name);
                                break;
                            case SerializedPropertyType.Integer:case SerializedPropertyType.Enum:case SerializedPropertyType.LayerMask:case SerializedPropertyType.ArraySize:result.Append(iterator.intValue);break;
                            case SerializedPropertyType.Boolean:result.Append(iterator.boolValue);break;
                            case SerializedPropertyType.Float:result.Append(iterator.doubleValue.ToString("R",System.Globalization.CultureInfo.InvariantCulture));break;
                            case SerializedPropertyType.String:result.Append(iterator.stringValue);break;
                        }
                    }
                }
            }
            return result.ToString();
        }
        private static string PathOf(Transform t,Transform root)
        {if(t==root)return "/";var parts=new List<string>();while(t!=null&&t!=root){parts.Add(t.name+"#"+t.GetSiblingIndex());t=t.parent;}parts.Reverse();return string.Join("/",parts);}
        private static void AssertFingerprint(string expected,string actual,string label)
        {
            if(expected==actual)return;
            int i=0;while(i<Math.Min(expected.Length,actual.Length)&&expected[i]==actual[i])i++;
            int start=Math.Max(0,i-70);
            throw new InvalidOperationException("Nonvisual mismatch "+label+" at "+i+": expected "+expected.Substring(start,Math.Min(180,expected.Length-start))+"; actual "+actual.Substring(start,Math.Min(180,actual.Length-start)));
        }
        private static void EnsureFolder(string path){if(!AssetDatabase.IsValidFolder(path))AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));}

        [MenuItem("SortQuest/Trash Art/Render Model Catalog")]
        public static void RenderPreview()
        {
            // Preview uses mesh-only copies in an unsaved scene. Gameplay and the API never run.
            var previous=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(previous.isDirty)throw new InvalidOperationException("Save the current scene before rendering the catalog.");
            string previousPath=previous.path;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var material=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/UsedTrash.mat");
            var items=new List<GameObject>();
            for(int type=0;type<6;type++)for(int v=0;v<Variants;v++)
            {
                string name=((ItemType)type)+"_"+v.ToString("00")+"_"+TrashArtAtlas.Names[type][v].Replace(' ','_');
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/"+name+".asset");
                var original=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/"+(ItemType)type+".prefab");
                var holder=new GameObject(name);items.Add(holder);
                holder.transform.position=new Vector3((v-3.5f)*.315f,(2.5f-type)*.325f,0);
                holder.transform.rotation=type==5?Quaternion.Euler(-64,12,-12):type==2?Quaternion.Euler(-21,28,0):Quaternion.Euler(-18,0,(v%3-1)*7)*Quaternion.Euler(0,-125,0);
                var go=new GameObject("Mesh",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(holder.transform,false);
                Vector3 physicalSize=Vector3.Scale(mesh.bounds.size,original.transform.localScale);
                float normalize=.215f/Mathf.Max(physicalSize.x,Mathf.Max(physicalSize.y,physicalSize.z));
                go.transform.localScale=original.transform.localScale*normalize;go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=material;
                Label(TrashArtAtlas.Names[type][v],holder.transform.position+new Vector3(0,-.14f,-.2f),.16f,.31f);
            }
            Label("SORTQUEST  /  RECOVERED MATERIALS",new Vector3(0,1.08f,-.2f),.46f,2.8f);
            Label("48 WORN VARIANTS     /     SIX SORTING TYPES     /     ONE SHARED ATLAS",new Vector3(0,1.015f,-.2f),.18f,2.8f);
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.64f,.68f,.72f);
            var sun=new GameObject("Catalog Light",typeof(Light));sun.GetComponent<Light>().type=LightType.Directional;sun.GetComponent<Light>().intensity=1.05f;sun.transform.rotation=Quaternion.Euler(35,-25,0);
            Capture(new Vector3(0,.045f,-4),Vector3.forward,true,1.2f,2560,1900,"trash-model-catalog.png");
            foreach(var item in items)Object.DestroyImmediate(item);
            foreach(var text in Object.FindObjectsByType<TextMeshPro>(FindObjectsSortMode.None))Object.DestroyImmediate(text.gameObject);
            // A closer inspection strip: representative models rendered at a larger screen size.
            for(int type=0;type<6;type++)
            {
                int variant=new[]{0,1,4,0,1,3}[type];
                string name=((ItemType)type)+"_"+variant.ToString("00")+"_"+TrashArtAtlas.Names[type][variant].Replace(' ','_');
                var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/"+(ItemType)type+".prefab");
                var holder=new GameObject("Closeup");holder.transform.position=new Vector3((type-2.5f)*.43f,0,0);
                holder.transform.rotation=type==5?Quaternion.Euler(-66,12,-15):type==2?Quaternion.Euler(-21,28,0):Quaternion.Euler(-18,0,-8)*Quaternion.Euler(0,-125,0);
                var go=new GameObject("Mesh",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(holder.transform,false);
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/"+name+".asset");var size=Vector3.Scale(mesh.bounds.size,source.transform.localScale);
                go.transform.localScale=source.transform.localScale*(.32f/Mathf.Max(size.x,Mathf.Max(size.y,size.z)));
                go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=material;
                Label(TrashArtAtlas.Names[type][variant],holder.transform.position+new Vector3(0,-.225f,-.25f),.22f,.42f);
            }
            Label("USED. SORTED. RECOVERED.",new Vector3(0,.34f,-.25f),.40f,2.8f);
            Capture(new Vector3(0,.02f,-4),Vector3.forward,true,.49f,2560,900,"trash-model-closeups.png");
            if(!string.IsNullOrEmpty(previousPath))EditorSceneManager.OpenScene(previousPath);
        }
        private static void Label(string value,Vector3 p,float size,float width)
        {
            var go=new GameObject(value);go.transform.position=p;var text=go.AddComponent<TextMeshPro>();
            text.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            text.text=value;text.fontSize=size;text.color=new Color(.79f,.83f,.81f);text.alignment=TextAlignmentOptions.Center;
            text.textWrappingMode=TextWrappingModes.NoWrap;text.rectTransform.sizeDelta=new Vector2(width,.05f);
        }
        private static void Capture(Vector3 position,Vector3 direction,bool orthographic,float size,int width,int height,string file)
        {
            var go=new GameObject("Catalog Camera",typeof(Camera));var camera=go.GetComponent<Camera>();go.transform.position=position;go.transform.forward=direction;
            camera.orthographic=orthographic;camera.orthographicSize=size;camera.nearClipPlane=.1f;camera.farClipPlane=12;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.065f,.088f,.1f);camera.allowHDR=false;
            var rt=new RenderTexture(width,height,24){antiAliasing=4};camera.targetTexture=rt;
            bool prior=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
            camera.Render();RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
            RenderTexture.active=rt;var texture=new Texture2D(width,height,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();
            Directory.CreateDirectory("docs/visuals");File.WriteAllBytes("docs/visuals/"+file,texture.EncodeToPNG());
            RenderTexture.active=null;camera.targetTexture=null;ShaderUtil.allowAsyncCompilation=prior;
            Object.DestroyImmediate(texture);Object.DestroyImmediate(rt);Object.DestroyImmediate(go);Debug.Log("TRASH_PREVIEW_SAVED "+file);
        }
    }
}
