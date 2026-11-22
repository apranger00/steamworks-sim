using System;
using SteamworksSim.Field;
using SteamworksSim.Robots;
using UnityEngine;

namespace SteamworksSim.Game
{
    /// <summary>
    /// A robot's scoring mechanisms, as its robot.json describes them (prototype mechanisms.js and physics.js):
    /// turret shooter that aims at the alliance's high goal, low-goal dump out of the front, gear slot onto a
    /// lift peg, rope climber. Infinite FUEL and GEARS for testing (no intake yet).
    /// </summary>
    public class RobotMechanisms : MonoBehaviour
    {
        public RobotDefinition def;
        public string alliance;
        public int id;
        public string displayName;

        /// <summary>Set by the match: robots only work during AUTO and TELEOP.</summary>
        public bool enabledByMatch;
        public bool shootHeld, dumpHeld;

        public enum ClimbPhase { None, Attach, Up, Hold, Down }
        public ClimbPhase Climb { get; private set; }
        public RopeSystem.Rope ClimbRope { get; private set; }
        public float Lift { get; private set; }   // in, underside above the carpet

        public string ShooterStatus { get; private set; } = "-";
        public string GearStatus { get; private set; } = "-";
        public string ClimbStatus { get; private set; } = "-";
        public event Action<string> Toast;

        FuelPool fuel;
        RopeSystem ropes;
        MatchRules rules;
        TargetsFile targets;
        Rigidbody rb;
        TankDrive drive;
        Collider body;
        Transform turret, heldGear;
        float shootT, dumpT, gearCooldown, climbTopZ;
        readonly System.Random rnd = new System.Random(2017);

        const float In = Units.Inch;
        float HalfL => def.LengthWithBumpers / 2f;
        float HalfW => def.WidthWithBumpers / 2f;
        float Height => def.frame.height;
        static float Gravity => Mathf.Max(0.1f, -Physics.gravity.y) / In; // in/s^2

        public void Init(RobotDefinition d, string al, int robotId, string name, FuelPool f, RopeSystem r, Func<MatchRules> rulesGetter, TargetsFile t)
        {
            def = d; alliance = al; id = robotId; displayName = name;
            fuel = f; ropes = r; targets = t; getRules = rulesGetter;
            rb = GetComponent<Rigidbody>();
            drive = GetComponent<TankDrive>();
            body = GetComponent<Collider>();
            BuildVisuals();
        }
        Func<MatchRules> getRules;

        // ---------------------------------------------------------------- pose helpers (spec frame)
        public Vector2 Center { get { var s = Units.ToSpec(transform.position); return new Vector2(s.x, s.y); } }
        public Vector2 Forward { get { var f = transform.forward; return new Vector2(f.x, f.z).normalized; } }
        Vector2 Left { get { var f = Forward; return new Vector2(-f.y, f.x); } }
        Vector2 SpecVelocity { get { var v = rb != null ? rb.linearVelocity : Vector3.zero; return new Vector2(v.x, v.z) / In; } }
        public float TopZ => Lift + Height;

        public Vector2[] Corners()
        {
            Vector2 c = Center, f = Forward * HalfL, l = Left * HalfW;
            return new[] { c - f - l, c + f - l, c + f + l, c - f + l };
        }

        public RopeSystem.Body AsBody() => new RopeSystem.Body
        {
            center = Center, fwd = Forward, halfL = HalfL, halfW = HalfW, lift = Lift, height = Height,
            climbing = Climb != ClimbPhase.None ? ClimbRope : null, climber = ClimberPoint(),
        };

        Vector2 ClimberPoint() => Center + Forward * (HalfL - (def.climber != null ? def.climber.setback : 3f));

        /// <summary>For the rules: which of its own alliance's touchpads this robot's rope goes through.</summary>
        public int ClimbTouchpad => Climb != ClimbPhase.None && ClimbRope != null && ClimbRope.alliance == alliance ? ClimbRope.touchpad : -1;

