using System.Collections.Generic;
using SteamworksSim.Field;
using UnityEngine;

namespace SteamworksSim.Game
{
    /// <summary>
    /// The six ROPES, ported from the prototype (physics.js addRope / _rope): a verlet chain pinned at the
    /// davit, held near the TOUCHPAD cut-out, lying on the carpet, pushed aside by robots and the field, held
    /// at the climber by a climbing robot. Stowed (coiled under the davit) at match start, deployed by the
    /// PILOTS with 30 s left. Drawn with a LineRenderer. Works in inches like the prototype.
    /// </summary>
    public class RopeSystem : MonoBehaviour
    {
        const float G = 386.09f; // in/s^2

        public class Rope
        {
            public RopeSpec spec;
            public string alliance;   // whose TOUCHPAD it hangs through
            public int touchpad;      // index into that alliance's touchpads
            public int n, pinIndex;
            public float seg;
            public Vector3[] p, prev;
            public bool deployed = true;
            public LineRenderer line;
            public Vector3 Anchor => new Vector3(spec.anchor[0], spec.anchor[1], spec.anchor[2]);
        }

        /// <summary>A robot as the ropes see it (spec inches).</summary>
        public struct Body
        {
            public Vector2 center, fwd;   // fwd: unit heading
            public float halfL, halfW, lift, height;
            public Rope climbing;         // the rope this robot is climbing, if any
            public Vector2 climber;       // where it grips the rope
        }

        public readonly List<Rope> ropes = new List<Rope>();
        readonly List<Body> bodies = new List<Body>();
        readonly Collider[] hits = new Collider[16];
        SphereCollider probe;

        public void Init(RopesFile file, TargetsFile targets, Material material, int segments = 24)
        {
            if (file == null || file.ropes == null) return;
            var probeGo = new GameObject("Rope probe") { layer = 2 }; // Ignore Raycast
            probeGo.transform.SetParent(transform, false);
            probe = probeGo.AddComponent<SphereCollider>();
            probe.isTrigger = true;

            foreach (var s in file.ropes)
            {
                var R = new Rope { spec = s, n = segments + 1, seg = s.length / segments };
                R.p = new Vector3[R.n]; R.prev = new Vector3[R.n];
                R.pinIndex = Mathf.RoundToInt((s.anchor[2] - s.pin[2]) / R.seg);
                // which TOUCHPAD this rope hangs through
                R.alliance = null; R.touchpad = -1;
                foreach (var al in new[] { "red", "blue" })
                {
                    var tps = targets.Of(al).touchpads;
                    for (int i = 0; i < tps.Length; i++)
                        if (new Vector2(tps[i].c[0] - s.anchor[0], tps[i].c[1] - s.anchor[1]).magnitude < 1f) { R.alliance = al; R.touchpad = i; }
                }
                Hang(R);
                var go = new GameObject(s.name);
                go.transform.SetParent(transform, false);
                R.line = go.AddComponent<LineRenderer>();
                R.line.sharedMaterial = material;
                R.line.positionCount = R.n;
                R.line.widthMultiplier = 2f * s.radius * Units.Inch;
                R.line.numCapVertices = 2;
                R.line.numCornerVertices = 2;
                R.line.useWorldSpace = true;
                R.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                ropes.Add(R);
                Draw(R);
            }
        }

        // Hanging straight down, the excess lying on the carpet away from the airship
        static void Hang(Rope R)
        {
            var s = R.spec;
            for (int i = 0; i < R.n; i++)
            {
                float z = s.anchor[2] - i * R.seg, x = s.anchor[0], y = s.anchor[1];
                if (z < s.radius)
                {
                    float over = s.radius - z; z = s.radius;
                    x += over * (s.@out != null && s.@out.Length >= 2 ? s.@out[0] : 1f);
                    y += over * (s.@out != null && s.@out.Length >= 2 ? s.@out[1] : 0f);
                }
                R.p[i] = R.prev[i] = new Vector3(x, y, z);
            }
        }

        // Coiled just under the davit (stowed until the PILOTS deploy it)
        static void Coil(Rope R)
        {
            var a = R.Anchor;
            for (int i = 0; i < R.n; i++)
            {
                float ang = i * 1.45f, rad = i == 0 ? 0f : 2.4f;
                R.p[i] = R.prev[i] = new Vector3(a.x + Mathf.Cos(ang) * rad, a.y + Mathf.Sin(ang) * rad, a.z - 1.5f - Mathf.Min(i, 1) * 1.5f - i * 0.22f);
            }
        }

        public void StowAll() { foreach (var R in ropes) { R.deployed = false; Coil(R); Draw(R); } }

        public void DeployAll()
        {
            foreach (var R in ropes)
            {
                if (R.deployed) continue;
                Coil(R); // fall from the coil with zero velocity
                R.deployed = true;
            }
        }

        /// <summary>Straight back to hanging (menus, practice).</summary>
        public void HangAll() { foreach (var R in ropes) { R.deployed = true; Hang(R); Draw(R); } }

        public void SetBodies(IEnumerable<Body> list) { bodies.Clear(); bodies.AddRange(list); }

