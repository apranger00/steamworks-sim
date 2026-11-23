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
        [Tooltip("Leave the loading lanes' GEAR stacks and FUEL bin out: the game draws them from the stations' live stock.")]
        public bool loadingLanesAtRuntime = true;

        /// <summary>Behind an alliance wall (|X| > 326 in): a loading lane.</summary>
        static bool InLoadingLane(float x) => Mathf.Abs(x) > 326f;

        public const string AnimatedName = "Animated";

        public const string RootName = "Generated Field";
        public Transform GeneratedRoot { get; private set; }
        public FieldMaterials Materials { get; private set; }
        /// <summary>The field data of the last build (staged FUEL positions and so on).</summary>
        public FieldFile Data { get; private set; }

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
            Data = data;
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
                if (loadingLanesAtRuntime && it.name != null && it.name.Contains("LOADING LANE")) continue;
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
                    if (loadingLanesAtRuntime && InLoadingLane(data.fuel[i])) continue;
                    // FUEL in a HOPPER pair is its own part, hidden when the pair is dumped onto the field
                    int hop = tg != null ? tg.HopperAt(data.fuel[i], data.fuel[i + 1]) : -1;
                    var mb = hop >= 0 ? A($"HOPPER {hop} fuel", Vector3.zero, "fuel") : fuel;
                    mb.SmoothSphere(new Vector3(data.fuel[i], data.fuel[i + 1], data.fuel[i + 2]), data.fuelRadius, 16, 11);
                }
                var gears = B("pieces", "gear");
                foreach (var g in data.gears)
                {
                    if (!string.IsNullOrEmpty(g.reserve) && !reserveGear) continue;
                    if (loadingLanesAtRuntime && string.IsNullOrEmpty(g.reserve) && InLoadingLane(g.c[0])) continue;
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
                        if (m.Key == "touchpad" || m.Key == "dome" || m.Key == "polyHopper") mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    }
                }
            }

            if (buildColliders && collidersJson != null) BuildColliders(JsonUtility.FromJson<ColliderFile>(collidersJson.text));
            BuildLogos(data);

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
                var mb = new MeshBuilder();
                if (p.pts0 != null && p.pts0.Length >= 6 && p.pts1 != null && p.pts1.Length == p.pts0.Length)
                    MeshGen.Loft(mb, MeshGen.Ring(p.pts0), p.z0, MeshGen.Ring(p.pts1), p.z1, true, true); // sloped (boiler roof, airship walls)
                else
                {
                    var ring = MeshGen.Ring(p.pts);
                    MeshGen.Loft(mb, ring, p.z0, ring, p.z1, true, true);
                }
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
            // each HOPPER pair's two DOORS are one part, hidden when the pair is dumped
            if (n.StartsWith("HOPPER container") && n.EndsWith(" door") && it.c != null)
            {
                var tg = TargetsFile.Load();
                int k = tg != null ? tg.HopperAt(it.c[0], it.c[1]) : -1;
                if (k >= 0) return $"HOPPER {k} doors";
            }
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
                var c = it.c != null && it.c.Length >= 3 ? it.c : it.p0; // the plate is a disc (cyl), the dome has a centre
                pivot = new Vector3(c[0], c[1], c[2]);
                return $"{n} #{touchpadCount++}";
            }
            return null;
        }

        // ---------------------------------------------------------------- FIRST STEAMWORKS logo
        const float LogoHeight = 30f; // in, on the 36.5 in tall airship base

        /// <summary>
        /// The FIRST STEAMWORKS logo (Resources/Textures/SteamworksLogo) on each AIRSHIP base, on the flat that faces
        /// the midline (the side without a lift).
        /// </summary>
        void BuildLogos(FieldFile data)
        {
            var tex = Resources.Load<Texture2D>("Textures/SteamworksLogo");
            if (tex == null || data.items == null) return;
            Material mat = null;
            Transform root = null;
            foreach (var it in data.items)
            {
                if (it.type != "loft" || it.name == null || !it.name.EndsWith("AIRSHIP base") || it.ring0 == null || it.ring0.Length < 6) continue;
                int n = it.ring0.Length / 2;
                float cx = 0f, cy = 0f;
                for (int k = 0; k < n; k++) { cx += it.ring0[2 * k]; cy += it.ring0[2 * k + 1]; }
                cx /= n; cy /= n;
                float toMid = -Mathf.Sign(cx);
                // the flat facing the midline: its two corners are the ones furthest toward X = 0
                float best = float.MinValue; int e = 0;
                for (int k = 0; k < n; k++)
                {
                    int k2 = (k + 1) % n;
                    float mx = (it.ring0[2 * k] + it.ring0[2 * k2]) / 2f * toMid;
                    if (mx > best) { best = mx; e = k; }
                }
                int e2 = (e + 1) % n;
                var p0 = new Vector2(it.ring0[2 * e], it.ring0[2 * e + 1]);
                var p1 = new Vector2(it.ring0[2 * e2], it.ring0[2 * e2 + 1]);
                var mid = (p0 + p1) / 2f;
                var outward = (mid - new Vector2(cx, cy)).normalized;
                var along = new Vector2(-outward.y, outward.x); // left to right, seen from outside
                float h = Mathf.Min(LogoHeight, (it.z1 - it.z0) - 4f), w = h * tex.width / tex.height;
                w = Mathf.Min(w, (p1 - p0).magnitude - 4f); h = w * tex.height / tex.width;
                float zc = (it.z0 + it.z1) / 2f;
                var c = mid + outward * 0.15f;

                if (mat == null)
                {
                    mat = new Material(Materials.LitShader) { name = "Field_logo" };
                    mat.SetTexture("_BaseMap", tex); mat.SetTexture("_MainTex", tex);
                    mat.SetColor("_BaseColor", Color.white);
                    mat.SetFloat("_Smoothness", 0.3f);
                    mat.SetFloat("_AlphaClip", 1f); mat.SetFloat("_Cutoff", 0.5f);
                    mat.EnableKeyword("_ALPHATEST_ON");
                    root = new GameObject("Logos").transform;
                    root.SetParent(GeneratedRoot, false);
                }
                var mesh = new Mesh { name = "Logo " + it.name };
                Vector3 V(float u, float v) => Units.ToUnity(new Vector3(c.x - along.x * (u - 0.5f) * w, c.y - along.y * (u - 0.5f) * w, zc + (v - 0.5f) * h));
                mesh.vertices = new[] { V(0f, 0f), V(1f, 0f), V(1f, 1f), V(0f, 1f) };
                mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
                var nrm = Units.ToUnity(new Vector3(outward.x, outward.y, 0f)).normalized;
                mesh.normals = new[] { nrm, nrm, nrm, nrm };
                // wound to face outward (Unity is left-handed: clockwise seen from the front)
                var tri = Vector3.Cross(mesh.vertices[1] - mesh.vertices[0], mesh.vertices[2] - mesh.vertices[0]);
                mesh.triangles = Vector3.Dot(tri, nrm) < 0f ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 0, 2, 1, 0, 3, 2 };
                mesh.RecalculateBounds();
                var go = new GameObject("Logo (" + it.name + ")");
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
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
