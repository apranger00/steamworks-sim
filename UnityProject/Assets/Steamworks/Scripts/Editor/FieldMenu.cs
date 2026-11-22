using SteamworksSim.Cameras;
using SteamworksSim.Field;
using SteamworksSim.Game;
using UnityEditor;
using UnityEngine;

namespace SteamworksSim.EditorTools
{
    /// <summary>Steamworks menu: create the field in the open scene, rebuild it, toggle the CAD reference.</summary>
    public static class FieldMenu
    {
        const string DataDir = "Assets/Steamworks/Data/";
        const string CadModel = "Assets/Steamworks/Reference/FieldCAD.obj";
        const string CadMaterial = "Assets/Steamworks/Reference/CADGhost.mat";
        const string CadName = "CAD Reference (official field, decimated)";

        [MenuItem("Steamworks/Create Field in Scene", priority = 0)]
        public static void CreateField()
        {
            var fb = Object.FindAnyObjectByType<FieldBuilder>();
            if (fb == null)
            {
                var go = new GameObject("Steamworks Field");
                Undo.RegisterCreatedObjectUndo(go, "Create Steamworks Field");
                fb = go.AddComponent<FieldBuilder>();
            }
            fb.fieldJson = AssetDatabase.LoadAssetAtPath<TextAsset>(DataDir + "field.json");
            fb.collidersJson = AssetDatabase.LoadAssetAtPath<TextAsset>(DataDir + "colliders.json");
            fb.litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (fb.fieldJson == null) { Debug.LogError("[Steamworks] Missing " + DataDir + "field.json"); return; }
            if (fb.litShader == null) Debug.LogWarning("[Steamworks] URP Lit not found: is the project using the Universal Render Pipeline? Falling back to Standard.");
            EditorUtility.SetDirty(fb);
            fb.BuildPreview();

            var cam = Camera.main;
            if (cam != null)
            {
                if (cam.GetComponent<FieldOrbitCamera>() == null) Undo.AddComponent<FieldOrbitCamera>(cam.gameObject);
                cam.farClipPlane = Mathf.Max(cam.farClipPlane, 200f);
                cam.nearClipPlane = 0.05f;
                cam.GetComponent<FieldOrbitCamera>().SnapTo(FieldOrbitCamera.Preset.Overview);
                EditorUtility.SetDirty(cam);
            }
            Selection.activeObject = fb.gameObject;
            SceneView.lastActiveSceneView?.Frame(new Bounds(Vector3.up * 1f, new Vector3(18f, 3f, 9f)), false);
            Debug.Log("[Steamworks] Field created. Press Play to build it for real, or use Steamworks > Rebuild Field Preview after editing data.");
        }

        [MenuItem("Steamworks/Set Up Drive Test", priority = 2)]
        public static void SetUpDriveTest()
        {
            var fb = Object.FindAnyObjectByType<FieldBuilder>();
            if (fb == null) { CreateField(); fb = Object.FindAnyObjectByType<FieldBuilder>(); }
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                Undo.RegisterCreatedObjectUndo(camGo, "Create camera");
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            if (cam.GetComponent<FieldOrbitCamera>() == null) Undo.AddComponent<FieldOrbitCamera>(cam.gameObject);
            var rig = cam.GetComponent<RobotCameraRig>();
            if (rig == null) rig = Undo.AddComponent<RobotCameraRig>(cam.gameObject);

            var boot = Object.FindAnyObjectByType<SimBootstrap>();
            if (boot == null)
            {
                var go = new GameObject("Sim");
                Undo.RegisterCreatedObjectUndo(go, "Create Sim");
                boot = go.AddComponent<SimBootstrap>();
            }
            boot.field = fb;
            boot.cameraRig = rig;
            EditorUtility.SetDirty(boot);
            EditorUtility.SetDirty(cam.gameObject);
            Selection.activeObject = boot.gameObject;

            // A saved scene goes into the build list, so File > Build Profiles builds the game
            var scenePath = boot.gameObject.scene.path;
            if (!string.IsNullOrEmpty(scenePath))
            {
                var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
                if (!list.Exists(s => s.path == scenePath))
                {
                    list.Insert(0, new EditorBuildSettingsScene(scenePath, true));
                    EditorBuildSettings.scenes = list.ToArray();
                }
            }
            Debug.Log("[Steamworks] Game scene ready. Save the scene (Ctrl+S), run this menu once more to add it to the build list, then press Play.");
        }

        [MenuItem("Steamworks/Run Scoring Self-Test", priority = 40)]
        public static void RunScoringSelfTest()
        {
            string report = MatchRulesSelfTest.Run(out int passed, out int failed);
            string head = $"[Steamworks] Scoring self-test: {passed} passed, {failed} failed (ported from prototype/tests/game.mjs)\n";
            if (failed == 0) Debug.Log(head + report); else Debug.LogError(head + report);
        }

        [MenuItem("Steamworks/Rebuild Field Preview", priority = 1)]
        public static void Rebuild()
        {
            var fb = Object.FindAnyObjectByType<FieldBuilder>();
            if (fb == null) { CreateField(); return; }
            fb.BuildPreview();
        }

        [MenuItem("Steamworks/Toggle CAD Reference", priority = 20)]
        public static void ToggleCad()
        {
            var existing = GameObject.Find(CadName);
            if (existing != null) { Undo.DestroyObjectImmediate(existing); return; }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CadModel);
            if (model == null) { Debug.LogError("[Steamworks] Missing " + CadModel); return; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            inst.name = CadName;
            Undo.RegisterCreatedObjectUndo(inst, "Add CAD reference");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(CadMaterial);
            if (mat == null)
            {
                // Save the ghost material as an asset so the scene keeps the reference.
                var src = new FieldMaterials(Shader.Find("Universal Render Pipeline/Lit")).Get("cadGhost");
                mat = new Material(src);
                AssetDatabase.CreateAsset(mat, CadMaterial);
                AssetDatabase.SaveAssets();
            }
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
    }
}
