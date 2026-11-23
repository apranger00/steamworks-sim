using System.Collections.Generic;
using SteamworksSim.Field;
using UnityEngine;

namespace SteamworksSim.Game
{
    /// <summary>
    /// HOPPERS: a robot pushing the panel in the guardrail between a pair of containers opens both containers'
    /// DOORS (the face toward the field, above the floor). Until then each pair's 100 FUEL is a staged mesh; on
    /// the push the mesh is swapped for live PhysX balls at the same spots, the door colliders and panels go, and
    /// the FUEL spills out on its own, pushed by the balls behind it and by the floor's shallow slope toward the
    /// field. Some stays in the container, as on the real field.
    /// Each pair opens once per match; Refill() closes and restocks them for the next match.
    /// </summary>
    public class HopperSystem : MonoBehaviour
    {
        const float TouchDistance = 2.0f;   // in between bumper and the panel face counts as pushing
        const float MinOverlap = 4f;        // in of bumper that has to be across the panel

        class Pair
        {
            public HopperSpec spec; public GameObject staged, doors; public bool dumped;
            public readonly List<Collider> doorColliders = new List<Collider>();
            public readonly List<Vector3> balls = new List<Vector3>(); // spec frame, inches
        }

        readonly List<Pair> pairs = new List<Pair>();
        FuelPool fuel;

        public void Init(FieldBuilder field, TargetsFile targets, FuelPool pool)
        {
            fuel = pool;
            if (targets == null || targets.hoppers == null) return;
            var root = field != null ? field.GeneratedRoot : null;
            var anim = root != null ? root.Find(FieldBuilder.AnimatedName) : null;
            for (int k = 0; k < targets.hoppers.Length; k++)
            {
                var t = anim != null ? anim.Find($"HOPPER {k} fuel") : null;
                var d = anim != null ? anim.Find($"HOPPER {k} doors") : null;
                pairs.Add(new Pair { spec = targets.hoppers[k], staged = t != null ? t.gameObject : null, doors = d != null ? d.gameObject : null });
            }
            // the staged FUEL positions of each pair (the live balls start exactly there)
            var f = field != null && field.Data != null ? field.Data.fuel : null;
            if (f != null)
                for (int i = 0; i + 2 < f.Length; i += 3)
                {
                    int k = targets.HopperAt(f[i], f[i + 1]);
                    if (k >= 0) pairs[k].balls.Add(new Vector3(f[i], f[i + 1], f[i + 2]));
                }
            // the DOOR colliders ("HOPPER container (...) door"), by the pair they belong to
            var cols = root != null ? root.Find("Colliders") : null;
            if (cols != null)
                foreach (Transform c in cols)
                {
                    if (!c.name.Contains("HOPPER container") || !c.name.EndsWith(" door")) continue;
                    var col = c.GetComponent<Collider>();
                    if (col == null) continue;
                    var at = Units.ToSpec(col.bounds.center);
                    int k = targets.HopperAt(at.x, at.y);
                    if (k >= 0) pairs[k].doorColliders.Add(col);
                }
            int doorCount = 0; foreach (var p in pairs) doorCount += p.doorColliders.Count;
            if (doorCount == 0) Debug.LogWarning("[Steamworks] HOPPER doors not found: re-export the field data (node tools/export_unity_data.mjs).");
        }

        /// <summary>Closed and full again (start of a match, menus). Live FUEL is cleared by the FuelPool.</summary>
        public void Refill()
        {
            foreach (var p in pairs) SetOpen(p, false);
        }

        /// <summary>Checks every robot against every panel; opens any pushed pair.</summary>
        public void Step(float dt, IEnumerable<RobotMechanisms> robots, bool active)
        {
            if (active)
                foreach (var r in robots)
                {
                    var corners = r.Corners();
                    foreach (var p in pairs)
                        if (!p.dumped && Pushing(corners, p.spec)) Dump(p);
                }
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
            SetOpen(p, true);
            if (fuel == null) return;
            foreach (var b in p.balls) fuel.Spawn(b, Vector3.zero); // the same spots as the staged balls, at rest
        }

        static void SetOpen(Pair p, bool open)
        {
            p.dumped = open;
            if (p.staged != null) p.staged.SetActive(!open);
            if (p.doors != null) p.doors.SetActive(!open);
            foreach (var c in p.doorColliders) if (c != null) c.enabled = !open;
        }
    }
}
