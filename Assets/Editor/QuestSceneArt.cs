using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SortQuest.Editor
{
    /// <summary>Build-time mesh art; no gameplay components or runtime update loops.</summary>
    public static class QuestSceneArt
    {
        private const string RootName = "Visual Set Dressing";
        private const string Folder = "Assets/Art/QuestLab";
        private static readonly Color Navy = Hex("25343A"), Ink = Hex("17262C"), Slate = Hex("647174");
        private static readonly Color Ivory = Hex("CED0C5"), White = Hex("F2EEDA"), Alloy = Hex("8A9794");
        private static readonly Color Teal = Hex("61B8A0"), DimTeal = Hex("307A6C"), Amber = Hex("F3C44E");
        private static Transform root;
        private static readonly Dictionary<string, Geometry> zones = new Dictionary<string, Geometry>();
        private static Geometry g;
        private static Material palette;

        public static void AddPeripheralDetails()
        {
            const string detailName = "Facility peripheral details";
            var scene = SceneManager.GetActiveScene();
            var old = scene.GetRootGameObjects().FirstOrDefault(o => o.name == detailName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            g = new Geometry();
            // Exposed services and equipment trim, outside the sorting/reaching area.
            foreach (int side in new[] { -1, 1 })
            {
                float x = side * 4.15f;
                Box(new Vector3(x, 2.85f, 1.3f), new Vector3(.25f, .25f, 4.4f), Alloy, .025f);
                for (int i = 0; i < 9; i++)
                {
                    Box(new Vector3(x, 2.85f, -.7f + i * .5f), new Vector3(.29f, .29f, .035f), Slate, .005f);
                    Box(new Vector3(side * 3.96f, .018f, -.6f + i * .32f), new Vector3(.35f, .025f, .025f), Slate, 0);
                }
                // Cable riser, electrical enclosure, warning stripe and latch.
                Box(new Vector3(side * 3.92f, 1.55f, 3.65f), new Vector3(.07f, 2.4f, .08f), Slate, .008f);
                Box(new Vector3(side * 3.85f, 1.9f, 3.5f), new Vector3(.47f, .62f, .18f), Ivory, .045f);
                Box(new Vector3(side * 3.85f, 2.11f, 3.395f), new Vector3(.36f, .045f, .018f), Amber, .003f);
                Box(new Vector3(side * 3.7f, 1.85f, 3.392f), new Vector3(.025f, .11f, .02f), Navy, .003f);
                for (int j = 0; j < 5; j++) Box(new Vector3(side * 3.85f, 1.75f + .03f * j, 3.395f), new Vector3(.2f, .01f, .01f), Slate, 0);
                // Stanchions behind the training displays, not across the player workspace.
                Box(new Vector3(side * 2.05f, 1.0f, 2.8f), new Vector3(.075f, 2f, .075f), Slate, .012f);
                Box(new Vector3(side * 2.05f, .04f, 2.8f), new Vector3(.5f, .07f, .32f), Navy, .018f);
            }
            // Wall-mounted fire equipment with handle, hose, retaining band and inspection plate.
            Cylinder(new Vector3(3.85f, .92f, 3.48f), .105f, .5f, Hex("B74D40"), 12);
            Box(new Vector3(3.85f, 1.21f, 3.48f), new Vector3(.19f, .04f, .05f), Navy, .01f);
            Box(new Vector3(3.97f, .98f, 3.47f), new Vector3(.025f, .4f, .035f), Navy, .008f);
            Box(new Vector3(3.85f, .93f, 3.365f), new Vector3(.11f, .17f, .012f), White, .005f);
            // Notice board and clipped work sheets on the far left wall.
            Box(new Vector3(-3.35f, 2.08f, 3.78f), new Vector3(.75f, .56f, .045f), Navy, .018f);
            for (int i = 0; i < 3; i++)
            {
                var p = new Vector3(-3.57f + i * .22f, 2.09f, 3.746f);
                Box(p, new Vector3(.18f, .34f, .008f), White, .002f);
                Box(p + new Vector3(0, .14f, -.008f), new Vector3(.07f, .018f, .009f), Teal, .002f);
                for (int j = 0; j < 5; j++) Box(p + new Vector3(0, .07f - j * .037f, -.006f), new Vector3(.12f, .006f, .005f), Slate, 0);
            }
            Mesh mesh = g.ToMesh("PeripheralDetails");
            string path = Folder + "/PeripheralDetails.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) AssetDatabase.CreateAsset(mesh, path);
            else { EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); mesh = existing; }
            var details = new GameObject(detailName, typeof(MeshFilter), typeof(MeshRenderer));
            details.layer = 2;
            details.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = details.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Palette.mat");
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Debug.Log("PERIPHERAL_DETAILS: " + mesh.triangles.Length / 3 + " triangles; one renderer; no colliders or lights.");
        }

        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/SampleScene.unity") throw new InvalidOperationException("Open SampleScene first.");
            var before = Snapshot(scene);
            var old = scene.GetRootGameObjects().FirstOrDefault(o => o.name == RootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            EnsureFolder("Assets/Art"); EnsureFolder(Folder);
            palette = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Palette.mat");
            if (palette == null)
            {
                palette = new Material(Shader.Find("SortQuest/Quest Lab Palette")) { name = "Quest Lab Palette", enableInstancing = true };
                AssetDatabase.CreateAsset(palette, Folder + "/Palette.mat");
            }
            root = new GameObject(RootName).transform;
            zones.Clear();
            Architecture(); Landscape(); Workcell(scene); Storage(); FloorDetails(); Signage();
            int triangles = 0;
            foreach (var zone in zones)
            {
                Mesh mesh = zone.Value.ToMesh(zone.Key);
                triangles += mesh.triangles.Length / 3;
                string path = Folder + "/" + zone.Key + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing == null) AssetDatabase.CreateAsset(mesh, path);
                else { EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); mesh = existing; }
                var go = new GameObject(zone.Key, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(root, false); go.layer = 2;
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = palette;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                // Already combined per zone. Avoid Unity duplicating these meshes for static batching.
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic);
            }
            var floor = Find(scene, "Floor")?.GetComponent<Renderer>();
            if (floor != null)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Floor.mat");
                if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Simple Lit")); AssetDatabase.CreateAsset(mat, Folder + "/Floor.mat"); }
                mat.SetColor("_BaseColor", Hex("505B5D")); mat.SetFloat("_Smoothness", .15f);
                floor.sharedMaterial = mat;
            }
            ConfigureMobile();
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("C7DDD9"); RenderSettings.ambientEquatorColor = Hex("83A7AB"); RenderSettings.ambientGroundColor = Hex("354F60");
            var after = Snapshot(scene);
            foreach (var pair in before)
                if (!after.TryGetValue(pair.Key, out string value) || pair.Value != value)
                    throw new InvalidOperationException("Original component changed: " + pair.Key);
            if (root.GetComponentsInChildren<Collider>(true).Length != 0 || root.GetComponentsInChildren<Rigidbody>(true).Length != 0)
                throw new InvalidOperationException("Art must not contain physics components.");
            int renderers = root.GetComponentsInChildren<Renderer>().Length;
            if (triangles > 18000 || renderers > 24) throw new InvalidOperationException("Art exceeded its mesh/renderer budget.");
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            // Retire only the previous pass's generated materials, now unused by the scene.
            if (AssetDatabase.IsValidFolder("Assets/Materials/VisualPolish")) AssetDatabase.DeleteAsset("Assets/Materials/VisualPolish");
            Directory.CreateDirectory("docs");
            File.WriteAllText("docs/QUEST_VISUAL_AUDIT.md", $"# Quest scene art audit\n\nGenerated from the saved scene by the Unity editor.\n\n- Decorative mesh triangles: **{triangles:N0}** (excludes text).\n- Decorative mesh renderers: **{zones.Count}**, each using the same opaque palette material.\n- Total decorative renderers including text: **{renderers}**.\n- Added textures, realtime lights, shadow casters, colliders, rigidbodies, and runtime update scripts: **0**.\n- Original nonvisual component snapshots verified unchanged: **{before.Count:N0}**.\n- URP mobile: existing 0.8 render scale and 4x MSAA retained; one 1024px main-light shadow map, 12m range; no additional lights, depth texture, opaque texture, HDR, or added post-processing.\n\nThese are scene complexity checks, not headset frame-time measurements. The original robot/hand/graph renderers, camera capture and training simulation are additional costs. A standalone Quest 2 run with CPU/GPU profiling is required before claiming a stable frame rate.\n\nArt uses opaque vertex colors with directional shading computed at build time. Geometry is combined by room zone so culling remains useful. The custom shader supports Unity stereo instancing/multiview macros. No transparent window layers or full-screen effects are used.\n\n## On-device validation\n\nBuild a Development APK, connect the Quest 2, and measure CPU/GPU frame time during human sorting, augmentation/training, robot trials, and camera capture. At 72 Hz the total frame budget is 13.89 ms; leave headroom and check thermals over 15 minutes. Check text legibility, both-eye rendering, hand tracking, and all bin interactions.\n\nSources: [Meta performance guidance](https://developers.meta.com/horizon/documentation/unity/unity-perf/), [Unity untethered XR optimization](https://docs.unity.com/en-us/engine/6000.5/manual/xr/graphics/untethered-device-optimization).\n");
            Debug.Log($"QUEST_ART_PASS: {triangles} triangles, {renderers} decorative renderers; {before.Count} original nonvisual components unchanged.");
        }

        private static void Architecture()
        {
            Zone("Architecture_Rear");
            Box(new Vector3(0, 2.15f, 4.3f), new Vector3(9.2f, 4.3f, .18f), Slate, .035f);
            Box(new Vector3(0, .48f, 4.14f), new Vector3(9, .96f, .15f), Navy, .05f);
            Box(new Vector3(0, .98f, 4.03f), new Vector3(9, .045f, .025f), Amber, .008f);
            // Central inset: a quiet high-contrast background for the existing live UI.
            Box(new Vector3(0, 2.06f, 4.05f), new Vector3(3.85f, 2.13f, .23f), Ivory, .16f);
            Box(new Vector3(0, 2.02f, 3.905f), new Vector3(3.56f, 1.9f, .04f), Ink, .12f);
            for (int i = 0; i < 6; i++) Box(new Vector3(-1.58f + .09f * i, 1.18f, 3.874f), new Vector3(.035f, .04f + i * .016f, .007f), DimTeal, .002f);
            Box(new Vector3(0, 3.32f, 4.02f), new Vector3(8.3f, .57f, .25f), Ivory, .12f);
            Box(new Vector3(0, 3.63f, 3.98f), new Vector3(7.9f, .024f, .08f), Alloy, .009f);
            for (int s = -1; s <= 1; s += 2)
            {
                Box(new Vector3(s * 4.3f, 2.1f, 4.04f), new Vector3(.24f, 4.05f, .4f), Ivory, .055f);
                Box(new Vector3(s * 4.12f, 2.4f, 3.9f), new Vector3(.035f, 2.2f, .04f), Alloy, .01f);
                Box(new Vector3(s * 3, .51f, 3.7f), new Vector3(2.1f, .72f, .58f), Navy, .09f);
                Box(new Vector3(s * 3, .9f, 3.67f), new Vector3(2.17f, .065f, .63f), Alloy, .022f);
                for (int i = 0; i < 3; i++)
                {
                    Box(new Vector3(s * 3 + (i - 1) * .65f, .51f, 3.395f), new Vector3(.6f, .54f, .035f), Slate, .025f);
                    Box(new Vector3(s * 3 + (i - 1) * .65f, .67f, 3.37f), new Vector3(.21f, .018f, .015f), Ivory, .005f);
                }
            }
            for (int s = -1; s <= 1; s += 2)
            {
                Zone(s < 0 ? "Architecture_Left" : "Architecture_Right");
                Box(new Vector3(s * 4.55f, 2.1f, -.4f), new Vector3(.15f, 4.2f, 9.3f), Slate, .02f);
                Box(new Vector3(s * 4.43f, .55f, -.4f), new Vector3(.11f, 1.1f, 9.2f), Navy, .035f);
                for (int i = -1; i <= 2; i++)
                {
                    Box(new Vector3(s * 4.32f, 2.45f, i * 2.15f - .8f), new Vector3(.12f, 2.56f, 1.96f), Ivory, .07f);
                    Box(new Vector3(s * 4.24f, 1.19f, i * 2.15f - .8f), new Vector3(.04f, .034f, 1.5f), Alloy, .01f);
                    for(int rib=0;rib<8;rib++) Box(new Vector3(s*4.242f,2.45f,i*2.15f-1.6f+rib*.23f),new Vector3(.025f,2.38f,.018f),Ivory,0);
                }
                // Inset utility louvers; modeled in the combined mesh, not separate renderers.
                for (int i = 0; i < 12; i++) Box(new Vector3(s * 4.235f, 1.9f + i * .045f, -1.6f), new Vector3(.025f, .019f, .65f), Navy, .004f);
            }
            Zone("Ceiling");
            Box(new Vector3(0, 4.23f, -.2f), new Vector3(9.2f, .08f, 9), Navy, .01f);
            for (int j = -1; j <= 1; j++)
            {
                float z = j * 2.5f + 1;
                Box(new Vector3(0, 3.98f, z), new Vector3(8.9f, .17f, .2f), Ivory, .045f);
                Box(new Vector3(0, 3.875f, z), new Vector3(6.8f, .024f, .10f), White, .01f, true);
                for (int s = -1; s <= 1; s += 2)
                    Beam(new Vector3(s * 4.3f, 3.1f, z), new Vector3(s * 3.7f, 3.93f, z), .18f, .26f, Ivory);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                Box(new Vector3(s * 2.2f, 4.15f, -.1f), new Vector3(.14f, .08f, 8.7f), Slate, .025f);
                Box(new Vector3(s * 2.2f, 4.10f, -.1f), new Vector3(.045f, .012f, 8.5f), DimTeal, .003f, true);
            }
        }

        private static void Landscape()
        {
            Zone("Landscape_Panels");
            foreach (int s in new[] { -1, 1 })
            {
                float x = s * 3.03f;
                Box(new Vector3(x, 2.03f, 4.025f), new Vector3(2.02f, 1.84f, .07f), Ink, .11f);
                // Opaque scenic insets: no glass, transparency, expensive skybox or distant scene.
                g.Quad(new Vector3(x-.9f,1.2f,3.981f),new Vector3(x+.9f,1.2f,3.981f),new Vector3(x+.9f,2.85f,3.981f),new Vector3(x-.9f,2.85f,3.981f), Vector3.back, Hex("97D6BF"), Hex("315A76"));
                Disc(new Vector3(x+.42f,2.5f,3.966f), .18f, White, 24, Quaternion.Euler(90,0,0));
                for(int layer=0;layer<3;layer++)
                {
                    var pts=new List<Vector3>{new Vector3(x-.9f,1.21f,3.95f-layer*.015f)};
                    for(int j=0;j<=8;j++) pts.Add(new Vector3(x-.9f+j*.225f,1.65f+layer*.08f+.35f*Mathf.Sin(j*1.1f+layer*2+s),3.95f-layer*.015f));
                    pts.Add(new Vector3(x+.9f,1.21f,3.95f-layer*.015f));
                    g.Polygon(pts.ToArray(),Vector3.back,Hex(new[]{"548C8A","326D6D","204E54"}[layer]),true);
                }
                Box(new Vector3(x,2.03f,3.84f),new Vector3(.055f,1.68f,.1f),Alloy,.008f);
                Box(new Vector3(x,2.05f,3.84f),new Vector3(1.85f,.055f,.1f),Alloy,.008f);
                for(int i=0;i<5;i++)
                {
                    float tx=x-.73f+i*.35f;
                    Box(new Vector3(tx,1.47f,3.885f),new Vector3(.017f,.35f,.016f),Navy,.002f);
                    g.Polygon(new[]{new Vector3(tx-.12f,1.48f,3.872f),new Vector3(tx,1.98f-(i%2)*.12f,3.872f),new Vector3(tx+.12f,1.48f,3.872f)},Vector3.back,Hex("1E4B45"),true);
                }
                Box(new Vector3(x,1.2f,3.84f),new Vector3(2.04f,.09f,.24f),Ivory,.025f);
            }
        }

        private static void Workcell(Scene scene)
        {
            Zone("Workcell_Frame");
            var belt=Find(scene,"BeltSurface");
            if(belt==null)return;
            Bounds b=belt.GetComponent<Renderer>().bounds;
            Box(new Vector3(b.center.x,b.min.y-.095f,b.center.z),new Vector3(b.size.x+.08f,.17f,b.size.z+.12f),Alloy,.035f);
            Box(new Vector3(b.center.x,b.min.y-.065f,b.min.z-.072f),new Vector3(b.size.x-.08f,.12f,.035f),Navy,.018f);
            Box(new Vector3(b.center.x,b.min.y-.036f,b.min.z-.095f),new Vector3(b.size.x-.2f,.012f,.007f),Amber,.003f,true);
            for(int i=0;i<20;i++)Box(new Vector3(b.min.x+.14f+i*(b.size.x-.28f)/19,b.min.y-.1f,b.min.z-.094f),new Vector3(.018f,.035f,.007f),Slate,.003f);
            foreach(float x in new[]{b.min.x+.2f,b.max.x-.2f})
            {
                Box(new Vector3(x,.38f,b.center.z),new Vector3(.15f,.7f,.31f),Navy,.035f);
                Box(new Vector3(x,.05f,b.center.z),new Vector3(.36f,.08f,.55f),Alloy,.035f);
                Box(new Vector3(x,.40f,b.min.z-.014f),new Vector3(.047f,.45f,.017f),Amber,.01f);
            }
            // Cosmetic collars stay outside bin walls and below their openings.
            var bins=new[]{"Bin_Metal","Bin_Plastic","Bin_Paper","Bin_Hazardous"};
            var colors=new[]{Hex("578EFF"),Hex("F5D852"),Hex("55CF81"),Hex("F66B64")};
            for(int i=0;i<bins.Length;i++)
            {
                var bin=Find(scene,bins[i]); if(bin==null)continue;
                Vector3 p=bin.transform.position;
                Box(p+new Vector3(0,.055f,-.204f),new Vector3(.42f,.08f,.025f),Navy,.014f);
                Box(p+new Vector3(0,.37f,-.207f),new Vector3(.29f,.15f,.015f),Navy,.025f);
                Box(p+new Vector3(0,.265f,-.215f),new Vector3(.18f,.018f,.01f),colors[i],.004f,true);
                for(int j=0;j<=i;j++)Box(p+new Vector3(-i*.024f+j*.048f,.375f,-.22f),new Vector3(.02f,.05f,.008f),colors[i],.004f,true);
            }
            // Rear equipment islands away from the interaction volume.
            Zone("Equipment");
            foreach(int s in new[]{-1,1})
            {
                Box(new Vector3(s*3.35f,.5f,1.95f),new Vector3(.72f,1,.7f),Ivory,.1f);
                Box(new Vector3(s*3.35f,.54f,1.565f),new Vector3(.51f,.66f,.06f),Navy,.055f);
                Box(new Vector3(s*3.35f,.82f,1.52f),new Vector3(.31f,.04f,.012f),Teal,.01f,true);
                for(int j=0;j<6;j++)Box(new Vector3(s*3.35f,.34f+j*.05f,1.523f),new Vector3(.32f,.013f,.009f),Alloy,.003f);
                Cylinder(new Vector3(s*3.35f,1.04f,1.95f),.19f,.07f,Slate,16);
            }
        }

        private static void Storage()
        {
            Zone("Storage_Left");
            Color wood=Hex("997344"), cardboard=Hex("B79667"), cardboardEdge=Hex("796443");
            foreach(int side in new[]{-1,1})
            {
                if(side==1)Zone("Storage_Right");
                float x=side*3.15f, z=2.88f;
                // Pallets, straps and compressed material bales: static scenery outside the play area.
                for(int row=0;row<5;row++)Box(new Vector3(x,.115f,z-.42f+row*.21f),new Vector3(1.15f,.08f,.16f),wood,.015f);
                foreach(float sx in new[]{-.43f,0,.43f})Box(new Vector3(x+sx,.05f,z),new Vector3(.12f,.10f,1.03f),cardboardEdge,.012f);
                for(int level=0;level<2;level++)
                {
                    var p=new Vector3(x,.41f+level*.49f,z);
                    Box(p,new Vector3(1.04f,.46f,.86f),side<0?cardboard:Hex("A7B2AE"),.045f);
                    for(int line=0;line<8;line++)
                        Box(p+new Vector3(0,-.19f+line*.052f,-.434f),new Vector3(.98f,.009f,.006f),side<0?cardboardEdge:Alloy,0);
                    foreach(float band in new[]{-.28f,.28f})
                    {
                        Box(p+new Vector3(band,0,-.44f),new Vector3(.024f,.45f,.008f),Navy,.003f);
                        Box(p+new Vector3(band,.234f,0),new Vector3(.024f,.008f,.85f),Navy,.002f);
                    }
                }
                // Guardrail behind storage, never across the player's sorting/reaching space.
                for(int post=0;post<3;post++)
                {
                    Vector3 p=new Vector3(x-.68f+post*.68f,.5f,z-1.0f);
                    Cylinder(p,.035f,1f,Amber,10);
                    Box(new Vector3(p.x,.025f,p.z),new Vector3(.15f,.05f,.15f),Navy,.015f);
                }
                Beam(new Vector3(x-.68f,.92f,z-1.0f),new Vector3(x+.68f,.92f,z-1.0f),.05f,.05f,Amber);
                Beam(new Vector3(x-.68f,.52f,z-1.0f),new Vector3(x+.68f,.52f,z-1.0f),.04f,.04f,Amber);
            }
            Zone("Utilities");
            foreach(float y in new[]{3.68f,3.79f})Beam(new Vector3(-4.2f,y,4.0f),new Vector3(4.2f,y,4.0f),.055f,.055f,Alloy);
            for(int i=-3;i<=3;i++)Box(new Vector3(i,3.735f,3.96f),new Vector3(.026f,.26f,.025f),Navy,.004f);
            for(int s=-1;s<=1;s+=2)
            {
                // Visual service panels: no screens, buttons, colliders or new interaction affordances.
                Box(new Vector3(s*4.22f,1.72f,.1f),new Vector3(.16f,.9f,.66f),Alloy,.03f);
                Box(new Vector3(s*4.125f,1.72f,.1f),new Vector3(.018f,.75f,.53f),Ivory,.02f);
            }
        }

        private static void FloorDetails()
        {
            Zone("Floor_Inlays");
            // All markings are flush surfaces; there are no simulated steps or obstacles.
            g.Polygon(new[]{new Vector3(-1.85f,.003f,-.95f),new Vector3(1.85f,.003f,-.95f),new Vector3(2.08f,.003f,-.72f),new Vector3(2.08f,.003f,1.7f),new Vector3(1.85f,.003f,1.93f),new Vector3(-1.85f,.003f,1.93f),new Vector3(-2.08f,.003f,1.7f),new Vector3(-2.08f,.003f,-.72f)},Vector3.up,Navy,true);
            for(int i=-4;i<=4;i++)
            {
                Box(new Vector3(i,.006f,-.1f),new Vector3(.006f,.002f,8.4f),Slate,0);
                Box(new Vector3(0,.006f,i),new Vector3(8.8f,.002f,.006f),Slate,0);
            }
            foreach(int s in new[]{-1,1})
            {
                Box(new Vector3(s*2.13f,.009f,.4f),new Vector3(.045f,.002f,2.55f),Amber,0,true);
                Box(new Vector3(s*2.26f,.008f,.4f),new Vector3(.011f,.002f,2.55f),Slate,0);
                for(int j=0;j<8;j++)Box(new Vector3(s*2.13f,.009f,2.1f+j*.14f),new Vector3(.11f,.003f,.05f),Amber,0,true);
            }
            for(int s=-1;s<=1;s+=2)
                for(int j=0;j<3;j++)
                {
                    float z=-1.8f-j*.2f;
                    g.Polygon(new[]{new Vector3(s*.13f,.008f,z),new Vector3(s*.23f,.008f,z-.1f),new Vector3(s*.23f,.008f,z-.045f),new Vector3(s*.13f,.008f,z+.055f)},Vector3.up,DimTeal,true);
                }
        }

        private static void Signage()
        {
            Text("SORTQUEST",new Vector3(-1.32f,3.37f,3.865f),3.5f,.3f,3.35f,Navy);
            Text("MATERIAL RECOVERY FACILITY",new Vector3(-1.3f,3.15f,3.865f),3.5f,.12f,.85f,Navy);
            Text("01",new Vector3(3.06f,3.37f,3.86f),.65f,.32f,3.15f,DimTeal);
            Text("SORTING LINE",new Vector3(2.7f,3.13f,3.86f),1.2f,.12f,.76f,Slate);
            Text("SORT MATERIALS / REDUCE WASTE",new Vector3(-3.86f,1.1f,3.78f),1.9f,.11f,.78f,Teal);
            Text("RECOVER. REUSE. REPEAT.",new Vector3(2.2f,1.1f,3.78f),1.9f,.11f,.76f,Teal);
            Zone("Brand_Geometry");
            var center=new Vector3(-1.86f,3.34f,3.82f);
            for(int i=0;i<3;i++)
            {
                for(int j=0;j<4;j++)
                {
                    float a=(i*120+j*18)*Mathf.Deg2Rad,b=a+18*Mathf.Deg2Rad;
                    g.Polygon(new[]{center+new Vector3(Mathf.Cos(a)*.155f,Mathf.Sin(a)*.155f,0),center+new Vector3(Mathf.Cos(b)*.155f,Mathf.Sin(b)*.155f,0),center+new Vector3(Mathf.Cos(b)*.115f,Mathf.Sin(b)*.115f,0),center+new Vector3(Mathf.Cos(a)*.115f,Mathf.Sin(a)*.115f,0)},Vector3.back,DimTeal,true);
                }
                float angle=(i*120+72)*Mathf.Deg2Rad;
                Vector3 radial=new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),0),tangent=new Vector3(-Mathf.Sin(angle),Mathf.Cos(angle),0);
                Vector3 head=center+radial*.135f;
                g.Polygon(new[]{head+radial*.055f,head-radial*.055f,head+tangent*.075f},Vector3.back,DimTeal,true);
            }
        }

        private static void ConfigureMobile()
        {
            var asset=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            if(asset==null)throw new InvalidOperationException("Mobile URP asset not found.");
            asset.shadowDistance=12f; asset.shadowCascadeCount=1;
            var settings = new SerializedObject(asset);
            settings.FindProperty("m_AdditionalLightsRenderingMode").intValue = (int)LightRenderingMode.Disabled;
            settings.ApplyModifiedPropertiesWithoutUndo();
            asset.supportsHDR=false; asset.supportsCameraDepthTexture=false; asset.supportsCameraOpaqueTexture=false;
            asset.useSRPBatcher=true;
            EditorUtility.SetDirty(asset);
        }
        private static void Zone(string name){g=new Geometry();zones[name]=g;}
        private static Color Hex(string value){ColorUtility.TryParseHtmlString("#"+value,out var c);return c;}
        private static void EnsureFolder(string path){if(!AssetDatabase.IsValidFolder(path))AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));}
        private static GameObject Find(Scene scene,string name)=>scene.GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t=>t.name==name)?.gameObject;
        private static Dictionary<string,string> Snapshot(Scene scene)
        {
            var result=new Dictionary<string,string>();
            foreach(var go in scene.GetRootGameObjects())if(go.name!=RootName)
                foreach(var c in go.GetComponentsInChildren<Component>(true))if(c!=null && !(c is Renderer))result[GlobalObjectId.GetGlobalObjectIdSlow(c).ToString()]=EditorJsonUtility.ToJson(c);
            return result;
        }
        private static void Text(string value,Vector3 position,float width,float height,float size,Color color)
        {
            var go=new GameObject("Sign - "+value);go.transform.SetParent(root,false);go.transform.position=position;go.layer=2;
            var text=go.AddComponent<TextMeshPro>();text.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            text.text=value;text.fontSize=size;text.color=color;text.alignment=TextAlignmentOptions.Left;
            if(value=="SORTQUEST")text.fontStyle=FontStyles.Bold;
            text.rectTransform.pivot=new Vector2(0,.5f);text.rectTransform.sizeDelta=new Vector2(width,height);text.textWrappingMode=TextWrappingModes.NoWrap;
        }
        private static void Beam(Vector3 from,Vector3 to,float width,float depth,Color color)
            =>Box((from+to)*.5f,new Vector3(width,(to-from).magnitude,depth),color,.02f,false,Quaternion.FromToRotation(Vector3.up,to-from));
        private static void Box(Vector3 p,Vector3 size,Color c,float bevel=.015f,bool glow=false,Quaternion? rotation=null)
        {
            Vector3 h=size*.5f;float b=Mathf.Min(bevel,Mathf.Min(h.x,Mathf.Min(h.y,h.z))*.7f);Vector3 inner=h-Vector3.one*b;Quaternion q=rotation??Quaternion.identity;
            Action<Vector3[],Vector3> face=(v,n)=>g.Polygon(v.Select(x=>p+q*x).ToArray(),q*n,c,glow);
            for(int axis=0;axis<3;axis++)for(int sign=-1;sign<=1;sign+=2)
            {
                int u=(axis+1)%3,v=(axis+2)%3;var pts=new Vector3[4];int[] su={-1,1,1,-1},sv={-1,-1,1,1};
                for(int j=0;j<4;j++){pts[j][axis]=sign*h[axis];pts[j][u]=su[j]*inner[u];pts[j][v]=sv[j]*inner[v];}
                Vector3 n=Vector3.zero;n[axis]=sign;face(pts,n);
            }
            if(b<=0)return;
            for(int axis=0;axis<3;axis++)for(int a=-1;a<=1;a+=2)for(int d=-1;d<=1;d+=2)
            {
                int u=(axis+1)%3,v=(axis+2)%3;Vector3[] pts=new Vector3[4];
                for(int j=0;j<4;j++){pts[j][axis]=(j<2?-1:1)*inner[axis];pts[j][u]=a*(j==0||j==3?h[u]:inner[u]);pts[j][v]=d*(j==0||j==3?inner[v]:h[v]);}
                Vector3 n=Vector3.zero;n[u]=a;n[v]=d;face(pts,n.normalized);
            }
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
                face(new[]{new Vector3(x*h.x,y*inner.y,z*inner.z),new Vector3(x*inner.x,y*h.y,z*inner.z),new Vector3(x*inner.x,y*inner.y,z*h.z)},new Vector3(x,y,z).normalized);
        }
        private static void Cylinder(Vector3 p,float radius,float height,Color c,int segments)
        {
            Vector3[] top=new Vector3[segments],bottom=new Vector3[segments];
            for(int i=0;i<segments;i++){float a=i*Mathf.PI*2/segments;top[i]=p+new Vector3(Mathf.Cos(a)*radius,height*.5f,Mathf.Sin(a)*radius);bottom[i]=top[i]-Vector3.up*height;}
            g.Polygon(top,Vector3.up,c);g.Polygon(bottom,Vector3.down,c);
            for(int i=0;i<segments;i++){int j=(i+1)%segments;g.Polygon(new[]{top[i],bottom[i],bottom[j],top[j]},((top[i]+top[j])*.5f-p-Vector3.up*height*.5f).normalized,c);}
        }
        private static void Disc(Vector3 p,float radius,Color c,int segments,Quaternion q)
        {
            var pts=new Vector3[segments];for(int i=0;i<segments;i++){float a=i*Mathf.PI*2/segments;pts[i]=p+q*new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius);}g.Polygon(pts,q*Vector3.up,c,true);
        }
        private sealed class Geometry
        {
            private readonly List<Vector3> vertices=new List<Vector3>();private readonly List<Color> colors=new List<Color>();private readonly List<int> indices=new List<int>();
            public void Polygon(Vector3[] points,Vector3 normal,Color c,bool glow=false)
            {
                if(points.Length<3)return;
                float light=glow?1f:.58f+.42f*Mathf.Max(0,Vector3.Dot(normal.normalized,new Vector3(-.3f,.75f,-.6f).normalized));
                int start=vertices.Count;foreach(var p in points){vertices.Add(p);Color shaded=c*light;shaded.a=1;colors.Add(shaded.linear);}
                for(int i=1;i<points.Length-1;i++){bool reverse=Vector3.Dot(Vector3.Cross(points[i]-points[0],points[i+1]-points[0]),normal)<0;indices.Add(start);indices.Add(start+(reverse?i+1:i));indices.Add(start+(reverse?i:i+1));}
            }
            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 normal,Color low,Color high)
            {
                int start=vertices.Count;Polygon(new[]{a,b,c,d},normal,Color.white,true);colors[start]=low.linear;colors[start+1]=low.linear;colors[start+2]=high.linear;colors[start+3]=high.linear;
            }
            public void Leaf(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 ridge,Color color)
            {
                var ring=new[]{a,b,c,d};for(int i=0;i<4;i++){var next=ring[(i+1)%4];Polygon(new[]{ring[i],next,ridge},Vector3.up,color);Polygon(new[]{next,ring[i],ridge-Vector3.up*.035f},Vector3.down,color);}
            }
            public Mesh ToMesh(string name)
            {
                var mesh=new Mesh{name=name};if(vertices.Count>65535)mesh.indexFormat=IndexFormat.UInt32;mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();return mesh;
            }
        }
    }
}