        // ---------------------------------------------------------------- visuals
        void BuildVisuals()
        {
            var visual = transform.Find("Visual");
            if (visual == null) return;
            var mr = visual.GetComponentInChildren<MeshRenderer>();
            var dark = mr != null ? mr.sharedMaterial : null;
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var cubeMesh = cube.GetComponent<MeshFilter>().sharedMesh;
            Destroy(cube);

            // A hood on the turret that turns to face the goal
            if (def.shooter != null && def.shooter.enabled)
            {
                var off = def.shooter.exitOffset != null && def.shooter.exitOffset.Length >= 2 ? def.shooter.exitOffset : new[] { 0f, 0f };
                turret = new GameObject("Turret aim").transform;
                turret.SetParent(visual, false);
                turret.localPosition = new Vector3(-off[1] * In, Height * In, off[0] * In);
                var hood = new GameObject("Hood");
                hood.transform.SetParent(turret, false);
                hood.transform.localPosition = new Vector3(0f, 2f * In, 4f * In);
                hood.transform.localRotation = Quaternion.Euler(-35f, 0f, 0f);
                hood.transform.localScale = new Vector3(6f * In, 2.5f * In, 9f * In);
                hood.AddComponent<MeshFilter>().sharedMesh = cubeMesh;
                var hm = transform.Find("Visual/Shooter turret");
                hood.AddComponent<MeshRenderer>().sharedMaterial = hm != null ? hm.GetComponent<MeshRenderer>().sharedMaterial : dark;
            }
            // The GEAR it carries, standing in the front slot
            if (def.gear != null && def.gear.enabled)
            {
                heldGear = new GameObject("Held GEAR").transform;
                heldGear.SetParent(visual, false);
                heldGear.localPosition = new Vector3(-def.gear.slotOffset * In, 13f * In, (HalfL - 3f) * In);
                heldGear.localRotation = Quaternion.FromToRotation(Vector3.up, Vector3.forward);
                heldGear.gameObject.AddComponent<MeshFilter>().sharedMesh = FieldAnimator.GearMesh();
                var fm = transform.Find("Visual/Front marker");
                heldGear.gameObject.AddComponent<MeshRenderer>().sharedMaterial = fm != null ? fm.GetComponent<MeshRenderer>().sharedMaterial : dark;
            }
        }

        // ---------------------------------------------------------------- reset
        public void ResetState()
        {
            Climb = ClimbPhase.None; ClimbRope = null; Lift = 0f;
            shootT = dumpT = 0f; gearCooldown = 0f;
            if (rb != null) { rb.isKinematic = false; }
            if (drive != null) drive.enabled = true;
            var p = transform.position; p.y = 0f; transform.position = p;
            if (rb != null) rb.position = p;
        }

        // ---------------------------------------------------------------- per tick
        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            rules = getRules != null ? getRules() : null;
            if (gearCooldown > 0f) gearCooldown -= dt;
            if (heldGear != null) heldGear.gameObject.SetActive(gearCooldown <= 0f && Climb == ClimbPhase.None);

            UpdateShooterStatus(out var shot);
            if (Climb != ClimbPhase.None) { ClimbStep(dt); UpdateClimbStatus(); return; }
            UpdateGearStatus();
            UpdateClimbStatus();

            bool on = enabledByMatch;
            Fire(ref shootT, on && shootHeld && shot != null, def.shooter != null ? def.shooter.rate : 0f, dt, () => FireHigh());
            Fire(ref dumpT, on && dumpHeld && def.lowGoalDump != null && def.lowGoalDump.enabled, def.lowGoalDump != null ? def.lowGoalDump.rate : 0f, dt, () => { DumpLow(); return true; });
        }

        static void Fire(ref float timer, bool held, float rate, float dt, Func<bool> shoot)
        {
            if (!held || rate <= 0f) { timer = Mathf.Min(timer + dt, 0f); return; }
            timer += dt;
            int guard = 0;
            while (timer >= 0f && guard++ < 8)
            {
                timer -= 1f / rate;
                if (!shoot()) { timer = 0f; break; }
            }
        }

        // ---------------------------------------------------------------- shooter (mechanisms.js)
        struct Shot { public Vector3 p0, v; public float range, speed, pitch, yaw; }

        Vector3 ExitPoint()
        {
            var off = def.shooter.exitOffset != null && def.shooter.exitOffset.Length >= 2 ? def.shooter.exitOffset : new[] { 0f, 0f };
            var c = Center + Forward * off[0] + Left * off[1];
            return new Vector3(c.x, c.y, def.shooter.exitHeight + Lift);
        }

        /// <summary>Velocity that carries a ball from p0 through target, arriving descending at entryDeg.</summary>
        static bool Solve(Vector3 p0, Vector3 target, float entryDeg, out Shot s)
        {
            s = default;
            float dx = target.x - p0.x, dy = target.y - p0.y, H = target.z - p0.z;
            float D = Mathf.Sqrt(dx * dx + dy * dy);
            float k = H + D * Mathf.Tan(entryDeg * Mathf.Deg2Rad);
            if (D < 1e-6f || k <= 0f) return false;
            float g = Gravity, T = Mathf.Sqrt(2f * k / g);
            float vh = D / T, vz = (H + 0.5f * g * T * T) / T;
            s = new Shot { p0 = p0, v = new Vector3(dx / D * vh, dy / D * vh, vz), range = D, speed = Mathf.Sqrt(vh * vh + vz * vz), pitch = Mathf.Atan2(vz, vh), yaw = Mathf.Atan2(dy, dx) };
            return true;
        }

