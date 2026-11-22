using System.Collections.Generic;
using UnityEngine;

namespace SteamworksSim.Field
{
    /// <summary>
    /// Builds the STEAMWORKS field from Data/field.json (visuals) and Data/colliders.json (static
    /// collision prisms). Runs on Awake in Play mode; "Build preview" (component menu, or
    /// Steamworks > Rebuild Field Preview) shows it in the editor without saving generated meshes
    /// into the scene.
    ///
    /// Hierarchy: Generated Field / { Field, Tape, Staged Pieces, Colliders }.
    /// Visual meshes are combined per material and group to keep draw calls low.
    /// </summary>
    [DisallowMultipleComponent]
    public class FieldBuilder : MonoBehaviour
    {
        public TextAsset fieldJson;
        public TextAsset collidersJson;
        [Tooltip("URP Lit (assigned by the editor menu so builds include it).")]
        public Shader litShader;
        public bool buildOnAwake = true;
        public bool buildColliders = true;
        [Tooltip("Static FUEL in hoppers and loading lanes, and GEARS in the loading lanes.")]
        public bool includeStagedPieces = true;
        [Tooltip("Official reserve GEAR on each airship deck (off: harder game, matches the prototype default).")]
        public bool reserveGear = false;
        [Tooltip("Layer for the static collision prisms.")]
        public int colliderLayer = 0;
        [Tooltip("Build ROTORS and TOUCHPADS as separate objects so the match can spin and light them.")]
        public bool separateAnimatedParts = true;

        public const string AnimatedName = "Animated";

        public const string RootName = "Generated Field";
        public Transform GeneratedRoot { get; private set; }
        public FieldMaterials Materials { get; private set; }

        void Awake()
        {
            if (buildOnAwake) Build(false);
        }

        [ContextMenu("Build preview")]
        public void BuildPreview() => Build(true);

        [ContextMenu("Clear")]
        public void Clear()
        {
            var old = transform.Find(RootName);
            while (old != null)
            {
                old.SetParent(null);
                if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject);
                old = transform.Find(RootName);
            }
            GeneratedRoot = null;
        }

