using System;
using System.Collections.Generic;
using SteamworksSim.Field;
using UnityEngine;

namespace SteamworksSim.Game
{
    /// <summary>
    /// FUEL on PhysX: pooled spheres (5 in, 0.16 lb), the goal sensors from the prototype (high goal: ball
    /// centre falls through the opening; low goal: ball crosses the boiler face inside the opening) and a
    /// little rolling resistance on the carpet. When the pool is full the oldest ball is recycled.
    /// FUEL that ends up behind a boiler (out of the field: a long miss that fell behind the net line, or a
    /// ball resting on top of the boiler) rolls back out beside the low goal.
    /// </summary>
    public class FuelPool : MonoBehaviour
    {
        public const float RadiusIn = 2.5f;
        const float MassKg = 0.1625f * 0.45359237f;
        const float RollDecel = 14f * Units.Inch;      // m/s^2 on the carpet (prototype: 14 in/s^2)
        public int maxBalls = 1000; // 500 from the hoppers plus shots

        /// <summary>(alliance, goal) for a ball entering a goal; return true to take the ball off the field.</summary>
        public Func<string, Goal, bool> GoalEntered;
        /// <summary>(spec position, spec velocity) of a ball: return true if a robot takes it in.</summary>
        public Func<Vector3, Vector3, bool> Catch;
        public int Count => active.Count;

        class Ball
        {
            public GameObject go;
            public Rigidbody rb;
            public SphereCollider col;
            public Vector3 prev;          // spec position last tick
            public Collider ignored;      // the robot it just left (no collision for a moment)
            public float ignoreUntil;
            public float slow;            // seconds spent nearly still behind a boiler face
        }

        readonly List<Ball> active = new List<Ball>(), free = new List<Ball>();
        TargetsFile targets;
        Material material;
        Mesh sphere;
        PhysicsMaterial physics;
        Transform root;

        public void Init(TargetsFile t, Material mat)
        {
            targets = t; material = mat;
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            physics = new PhysicsMaterial("Fuel")
            {
                bounciness = 0.45f, dynamicFriction = 0.4f, staticFriction = 0.5f,
                bounceCombine = PhysicsMaterialCombine.Maximum, frictionCombine = PhysicsMaterialCombine.Average,
            };
            root = new GameObject("Fuel").transform;
        }

        Ball Make()
        {
            var go = new GameObject("Fuel ball");
            go.transform.SetParent(root, false);
            go.transform.localScale = Vector3.one * (2f * RadiusIn * Units.Inch);
            go.AddComponent<MeshFilter>().sharedMesh = sphere;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.5f; // unit sphere scaled to 5 in
            col.sharedMaterial = physics;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = MassKg;
            rb.linearDamping = 0f;
            rb.angularDamping = 0.8f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // shots move up to 12 m/s
            return new Ball { go = go, rb = rb, col = col };
        }

        /// <summary>Adds a ball at a spec position (in) with a spec velocity (in/s). It ignores `owner` briefly.</summary>
        public void Spawn(Vector3 specPos, Vector3 specVel, Collider owner = null, float ownerTime = 0.3f)
        {
            Ball b;
            if (free.Count > 0) { b = free[free.Count - 1]; free.RemoveAt(free.Count - 1); }
            else if (active.Count + free.Count < maxBalls) b = Make();
            else { b = active[0]; Release(b); active.RemoveAt(0); } // recycle the oldest ball on the field
            b.go.SetActive(true);
            var pos = Units.ToUnity(specPos);
            b.rb.position = pos; b.go.transform.position = pos;
            b.rb.linearVelocity = Units.ToUnity(specVel);
            b.rb.angularVelocity = Vector3.zero;
            b.prev = specPos;
            b.slow = 0f;
            if (owner != null)
            {
                Physics.IgnoreCollision(b.col, owner, true);
                b.ignored = owner; b.ignoreUntil = Time.time + ownerTime;
            }
            active.Add(b);
        }

        void Release(Ball b)
        {
            if (b.ignored != null) { Physics.IgnoreCollision(b.col, b.ignored, false); b.ignored = null; }
            b.go.SetActive(false);
        }