        Shot? Aim(out string reason)
        {
            reason = "-";
            var S = def.shooter;
            if (S == null || !S.enabled) { reason = "No shooter"; return null; }
            var hg = targets.Of(alliance).highGoal;
            if (!Solve(ExitPoint(), new Vector3(hg.c[0], hg.c[1], hg.z + FuelPool.RadiusIn), S.entryAngle, out var s)) { reason = "No shot"; return null; }
            if (s.range < S.minRange * 12f) { reason = $"Too close ({s.range / 12f:0.0} ft)"; return null; }
            if (s.range > S.maxRange * 12f || s.speed > S.maxExitSpeed * 12f) { reason = $"Out of range ({s.range / 12f:0.0} ft)"; return null; }
            reason = $"In range {s.range / 12f:0.0} ft";
            return s;
        }

        void UpdateShooterStatus(out Shot? shot)
        {
            shot = Aim(out var reason);
            ShooterStatus = reason;
            if (turret == null) return;
            // point the hood at the goal (yaw only)
            var hg = targets.Of(alliance).highGoal;
            var d = new Vector2(hg.c[0], hg.c[1]) - Center;
            float worldYaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg; // Unity yaw: spec (x, y) -> (x, z)
            float local = Mathf.DeltaAngle(transform.eulerAngles.y, worldYaw);
            turret.localRotation = Quaternion.Slerp(turret.localRotation, Quaternion.Euler(0f, local, 0f), 0.25f);
        }

        float Gauss() => ((float)rnd.NextDouble() + (float)rnd.NextDouble() + (float)rnd.NextDouble() - 1.5f) * 2f; // ~N(0,1) within +-3

        bool FireHigh()
        {
            var a = Aim(out _);
            if (a == null) return false;
            var S = def.shooter; var s = a.Value;
            float yaw = s.yaw + Gauss() * S.aimError * Mathf.Deg2Rad;
            float pitch = s.pitch + Gauss() * S.aimError * Mathf.Deg2Rad;
            float sp = s.speed * (1f + Gauss() * S.speedError / 100f);
            float vh = sp * Mathf.Cos(pitch);
            var rv = SpecVelocity; // shooting on the move is not compensated: a driver skill
            fuel.Spawn(s.p0, new Vector3(vh * Mathf.Cos(yaw) + rv.x, vh * Mathf.Sin(yaw) + rv.y, sp * Mathf.Sin(pitch)), body);
            return true;
        }

        void DumpLow()
        {
            var D = def.lowGoalDump;
            Vector2 f = Forward, l = Left, c = Center;
            float fwd = HalfL - 2.6f, lat = ((float)rnd.NextDouble() - 0.5f) * D.width;
            var p = c + f * fwd + l * lat;
            var rv = SpecVelocity;
            fuel.Spawn(new Vector3(p.x, p.y, D.height + Lift), new Vector3(f.x * D.speed * 12f + rv.x, f.y * D.speed * 12f + rv.y, 0f), body);
        }

        // ---------------------------------------------------------------- gear slot
        int GearAlign(out bool ok, out float dist, out float lat, out float angle)
        {
            ok = false; dist = lat = angle = 0f;
            var G = def.gear;
            if (G == null || !G.enabled) return -1;
            Vector2 f = Forward;
            var front = Center + f * HalfL + Left * G.slotOffset;
            var lifts = targets.Of(alliance).lifts;
            int best = -1; float bestScore = float.MaxValue;
            for (int i = 0; i < lifts.Length; i++)
            {
                var L = lifts[i];
                var u = new Vector2(L.u[0], L.u[1]);
                float cosA = -Vector2.Dot(f, u);
                float ang = Mathf.Acos(Mathf.Clamp(cosA, -1f, 1f)) * Mathf.Rad2Deg;
                var rel = front - new Vector2(L.tip[0], L.tip[1]);
                float d = Vector2.Dot(rel, u);
                float lt = Mathf.Abs(-rel.x * u.y + rel.y * u.x);
                float score = Mathf.Abs(d) + lt + ang / 5f;
                if (score < bestScore)
                {
                    bestScore = score; best = i;
                    ok = ang <= G.maxAngle && lt <= G.maxLateral && d <= G.maxDistance && d >= -2f;
                    dist = d; lat = lt; angle = ang;
                }
            }
            return best;
        }

