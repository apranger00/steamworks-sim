using System.Collections.Generic;
using SteamworksSim.Field;
using UnityEngine;

namespace SteamworksSim.Game
{
    /// <summary>
    /// HOPPERS: a robot pushing the panel in the guardrail between a pair of containers dumps both
    /// containers' FUEL (100 balls) over the guardrail onto the field. The staged balls (a separate mesh per
    /// pair, built by FieldBuilder) disappear and live PhysX FUEL rolls out: each container has a slanted floor lip
    /// sloping down over the guardrail, and balls appear at its top (only when the spot is clear) and roll off
    /// under gravity, so they drop just inside the field instead of being thrown.
    /// Each pair dumps once per match; Refill() restores them for the next match.
    /// </summary>
    public class HopperSystem : MonoBehaviour
    {
        const int BallsPerPair = 100;
        const float MaxPerSecond = 30f;     // per container; the real limit is how fast the lip clears
        const float TouchDistance = 2.0f;   // in between bumper and the panel face counts as pushing
        const float MinOverlap = 4f;        // in of bumper that has to be across the panel

        class Pair { public HopperSpec spec; public GameObject staged; public bool dumped; public int[] left = new int[2]; public float[] acc = new float[2]; }

        /// <summary>Spec frame: the slanted lip of one container, from its front at the floor to the guardrail.</summary>
        struct Lip { public float x0, x1; public Vector3 top, slope, normal; }

        readonly List<Pair> pairs = new List<Pair>();
        FuelPool fuel;
        readonly System.Random rnd = new System.Random(17);

        public void Init(FieldBuilder field, TargetsFile targets, FuelPool pool, FieldMaterials mats)
        {
            fuel = pool;
            alu = mats != null ? mats.Get("alu") : null;
            if (targets == null || targets.hoppers == null) return;
            var anim = field != null && field.GeneratedRoot != null ? field.GeneratedRoot.Find(FieldBuilder.AnimatedName) : null;
            for (int k = 0; k < targets.hoppers.Length; k++)
            {
                var t = anim != null ? anim.Find($"HOPPER {k} fuel") : null;
                pairs.Add(new Pair { spec = targets.hoppers[k], staged = t != null ? t.gameObject : null });
                for (int c = 0; c < 2; c++) BuildLip(targets.hoppers[k], c);
            }
        }

        Material alu;
        readonly Dictionary<(HopperSpec, int), Lip> lips = new Dictionary<(HopperSpec, int), Lip>();

        // A 0.5 in aluminium plate across the container's width, from its front edge at floor height down to just
        // above the guardrail (it never reaches into the field, so robots pass underneath it)
        void BuildLip(HopperSpec h, int c)
        {
            float x0 = h.containers[2 * c], x1 = h.containers[2 * c + 1];
            var top = new Vector3((x0 + x1) / 2f, h.side * Mathf.Abs(h.yIn), h.floorZ);
            var bottom = new Vector3(top.x, h.side * h.fieldHalfY, h.railH + 1.2f);
            var slope = (bottom - top).normalized;
            var normal = new Vector3(0f, -slope.z, slope.y);
            if (normal.z < 0f) normal = -normal;
            lips[(h, c)] = new Lip { x0 = x0, x1 = x1, top = top, slope = slope, normal = normal };

            float len = (bottom - top).magnitude, width = x1 - x0;
            var go = new GameObject($"HOPPER lip {x0:0}");
            go.transform.SetParent(transform, false);
            var centre = (top + bottom) / 2f - normal * 0.25f;
            go.transform.SetPositionAndRotation(Units.ToUnity(centre),
                Quaternion.LookRotation(Units.ToUnity(slope).normalized, Units.ToUnity(normal).normalized));
            go.transform.localScale = new Vector3(width, 0.5f, len) * Units.Inch;
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.AddComponent<MeshFilter>().sharedMesh = cube.GetComponent<MeshFilter>().sharedMesh;
            Destroy(cube);
            var mr = go.AddComponent<MeshRenderer>();
            if (alu != null) mr.sharedMaterial = alu;
            go.AddComponent<BoxCollider>(); // unit cube, scaled with the plate
            go.isStatic = true;
        }

        /// <summary>Full again (start of a match, menus).</summary>
        public void Refill()
        {
            foreach (var p in pairs)
            {
                p.dumped = false; p.left[0] = p.left[1] = 0; p.acc[0] = p.acc[1] = 0f;
                if (p.staged != null) p.staged.SetActive(true);
            }
        }

        /// <summary>Checks every robot against every panel; pours any dumping pair.</summary>
        public void Step(float dt, IEnumerable<RobotMechanisms> robots, bool active)
        {
            if (active)
                foreach (var r in robots)
                {
                    var corners = r.Corners();
                    foreach (var p in pairs)
                        if (!p.dumped && Pushing(corners, p.spec)) Dump(p);
                }
            foreach (var p in pairs) Pour(p, dt);
        }

        static bool Pushing(Vector2[] corners, HopperSpec h)
        {
            // the panel's field-side face; the bumper must be up against it across part of its width
            float face = h.side * (Mathf.Abs(h.panel[1]) - 0.25f);
            float minX = float.MaxValue, maxX = float.MinValue, reach = float.MinValue;
            foreach (var c in corners)
            {
                minX = Mathf.Min(minX, c.x); maxX = Mathf.Max(maxX, c.x);
                reach = Mathf.Max(reach, c.y * h.side); // how far toward the panel the robot reaches
            }
            if (reach < Mathf.Abs(face) - TouchDistance) return false;
            float overlap = Mathf.Min(maxX, h.panel[0] + h.panelHalfW) - Mathf.Max(minX, h.panel[0] - h.panelHalfW);
            return overlap >= MinOverlap;
        }

        void Dump(Pair p)
        {
            p.dumped = true;
            p.left[0] = p.left[1] = BallsPerPair / 2; p.acc[0] = p.acc[1] = 0f;
            if (p.staged != null) p.staged.SetActive(false);
        }

        void Pour(Pair p, float dt)
        {
            if (fuel == null) return;
            for (int c = 0; c < 2; c++)
            {
                if (p.left[c] <= 0) continue;
                p.acc[c] = Mathf.Min(p.acc[c] + dt * MaxPerSecond, 2f);
                if (p.acc[c] < 1f || !lips.TryGetValue((p.spec, c), out var lip)) continue;
                // a ball appears 2 in down the lip, resting on it, wherever across the container there is room
                const float r = FuelPool.RadiusIn;
                float x = Mathf.Lerp(lip.x0 + r + 0.5f, lip.x1 - r - 0.5f, (float)rnd.NextDouble());
                var pos = new Vector3(x, 0f, 0f) + new Vector3(0f, lip.top.y, lip.top.z) + lip.slope * 2f + lip.normal * (r + 0.05f);
                if (Physics.CheckSphere(Units.ToUnity(pos), r * 0.9f * Units.Inch, ~0, QueryTriggerInteraction.Ignore)) continue;
                p.acc[c] -= 1f; p.left[c]--;
                fuel.Spawn(pos, lip.slope * 8f); // a nudge down the slope; gravity does the rest
            }
        }
    }
}