        public void Clear()
        {
            foreach (var b in active) { Release(b); free.Add(b); }
            active.Clear();
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime, now = Time.time;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var b = active[i];
                if (b.ignored != null && now >= b.ignoreUntil) { if (b.ignored) Physics.IgnoreCollision(b.col, b.ignored, false); b.ignored = null; }

                var p = b.rb.position;
                var cur = Units.ToSpec(p);
                if (targets != null && Sense(b.prev, cur, out var alliance, out var goal) && GoalEntered != null && GoalEntered(alliance, goal))
                {
                    Release(b); active.RemoveAt(i); free.Add(b);
                    continue;
                }
                b.prev = cur;

                if (targets != null && BehindBoiler(b, cur, dt, out var back)) { Return(b, back); continue; }

                // into a robot's hopper or intake (not while it is still leaving the robot that launched it)
                if (Catch != null && b.ignored == null && Catch(cur, b.rb.linearVelocity / Units.Inch))
                {
                    Release(b); active.RemoveAt(i); free.Add(b);
                    continue;
                }

                // rolling resistance on the carpet
                if (!b.rb.IsSleeping() && p.y <= RadiusIn * Units.Inch + 0.004f)
                {
                    var v = b.rb.linearVelocity;
                    var h = new Vector2(v.x, v.z);
                    float sp = h.magnitude;
                    if (sp > 1e-4f)
                    {
                        float ns = Mathf.Max(0f, sp - RollDecel * dt);
                        h *= ns / sp;
                        b.rb.linearVelocity = new Vector3(h.x, v.y, h.y);
                    }
                }
                // anything that escaped the field (or fell through it) is removed
                if (p.y < -1f || Mathf.Abs(cur.x) > 500f || Mathf.Abs(cur.y) > 250f) { Release(b); active.RemoveAt(i); free.Add(b); }
            }
        }

        // ---------------------------------------------------------------- boiler pocket
        /// <summary>
        /// Behind a boiler's field-facing face (outside the field), near the boiler, and either below the top of
        /// the boiler body (it fell off the back or sides) or caught still on the boiler for half a second
        /// (e.g. between the roof and the net). Balls rolling over the roof are left to roll back onto the field.
        /// </summary>
        bool BehindBoiler(Ball b, Vector3 p, float dt, out LowGoalSpec boiler)
        {
            foreach (var al in new[] { "red", "blue" })
            {
                var lg = targets.Of(al).lowGoal;
                float d = (p.x - lg.face[0]) * lg.n[0] + (p.y - lg.face[1]) * lg.n[1];
                float lat = (p.x - lg.face[0]) * lg.u[0] + (p.y - lg.face[1]) * lg.u[1];
                if (d > -RadiusIn || d < -90f || Mathf.Abs(lat) > 70f) continue;
                boiler = lg;
                if (p.z < 50f) return true;                       // dropped behind the boiler
                float speed = b.rb.linearVelocity.magnitude / Units.Inch;
                b.slow = speed < 10f ? b.slow + dt : 0f;
                return b.slow > 0.5f;                             // stuck on the boiler's top
            }
            boiler = null;
            return false;
        }

        /// <summary>Rolls the ball back onto the field beside the low goal, alternating sides.</summary>
        void Return(Ball b, LowGoalSpec lg)
        {
            returnSide = -returnSide;
            float lat = returnSide * (lg.halfW + 5f);
            var p = new Vector3(lg.face[0] + lg.n[0] * (RadiusIn + 3f) + lg.u[0] * lat, lg.face[1] + lg.n[1] * (RadiusIn + 3f) + lg.u[1] * lat, RadiusIn + 4f);
            var v = new Vector3(lg.n[0], lg.n[1], 0f) * 30f;
            b.rb.position = Units.ToUnity(p); b.go.transform.position = b.rb.position;
            b.rb.linearVelocity = Units.ToUnity(v); b.rb.angularVelocity = Vector3.zero;
            b.prev = p; b.slow = 0f;
        }
        int returnSide = 1;

        // The prototype's goal sensors (physics.js _sense), in the spec frame (inches)
        bool Sense(Vector3 o, Vector3 c, out string alliance, out Goal goal)
        {
            const float r = RadiusIn;
            foreach (var al in new[] { "red", "blue" })
            {
                var t = targets.Of(al);
                alliance = al;
                // high goal: the ball centre drops through the opening plane (z + r) within the capture radius
                var hg = t.highGoal;
                float plane = hg.z + r;
                if (o.z >= plane && c.z < plane)
                {
                    float k = (o.z - plane) / (o.z - c.z);
                    float hx = o.x + (c.x - o.x) * k, hy = o.y + (c.y - o.y) * k;
                    if (new Vector2(hx - hg.c[0], hy - hg.c[1]).magnitude <= hg.capture) { goal = Goal.High; return true; }
                }
                // low goal: the ball reaches the face plane moving inward, wholly inside the opening
                var lg = t.lowGoal;
                float d0 = (o.x - lg.face[0]) * lg.n[0] + (o.y - lg.face[1]) * lg.n[1];
                float d1 = (c.x - lg.face[0]) * lg.n[0] + (c.y - lg.face[1]) * lg.n[1];
                if (d0 >= r && d1 < r)
                {
                    float k = (d0 - r) / (d0 - d1);
                    float hx = o.x + (c.x - o.x) * k, hy = o.y + (c.y - o.y) * k, hz = o.z + (c.z - o.z) * k;
                    float lat = (hx - lg.face[0]) * lg.u[0] + (hy - lg.face[1]) * lg.u[1];
                    if (Mathf.Abs(lat) <= lg.halfW - r + 0.5f && hz >= lg.z0 + r - 0.5f && hz <= lg.z1 - r + 0.5f) { goal = Goal.Low; return true; }
                }
            }
            alliance = null; goal = Goal.High;
            return false;
        }
    }
}
