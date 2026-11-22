using System.Collections.Generic;
using SteamworksSim.Field;
using UnityEngine;

namespace SteamworksSim.Game
{
    /// <summary>
    /// HOPPERS: a robot pushing the panel in the guardrail between a pair of containers dumps both
    /// containers' FUEL (100 balls) over the guardrail onto the field. The staged balls (a separate mesh per
    /// pair, built by FieldBuilder) disappear and live PhysX FUEL pours out over about a second and a half.
    /// Each pair dumps once per match; Refill() restores them for the next match.
    /// </summary>
    public class HopperSystem : MonoBehaviour
    {
        const int BallsPerPair = 100;
        const float PourSeconds = 1.4f;
        const float TouchDistance = 2.0f;   // in between bumper and the panel face counts as pushing
        const float MinOverlap = 4f;        // in of bumper that has to be across the panel

        class Pair { public HopperSpec spec; public GameObject staged; public bool dumped; public int left; public float acc; }

        readonly List<Pair> pairs = new List<Pair>();
        FuelPool fuel;
        readonly System.Random rnd = new System.Random(17);

        public void Init(FieldBuilder field, TargetsFile targets, FuelPool pool)
        {
            fuel = pool;
            if (targets == null || targets.hoppers == null) return;
            var anim = field != null && field.GeneratedRoot != null ? field.GeneratedRoot.Find(FieldBuilder.AnimatedName) : null;
            for (int k = 0; k < targets.hoppers.Length; k++)
            {
                var t = anim != null ? anim.Find($"HOPPER {k} fuel") : null;
                pairs.Add(new Pair { spec = targets.hoppers[k], staged = t != null ? t.gameObject : null });
            }
        }

        /// <summary>Full again (start of a match, menus).</summary>
        public void Refill()
        {
            foreach (var p in pairs)
            {
                p.dumped = false; p.left = 0; p.acc = 0f;
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
            p.left = BallsPerPair; p.acc = 0f;
            if (p.staged != null) p.staged.SetActive(false);
        }

        void Pour(Pair p, float dt)
        {
            if (p.left <= 0 || fuel == null) return;
            p.acc += dt * BallsPerPair / PourSeconds;
            var h = p.spec;
            while (p.acc >= 1f && p.left > 0)
            {
                p.acc -= 1f; p.left--;
                // out of one of the two containers, over the guardrail, tumbling into the field
                var c = h.containers;
                bool first = (p.left & 1) == 0;
                float x0 = first ? c[0] : c[2], x1 = first ? c[1] : c[3];
                float x = Mathf.Lerp(x0 + 2.5f, x1 - 2.5f, (float)rnd.NextDouble());
                float y = h.side * (h.fieldHalfY - 4f - 3f * (float)rnd.NextDouble());
                float z = h.railH + 6f + 18f * (float)rnd.NextDouble();
                var v = new Vector3(((float)rnd.NextDouble() - 0.5f) * 30f, -h.side * (35f + 45f * (float)rnd.NextDouble()), 10f * (float)rnd.NextDouble());
                fuel.Spawn(new Vector3(x, y, z), v);
            }
        }
    }
}