        /// <summary>The prototype's canClimb: a rope point just in front of the bumper, below the robot's top.</summary>
        public static bool InReach(Rope R, Body b, float reachForward, float reachLateral)
        {
            if (!R.deployed) return false;
            var left = new Vector2(-b.fwd.y, b.fwd.x);
            for (int i = 0; i < R.n; i++)
            {
                var q = R.p[i];
                if (q.z > b.lift + b.height) continue;
                var d = new Vector2(q.x, q.y) - b.center;
                float lx = Vector2.Dot(d, b.fwd), ly = Vector2.Dot(d, left);
                if (lx > b.halfL - 2f && lx < b.halfL + reachForward && Mathf.Abs(ly) < reachLateral) return true;
            }
            return false;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            foreach (var R in ropes) { if (R.deployed) Step(R, dt); }
        }

        void LateUpdate() { foreach (var R in ropes) Draw(R); }

        static void Draw(Rope R)
        {
            if (R.line == null) return;
            for (int i = 0; i < R.n; i++) R.line.SetPosition(i, Units.ToUnity(R.p[i]));
        }

        void Step(Rope R, float dt)
        {
            int n = R.n; var p = R.p; var prev = R.prev;
            float rr = R.spec.radius, seg = R.seg;
            // the prototype damped 0.996 per 1/240 s step
            float damp = Mathf.Pow(0.996f, dt * 240f), g = G * dt * dt;
            for (int i = 1; i < n; i++)
            {
                var v = (p[i] - prev[i]) * damp;
                prev[i] = p[i];
                p[i] += v; p[i].z -= g;
            }
            p[0] = R.Anchor;
            for (int iter = 0; iter < 10; iter++)
            {
                // a rope can go slack but not stretch
                for (int i = 0; i < n - 1; i++)
                {
                    var d = p[i + 1] - p[i];
                    float len = d.magnitude;
                    if (len <= seg || len < 1e-9f) continue;
                    float k = (len - seg) / len, wa = i == 0 ? 0f : 0.5f, wb = i == 0 ? 1f : 0.5f;
                    p[i] += d * (k * wa);
                    p[i + 1] -= d * (k * wb);
                }
                // TOUCHPAD cut-out keeps the rope near its centre
                int kp = R.pinIndex;
                if (kp > 0 && kp < n)
                {
                    var o = new Vector2(p[kp].x - R.spec.pin[0], p[kp].y - R.spec.pin[1]);
                    float od = o.magnitude;
                    if (od > R.spec.pinR) { o *= R.spec.pinR / od; p[kp].x = R.spec.pin[0] + o.x; p[kp].y = R.spec.pin[1] + o.y; }
                }
                // carpet, with friction
                for (int i = 1; i < n; i++)
                    if (p[i].z < rr)
                    {
                        p[i].z = rr;
                        prev[i].x = p[i].x - (p[i].x - prev[i].x) * 0.6f;
                        prev[i].y = p[i].y - (p[i].y - prev[i].y) * 0.6f;
                    }
                foreach (var b in bodies)
                {
                    if (b.climbing == R)
                    {
                        // a climbing robot holds its rope at the climber, at the robot's top
                        float hz = b.lift + b.height;
                        int kk = Mathf.Max(R.pinIndex + 1, Mathf.Min(n - 1, Mathf.RoundToInt((R.spec.anchor[2] - hz) / seg)));
                        p[kk] = new Vector3(b.climber.x, b.climber.y, Mathf.Max(hz, rr));
                        continue;
                    }
                    // robots push the rope aside (or drape it over the top)
                    var left = new Vector2(-b.fwd.y, b.fwd.x);
                    for (int i = 1; i < n; i++)
                    {
                        var d = new Vector2(p[i].x, p[i].y) - b.center;
                        float lx = Vector2.Dot(d, b.fwd), ly = Vector2.Dot(d, left);
                        float px = b.halfL + rr - Mathf.Abs(lx), py = b.halfW + rr - Mathf.Abs(ly);
                        float top = b.lift + b.height + rr - p[i].z, bottom = p[i].z - (b.lift - rr);
                        if (px <= 0f || py <= 0f || top <= 0f || bottom <= 0f) continue;
                        if (top <= px && top <= py) p[i].z += top;
                        else if (px <= py) { var push = b.fwd * (Mathf.Sign(lx) * px); p[i].x += push.x; p[i].y += push.y; }
                        else { var push = left * (Mathf.Sign(ly) * py); p[i].x += push.x; p[i].y += push.y; }
                    }
                }
            }
            // static field (airship base, barriers...): once per step
            if (probe != null)
            {
                float rm = rr * Units.Inch;
                probe.radius = rm;
                for (int i = 1; i < n; i++)
                {
                    var w = Units.ToUnity(p[i]);
                    int c = Physics.OverlapSphereNonAlloc(w, rm, hits, ~0, QueryTriggerInteraction.Ignore);
                    for (int h = 0; h < c; h++)
                    {
                        var col = hits[h];
                        if (col.attachedRigidbody != null) continue; // robots and fuel are handled above / ignored
                        if (Physics.ComputePenetration(probe, w, Quaternion.identity, col, col.transform.position, col.transform.rotation, out var dir, out var dist))
                            w += dir * dist;
                    }
                    if (c > 0) p[i] = Units.ToSpec(w);
                }
            }
        }
    }
}