        public void Build(bool preview)
        {
            Clear();
            if (fieldJson == null) { Debug.LogError("[Steamworks] FieldBuilder: assign Data/field.json."); return; }
            var data = JsonUtility.FromJson<FieldFile>(fieldJson.text);
            Materials = new FieldMaterials(litShader);

            GeneratedRoot = new GameObject(RootName).transform;
            GeneratedRoot.SetParent(transform, false);

            // Collect triangles per (group, material) so each pair becomes one mesh.
            var builders = new Dictionary<(string, string), MeshBuilder>();
            MeshBuilder B(string group, string mat)
            {
                var k = (group, mat);
                if (!builders.TryGetValue(k, out var mb)) builders[k] = mb = new MeshBuilder();
                return mb;
            }

            // Moving parts (ROTORS, TOUCHPADS): own mesh per part, around a pivot
            var animated = new Dictionary<string, (Vector3 pivot, Dictionary<string, MeshBuilder> meshes)>();
            MeshBuilder A(string key, Vector3 pivotSpec, string mat)
            {
                if (!animated.TryGetValue(key, out var e)) animated[key] = e = (pivotSpec, new Dictionary<string, MeshBuilder>());
                if (!e.meshes.TryGetValue(mat, out var mb)) e.meshes[mat] = mb = new MeshBuilder();
                return mb;
            }
            int touchpadCount = 0;

            foreach (var it in data.items)
            {
                if (it.mat == "rope") continue; // ropes are simulated objects, not static geometry
                if (it.group == "pieces" && !includeStagedPieces) continue;
                string group = string.IsNullOrEmpty(it.group) ? "field" : it.group;
                Vector3 pivot = default;
                string animKey = separateAnimatedParts ? AnimatedKey(it, ref touchpadCount, out pivot) : null;
                var mb = animKey != null ? A(animKey, pivot, it.mat) : B(group, it.mat);
                switch (it.type)
                {
                    case "box":
                        MeshGen.Box(mb, Units.Spec(it.c), Units.Spec(it.size), it.rotZ);
                        break;
                    case "tape":
                        MeshGen.Strip(mb, new Vector2(it.a[0], it.a[1]), new Vector2(it.b[0], it.b[1]), it.w, 0.02f, 0.17f);
                        break;
                    case "loft":
                        MeshGen.Loft(mb, MeshGen.Ring(it.ring0), it.z0, MeshGen.Ring(it.ring1), it.z1, it.capBottom, it.capTop);
                        break;
                    case "cyl":
                        MeshGen.Cylinder(mb, Units.Spec(it.p0), Units.Spec(it.p1), it.r0, it.r1, it.seg, it.open);
                        break;
                    case "helicoid":
                        MeshGen.Helicoid(mb, new Vector2(it.c[0], it.c[1]), it.r0, it.r1, it.z0, it.z1, it.turns);
                        break;
                    case "dome":
                        MeshGen.Sphere(mb, Units.Spec(it.c), it.r, 16, 6, true);
                        break;
                    default:
                        Debug.LogWarning($"[Steamworks] Unknown field item type '{it.type}' ({it.name}).");
                        break;
                }
            }

            if (includeStagedPieces)
            {
                var fuel = B("pieces", "fuel");
                var tg = separateAnimatedParts ? TargetsFile.Load() : null;
                for (int i = 0; i + 2 < data.fuel.Length; i += 3)
                {
                    // FUEL in a HOPPER pair is its own part, hidden when the pair is dumped onto the field
                    int hop = tg != null ? tg.HopperAt(data.fuel[i], data.fuel[i + 1]) : -1;
                    var mb = hop >= 0 ? A($"HOPPER {hop} fuel", Vector3.zero, "fuel") : fuel;
                    MeshGen.Sphere(mb, new Vector3(data.fuel[i], data.fuel[i + 1], data.fuel[i + 2]), data.fuelRadius, 10, 7);
                }
                var gears = B("pieces", "gear");
                foreach (var g in data.gears)
                {
                    if (!string.IsNullOrEmpty(g.reserve) && !reserveGear) continue;
                    var ring = MeshGen.GearOutline(new Vector2(g.c[0], g.c[1]), 11f, 10, g.rotZ);
                    MeshGen.Loft(gears, ring, g.c[2] - 1f, ring, g.c[2] + 1f, true, true);
                }
            }

            var groupRoots = new Dictionary<string, Transform>();
            Transform Group(string g)
            {
                if (!groupRoots.TryGetValue(g, out var t))
                {
                    string label = g == "field" ? "Field" : g == "tape" ? "Tape" : g == "pieces" ? "Staged Pieces" : g;
                    t = new GameObject(label).transform;
                    t.SetParent(GeneratedRoot, false);
                    groupRoots[g] = t;
                }
                return t;
            }

            foreach (var kv in builders)
            {
                if (kv.Value.VertexCount == 0) continue;
                var (group, mat) = kv.Key;
                var go = new GameObject($"{mat}");
                go.transform.SetParent(Group(group), false);
                go.AddComponent<MeshFilter>().sharedMesh = kv.Value.ToMesh($"{group}_{mat}");
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Materials.Get(mat);
                bool transparent = mat == "poly" || mat == "polyHopper" || mat == "touchpad" || mat == "dome" || mat == "net" || mat == "pushPanel";
                mr.shadowCastingMode = transparent || group == "tape"
                    ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                go.isStatic = !Application.isPlaying;
            }

            if (animated.Count > 0)
            {
                var animRoot = new GameObject(AnimatedName).transform;
                animRoot.SetParent(GeneratedRoot, false);
                foreach (var kv in animated)
                {
                    var pivot = new GameObject(kv.Key).transform;
                    pivot.SetParent(animRoot, false);
                    pivot.localPosition = Units.ToUnity(kv.Value.pivot);
                    foreach (var m in kv.Value.meshes)
                    {
                        var go = new GameObject(m.Key);
                        go.transform.SetParent(pivot, false);
                        go.transform.localPosition = -pivot.localPosition; // vertices are in field space
                        go.AddComponent<MeshFilter>().sharedMesh = m.Value.ToMesh($"{kv.Key}_{m.Key}");
                        var mr = go.AddComponent<MeshRenderer>();
                        mr.sharedMaterial = Materials.Get(m.Key);
                        if (m.Key == "touchpad" || m.Key == "dome") mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    }
                }
            }

            if (buildColliders && collidersJson != null) BuildColliders(JsonUtility.FromJson<ColliderFile>(collidersJson.text));

            if (preview) SetHideFlagsRecursive(GeneratedRoot, HideFlags.DontSave);
        }

