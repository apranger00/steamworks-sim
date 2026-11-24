using System;
using System.Collections.Generic;
using System.IO;
using SteamworksSim.Field;
using UnityEngine;
using UnityEngine.Rendering;

namespace SteamworksSim.Robots
{
    /// <summary>
    /// A robot's own model (robot.json "model": { "file": "model.json" }), made from its CAD with
    /// tools/step_to_robot_model.py. Inches, x right, y up, z forward, origin on the carpet under the footprint centre.
    /// Each part is a GameObject at its pivot, so moving parts can be animated by name: "Wheel L0".."Wheel R2" (spun
    /// by the drivetrain), "Intake roller" and "Conveyor roller 0..3" (spun while intaking), "Gear slider" (moved
    /// sideways to line the GEAR up with a peg), "Turret yaw" (turned to face the goal; build it shooting along +z).
    /// </summary>
    public static class RobotModel
    {
        [Serializable] class MaterialData { public string name; public float[] color; public float metal, smooth; }
        [Serializable] class MeshData { public int material; public float[] v, n; public int[] t; }
        [Serializable] class PartData { public string name; public float[] pivot; public MeshData[] meshes; }
        [Serializable] class FileData { public string format; public int version; public MaterialData[] materials; public PartData[] parts; }

        class Loaded { public FileData data; public Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>(); }
        static readonly Dictionary<string, Loaded> cache = new Dictionary<string, Loaded>();

        /// <summary>The model file for this robot, or null if it has none (or it is missing).</summary>
        public static string PathOf(RobotDefinition d)
        {
            if (d == null || d.model == null || string.IsNullOrEmpty(d.model.file) || string.IsNullOrEmpty(d.folder)) return null;
            if (!d.model.file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return null;
            string p = Path.Combine(d.folder, d.model.file);
            return File.Exists(p) ? p : null;
        }

        /// <summary>Builds the model under `parent`. False (and nothing built) if the file can't be read.</summary>
        public static bool Build(Transform parent, RobotDefinition d, FieldMaterials mats, out List<Transform> leftWheels, out List<Transform> rightWheels)
        {
            leftWheels = new List<Transform>(); rightWheels = new List<Transform>();
            string path = PathOf(d);
            if (path == null) return false;
            Loaded m;
            if (!cache.TryGetValue(path, out m))
            {
                try
                {
                    var data = JsonUtility.FromJson<FileData>(File.ReadAllText(path));
                    if (data == null || data.format != "steamworks-robot-model" || data.parts == null) throw new Exception("not a steamworks-robot-model file");
                    m = new Loaded { data = data };
                    cache[path] = m;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Steamworks] Robot model {path}: {e.Message}. Using the generated blockout.");
                    return false;
                }
            }
            const float In = Units.Inch;
            var materials = new Material[m.data.materials.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                var md = m.data.materials[i];
                var c = md.color != null && md.color.Length >= 3 ? new Color(md.color[0], md.color[1], md.color[2], md.color.Length > 3 ? md.color[3] : 1f) : Color.gray;
                materials[i] = mats.Custom(md.name, c, md.metal, md.smooth);
            }
            var root = new GameObject("Model").transform;
            root.SetParent(parent, false);
            var off = d.model.offset; var rot = d.model.rotation;
            if (off != null && off.Length >= 3) root.localPosition = new Vector3(-off[1] * In, off[2] * In, off[0] * In);
            if (rot != null && rot.Length >= 3) root.localRotation = Quaternion.Euler(-rot[1], -rot[2], -rot[0]);
            foreach (var p in m.data.parts)
            {
                var go = new GameObject(p.name);
                // parts the game animates sit directly under Visual (found by name); the rest under Model
                bool animated = p.name.StartsWith("Wheel ") || p.name.StartsWith("Intake roller") || p.name.StartsWith("Conveyor roller") || p.name == "Gear slider" || p.name == "Turret yaw";
                go.transform.SetParent(animated ? parent : root, false);
                var pv = p.pivot != null && p.pivot.Length >= 3 ? new Vector3(p.pivot[0], p.pivot[1], p.pivot[2]) * In : Vector3.zero;
                go.transform.localPosition = root.localPosition + root.localRotation * pv;
                go.transform.localRotation = root.localRotation;
                if (!m.meshes.TryGetValue(p.name, out var mesh)) { mesh = MakeMesh(p); m.meshes[p.name] = mesh; }
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                var used = new Material[p.meshes.Length];
                for (int k = 0; k < used.Length; k++) used[k] = materials[Mathf.Clamp(p.meshes[k].material, 0, materials.Length - 1)];
                mr.sharedMaterials = used;
                if (p.name.StartsWith("Wheel L")) leftWheels.Add(go.transform);
                else if (p.name.StartsWith("Wheel R")) rightWheels.Add(go.transform);
            }
            return true;
        }

        static Mesh MakeMesh(PartData p)
        {
            const float In = Units.Inch;
            var verts = new List<Vector3>(); var norms = new List<Vector3>();
            var subs = new List<int[]>();
            foreach (var md in p.meshes)
            {
                int baseV = verts.Count;
                for (int i = 0; i + 2 < md.v.Length; i += 3) verts.Add(new Vector3(md.v[i], md.v[i + 1], md.v[i + 2]) * In);
                for (int i = 0; i + 2 < md.n.Length; i += 3) norms.Add(new Vector3(md.n[i], md.n[i + 1], md.n[i + 2]));
                var t = new int[md.t.Length];
                for (int i = 0; i < t.Length; i++) t[i] = md.t[i] + baseV;
                subs.Add(t);
            }
            var mesh = new Mesh { name = "Robot model: " + p.name };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            if (norms.Count == verts.Count) mesh.SetNormals(norms);
            mesh.subMeshCount = subs.Count;
            for (int k = 0; k < subs.Count; k++) mesh.SetTriangles(subs[k], k, false);
            if (norms.Count != verts.Count) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The team logo (robot.json "logo"), or null. Loaded once per file.</summary>
        public static Texture2D Logo(RobotDefinition d)
        {
            if (d == null || string.IsNullOrEmpty(d.logo) || string.IsNullOrEmpty(d.folder)) return null;
            string p = Path.Combine(d.folder, d.logo);
            if (logos.TryGetValue(p, out var tex)) return tex;
            tex = null;
            try
            {
                if (File.Exists(p))
                {
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "Team logo " + d.team };
                    if (!tex.LoadImage(File.ReadAllBytes(p))) tex = null;
                    else { tex.wrapMode = TextureWrapMode.Clamp; tex.anisoLevel = 4; }
                }
            }
            catch (Exception e) { Debug.LogWarning($"[Steamworks] Team logo {p}: {e.Message}"); tex = null; }
            logos[p] = tex;
            return tex;
        }
        static readonly Dictionary<string, Texture2D> logos = new Dictionary<string, Texture2D>();
    }
}