        void UpdateGearStatus()
        {
            if (def.gear == null || !def.gear.enabled) { GearStatus = "No gear slot"; return; }
            int i = GearAlign(out bool ok, out _, out _, out _);
            GearStatus = ok ? $"Lined up: {targets.Of(alliance).lifts[i].name} lift" : "-";
        }

        /// <summary>Place GEAR (E / A): onto the lift peg the front slot is lined up with.</summary>
        public void PlaceGear()
        {
            if (!enabledByMatch || Climb != ClimbPhase.None || def.gear == null || !def.gear.enabled || gearCooldown > 0f) return;
            int i = GearAlign(out bool ok, out float dist, out float lat, out float angle);
            if (i < 0 || !ok) { Toast?.Invoke("Not lined up with a peg: drive the front slot onto it"); return; }
            if (rules == null) return;
            if (rules.DeliverGear(alliance, i, out var msg)) gearCooldown = 0.6f;
            Toast?.Invoke(msg);
        }

        // ---------------------------------------------------------------- climber (physics.js _climbStep)
        void UpdateClimbStatus()
        {
            if (def.climber == null || !def.climber.enabled) { ClimbStatus = "No climber"; return; }
            switch (Climb)
            {
                case ClimbPhase.Attach: ClimbStatus = "Latching..."; return;
                case ClimbPhase.Up: ClimbStatus = $"Climbing {TopZ:0} in"; return;
                case ClimbPhase.Hold: ClimbStatus = "Pressing TOUCHPAD"; return;
                case ClimbPhase.Down: ClimbStatus = "Lowering"; return;
            }
            ClimbStatus = FindRope() != null ? "Rope in reach: press Climb" : AnyRopeDeployed() ? "-" : "Ropes drop at 0:30";
        }

        bool AnyRopeDeployed() { if (ropes == null) return false; foreach (var R in ropes.ropes) if (R.deployed) return true; return false; }

        RopeSystem.Rope FindRope()
        {
            if (ropes == null || def.climber == null || !def.climber.enabled) return null;
            var b = AsBody();
            foreach (var R in ropes.ropes)
                if (R.alliance == alliance && RopeSystem.InReach(R, b, def.climber.reachForward, def.climber.reachLateral)) return R;
            return null;
        }

        /// <summary>Climb (T / X): latch onto an own rope in front of the bumper, or let go.</summary>
        public void ToggleClimb()
        {
            if (!enabledByMatch || def.climber == null || !def.climber.enabled) return;
            if (Climb != ClimbPhase.None) { if (Climb != ClimbPhase.Down) { Climb = ClimbPhase.Down; Toast?.Invoke("Letting go of the rope"); } return; }
            var R = FindRope();
            if (R == null) { Toast?.Invoke(AnyRopeDeployed() ? "Drive your front bumper into one of your ropes first" : "ROPES drop with 30 seconds left"); return; }
            ClimbRope = R;
            climbTopZ = targets.Of(alliance).touchpads[R.touchpad].z + 1f;
            Climb = ClimbPhase.Attach;
            drive.enabled = false;
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            Toast?.Invoke($"Latched: climbing to TOUCHPAD {R.touchpad + 1}");
        }

        void ClimbStep(float dt)
        {
            var c = Center;
            float rate = def.climber.rate;
            if (Climb == ClimbPhase.Attach)
            {
                var a = new Vector2(ClimbRope.spec.anchor[0], ClimbRope.spec.anchor[1]);
                var d = a - ClimberPoint();
                float step = 30f * dt;
                if (d.magnitude <= step) { c += d; Climb = ClimbPhase.Up; }
                else c += d.normalized * step;
            }
            else if (Climb == ClimbPhase.Up)
            {
                float target = climbTopZ - Height;
                Lift = Mathf.Min(target, Lift + rate * dt);
                if (Lift >= target) Climb = ClimbPhase.Hold;
            }
            else if (Climb == ClimbPhase.Down)
            {
                Lift = Mathf.Max(0f, Lift - rate * 1.5f * dt);
            }
            rb.MovePosition(Units.ToUnity(c.x, c.y, Lift));
            if (Climb == ClimbPhase.Down && Lift <= 0f)
            {
                Climb = ClimbPhase.None; ClimbRope = null;
                rb.isKinematic = false;
                rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                drive.enabled = true;
            }
        }
    }
}