        void BuildColliders(ColliderFile cols)
        {
            var root = new GameObject("Colliders").transform;
            root.SetParent(GeneratedRoot, false);
            // Carpet floor: the whole carpet (936 x 360 in), top surface at the carpet (y = 0).
            var floor = new GameObject("Carpet floor");
            floor.layer = colliderLayer;
            floor.transform.SetParent(root, false);
            var box = floor.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.05f, 0.2f * Units.Inch);
            box.size = new Vector3(936f * Units.Inch, 0.1f, 360f * Units.Inch);
            floor.isStatic = true;
            int i = 0;
            foreach (var p in cols.prisms)
            {
                var ring = MeshGen.Ring(p.pts);
                var mb = new MeshBuilder();
                MeshGen.Loft(mb, ring, p.z0, ring, p.z1, true, true);
                var go = new GameObject($"{i++:000} {p.name}");
                go.layer = colliderLayer;
                go.transform.SetParent(root, false);
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mb.ToMesh(p.name);
                mc.convex = true; // every prism is convex by construction
                go.isStatic = true;
            }
        }

        static readonly int[] SmallRotorAngles = { 90, 210, 330 }; // ROTORS 2, 3, 4 (numbering as in the prototype)

        /// <summary>
        /// Name of the moving part this item belongs to, or null for static geometry. ROTOR parts pivot on their
        /// vertical axis; each TOUCHPAD plate and dome is its own part ("RED TOUCHPAD #0").
        /// </summary>
        static string AnimatedKey(FieldItem it, ref int touchpadCount, out Vector3 pivot)
        {
            pivot = Vector3.zero;
            string n = it.name ?? "";
            int sp = n.IndexOf(' ');
            if (sp <= 0) return null;
            string al = n.Substring(0, sp);
            if (al != "RED" && al != "BLUE") return null;
            if (n.EndsWith("ROTOR 1 (central)"))
            {
                pivot = new Vector3(it.c[0], it.c[1], 0f);
                return $"{al} ROTOR 1";
            }
            if (n.Contains("small ROTOR (") && (n.EndsWith(" hub") || n.EndsWith(" blade")))
            {
                for (int k = 0; k < SmallRotorAngles.Length; k++)
                {
                    if (!n.Contains($"({SmallRotorAngles[k]}°)")) continue;
                    var tg = TargetsFile.Load();
                    var ac = tg != null ? tg.Of(al == "BLUE" ? "blue" : "red").airship : new[] { 0f, 0f };
                    float a = SmallRotorAngles[k] * Mathf.Deg2Rad;
                    pivot = new Vector3(ac[0] + Mathf.Cos(a) * 53f, ac[1] + Mathf.Sin(a) * 53f, 0f);
                    return $"{al} ROTOR {k + 2}";
                }
                return null;
            }
            if (n == $"{al} TOUCHPAD" || n == $"{al} TOUCHPAD dome")
            {
                pivot = new Vector3(it.c[0], it.c[1], it.c[2]);
                return $"{n} #{touchpadCount++}";
            }
            return null;
        }

        static void SetHideFlagsRecursive(Transform t, HideFlags flags)
        {
            t.gameObject.hideFlags = flags;
            var mf = t.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) mf.sharedMesh.hideFlags = flags;
            var mc = t.GetComponent<MeshCollider>(); if (mc != null && mc.sharedMesh != null) mc.sharedMesh.hideFlags = flags;
            foreach (Transform c in t) SetHideFlagsRecursive(c, flags);
        }
    }
}
