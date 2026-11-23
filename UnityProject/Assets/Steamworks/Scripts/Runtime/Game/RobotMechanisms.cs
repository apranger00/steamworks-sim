using System;
using SteamworksSim.Field;
using SteamworksSim.Robots;
using UnityEngine;

namespace SteamworksSim.Game
{
    /// <summary>
    /// A robot's scoring mechanisms, as its robot.json describes them (prototype mechanisms.js and physics.js):
    /// turret shooter that aims at the alliance's high goal, low-goal dump out of the front, gear slot onto a
    /// lift peg, rope climber. It carries what robot.json allows (shooter.capacity FUEL, gear.capacity GEARS; 0 =
    /// unlimited for testing), starts with its preload, and takes pieces in as its intake allows: FUEL falling
    /// into its open-top hopper from a chute, a GEAR sliding out of a chute into the front slot, and (if enabled)
    /// pieces off the floor.
    /// </summary>
    public class RobotMechanisms : MonoBehaviour
    {
        public RobotDefinition def;
        public string alliance;
        public int id;
        public string displayName;

        /// <summary>Set by the match: robots only work during AUTO and TELEOP.</summary>
        public bool enabledByMatch;
        /// <summary>Takes part in the match (preloads from its alliance's stock). False for the parked defender.</summary>
        public bool participates = true;

        public int Fuel { get; private set; }
        public int Gears { get; private set; }
        public int FuelCapacity => def.shooter != null ? def.shooter.capacity : 0;   // 0 = unlimited
        public int GearCapacity => def.gear != null ? def.gear.capacity : 0;
        bool HasFuel => FuelCapacity <= 0 || Fuel > 0;
        bool HasGear => GearCapacity <= 0 || Gears > 0;
        public int PreloadedFuel => def.preload == null ? 0 : (FuelCapacity > 0 ? Mathf.Min(def.preload.fuel, FuelCapacity) : def.preload.fuel);
        public int PreloadedGears => def.preload == null ? 0 : (GearCapacity > 0 ? Mathf.Min(def.preload.gears, GearCapacity) : def.preload.gears);
        public string LoadStatus => $"{(FuelCapacity > 0 ? $"{Fuel} / {FuelCapacity}" : "unlimited")} FUEL, {(GearCapacity > 0 ? $"{Gears} / {GearCapacity}" : "unlimited")} GEAR";
        public bool shootHeld, dumpHeld, intakeHeld;

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
        Collider[] body; // every collider of the robot (an open-hopper robot has several)
        Transform turret, heldGear;
        GameObject[] hopperBalls;
        int shownFuel = -1;
        Collider fullCap; // closes a full hopper with a shallow dome, so more FUEL rolls off over the sides
        float shootT, dumpT, gearCooldown, climbTopZ;
        RigidbodyConstraints savedConstraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
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
            BuildVisuals();
            // after the visuals: includes the full-hopper dome, so shots and dumps leaving a full robot don't hit it
            body = GetComponentsInChildren<Collider>(true);
        }
        Func<MatchRules> getRules;

        // ---------------------------------------------------------------- pose helpers (spec frame)
        // physics pose (not the interpolated transform), so per-tick logic never lags a step behind
        Vector3 Pose => rb != null ? rb.position : transform.position;
        public Vector2 Center { get { var s = Units.ToSpec(Pose); return new Vector2(s.x, s.y); } }
        public Vector2 Forward { get { var f = (rb != null ? rb.rotation : transform.rotation) * Vector3.forward; return new Vector2(f.x, f.z).normalized; } }
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
            // FUEL in the open-top hopper, so you can see how full the robot is. The balls lie loose, as a real pile
            // does: each one settles into the lowest gap it can find on top of the ones already there.
            if (def.intake != null && def.intake.fuelFromChute && fuel != null)
            {
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var sphereMesh = sphere.GetComponent<MeshFilter>().sharedMesh;
                Destroy(sphere);
                var holder = new GameObject("Hopper FUEL").transform;
                holder.SetParent(visual, false);
                float hl = RobotFactory.HopperLength(def), hw = RobotFactory.HopperWidth(def), floor = RobotFactory.HopperFloor(def);
                var pile = RobotFactory.HopperPile(def);
                hopperBalls = new GameObject[pile.Count];
                for (int n = 0; n < pile.Count; n++)
                {
                    var b = new GameObject("FUEL");
                    b.transform.SetParent(holder, false);
                    b.transform.localPosition = new Vector3(-pile[n].y * In, (floor + pile[n].z) * In, pile[n].x * In); // (fwd, left, up) -> local
                    b.transform.localScale = Vector3.one * (2f * FuelPool.RadiusIn * In);
                    b.AddComponent<MeshFilter>().sharedMesh = sphereMesh;
                    b.AddComponent<MeshRenderer>().sharedMaterial = fuel.BallMaterial;
                    b.SetActive(false);
                    hopperBalls[n] = b;
                }
                // the dome over a full hopper (a collider only, switched on while full)
                var cap = new GameObject("Collider (full hopper)");
                cap.transform.SetParent(transform, false);
                var mc = cap.AddComponent<MeshCollider>();
                mc.sharedMesh = Dome(hl, hw, Height);
                mc.convex = true;
                mc.enabled = false;
                fullCap = mc;
                // a lid only GEARS collide with: a GEAR landing on the robot slides off instead of dropping in
                PhysicsLayers.Setup();
                var lid = new GameObject("Collider (gear lid)") { layer = PhysicsLayers.GearLid };
                lid.transform.SetParent(transform, false);
                var lc = lid.AddComponent<MeshCollider>();
                lc.sharedMesh = Dome(hl, hw, Height);
                lc.convex = true;
                lc.sharedMaterial = new PhysicsMaterial("Gear lid")
                {
                    bounciness = 0.3f, dynamicFriction = 0f, staticFriction = 0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Maximum,
                };
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
        /// <summary>Start of a match: just the preload on board.</summary>
        public void Reload() { Fuel = PreloadedFuel; Gears = PreloadedGears; overflow = 0; }

        /// <summary>Off the rope and back on the carpet (the load stays as it is).</summary>
        public void ResetState()
        {
            Climb = ClimbPhase.None; ClimbRope = null; Lift = 0f;
            shootT = dumpT = 0f; gearCooldown = 0f;
            if (rb != null) { rb.isKinematic = false; if (Climb == ClimbPhase.None) rb.constraints = savedConstraints; }
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
            if (heldGear != null) heldGear.gameObject.SetActive(gearCooldown <= 0f && Climb == ClimbPhase.None && HasGear);
            ShowHopperFuel();
            Overflow();

            UpdateShooterStatus(out var shot);
            if (Climb != ClimbPhase.None) { ClimbStep(dt); UpdateClimbStatus(); return; }
            UpdateGearStatus();
            UpdateClimbStatus();

            bool on = enabledByMatch;
            Fire(ref shootT, on && shootHeld && shot != null && HasFuel, def.shooter != null ? def.shooter.rate : 0f, dt, () => HasFuel && FireHigh());
            Fire(ref dumpT, on && dumpHeld && def.lowGoalDump != null && def.lowGoalDump.enabled && HasFuel, def.lowGoalDump != null ? def.lowGoalDump.rate : 0f, dt, () => { if (!HasFuel) return false; DumpLow(); return true; });
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
            if (FuelCapacity > 0) Fuel--;
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
            if (FuelCapacity > 0) Fuel--;
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
            if (!enabledByMatch || Climb != ClimbPhase.None || def.gear == null || !def.gear.enabled || gearCooldown > 0f || !HasGear) return;
            int i = GearAlign(out bool ok, out float dist, out float lat, out float angle);
            if (i < 0 || !ok) { Toast?.Invoke("Not lined up with a peg: drive the front slot onto it"); return; }
            if (rules == null) return;
            if (rules.DeliverGear(alliance, i, out var msg)) { gearCooldown = 0.6f; if (GearCapacity > 0) Gears--; }
            Toast?.Invoke(msg);
        }

        /// <summary>A shallow dome (robot-local, Unity axes) closing the hopper at the rim: 2.5 in high in the middle.</summary>
        static Mesh Dome(float len, float wid, float height)
        {
            float L = len / 2f * In, W = wid / 2f * In, h = height * In;
            var m = new Mesh { name = "Full hopper dome" };
            m.vertices = new[]
            {
                new Vector3(-W, h - 0.5f * In, -L), new Vector3(W, h - 0.5f * In, -L), new Vector3(W, h - 0.5f * In, L), new Vector3(-W, h - 0.5f * In, L),
                new Vector3(0f, h + 2.5f * In, 0f),
            };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 4, 1, 1, 4, 2, 2, 4, 3, 3, 4, 0 };
            m.RecalculateBounds();
            return m;
        }

        void ShowHopperFuel()
        {
            if (fullCap != null)
            {
                bool full = FuelCapacity > 0 && Fuel >= FuelCapacity && Climb == ClimbPhase.None;
                if (fullCap.enabled != full) fullCap.enabled = full;
            }
            if (hopperBalls == null) return;
            int slots = hopperBalls.Length, n;
            if (FuelCapacity <= 0) n = slots;                                         // unlimited (testing): shown full
            else if (FuelCapacity <= slots) n = Fuel;                                 // one ball per FUEL carried
            else n = Mathf.CeilToInt(slots * Fuel / (float)FuelCapacity);             // big hoppers: scaled
            n = Mathf.Clamp(n, 0, slots);
            if (n == shownFuel) return;
            shownFuel = n;
            for (int i = 0; i < hopperBalls.Length; i++) hopperBalls[i].SetActive(i < n);
        }

        // ---------------------------------------------------------------- intake
        /// <summary>A FUEL at spec position p moving at v (in/s): true if this robot takes it.</summary>
        public bool TryCatchFuel(Vector3 p, Vector3 v)
        {
            if (def.intake == null || Climb != ClimbPhase.None) return false;
            bool full = FuelCapacity > 0 && Fuel >= FuelCapacity;
            var d = new Vector2(p.x, p.y) - Center;
            float lx = Vector2.Dot(d, Forward), ly = Vector2.Dot(d, Left);
            bool caught = false;
            // open-top hopper: a ball that has dropped in over the rim (or rests inside it)
            float hl = RobotFactory.HopperLength(def) / 2f, hw = RobotFactory.HopperWidth(def) / 2f;
            if (!full && def.intake.fuelFromChute && v.z <= 1f && Mathf.Abs(lx) <= hl && Mathf.Abs(ly) <= hw
                && p.z >= Lift + RobotFactory.HopperFloor(def) && p.z <= TopZ + 1f) caught = true;
            // floor intake across the front bumper
            if (!caught && def.intake.fuelFromFloor && enabledByMatch && intakeHeld && p.z <= FuelPool.RadiusIn + 2f
                && lx >= HalfL - 1f && lx <= HalfL + FuelPool.RadiusIn + 2f && Mathf.Abs(ly) <= HalfW - 3f) caught = true;
            // the floor intake keeps feeding a full hopper: each extra ball forces one out over the top
            if (caught && full) overflow++;
            else if (caught && FuelCapacity > 0) Fuel++;
            return caught;
        }

        int overflow; // balls to push out over the top of a full hopper (spawned next physics step, not mid-loop)

        /// <summary>One ball spills over the left, right or back edge of the full hopper (never the intake side).</summary>
        void Overflow()
        {
            if (overflow <= 0 || fuel == null) return;
            overflow--;
            float hl = RobotFactory.HopperLength(def) / 2f, hw = RobotFactory.HopperWidth(def) / 2f;
            const float inset = FuelPool.RadiusIn + 0.5f;
            float u = (float)rnd.NextDouble() * 2f - 1f;
            int edge = rnd.Next(3);
            Vector2 local, outward; // (forward, left)
            if (edge == 0) { local = new Vector2(u * (hl - inset), hw - inset); outward = new Vector2(0f, 1f); }
            else if (edge == 1) { local = new Vector2(u * (hl - inset), -(hw - inset)); outward = new Vector2(0f, -1f); }
            else { local = new Vector2(-(hl - inset), u * (hw - inset)); outward = new Vector2(-1f, 0f); }
            Vector2 f = Forward, l = Left, c = Center;
            var at = c + f * local.x + l * local.y;
            var dir = f * outward.x + l * outward.y;
            var rv = SpecVelocity;
            // just above the full pile (the dome is 2.5 in over the rim at its peak), rolling outward
            fuel.Spawn(new Vector3(at.x, at.y, TopZ + FuelPool.RadiusIn + 1.5f), new Vector3(dir.x * 30f + rv.x, dir.y * 30f + rv.y, 8f));
        }

        /// <summary>A loose GEAR on the carpet centred at spec position p: true if this robot's floor intake takes it.</summary>
        public bool TryCatchGear(Vector3 p)
        {
            if (def.intake == null || !def.intake.gearFromFloor || !enabledByMatch || !intakeHeld || def.gear == null || !def.gear.enabled || Climb != ClimbPhase.None) return false;
            if (GearCapacity > 0 && Gears >= GearCapacity) return false;
            var d = new Vector2(p.x, p.y) - Center;
            float lx = Vector2.Dot(d, Forward), ly = Vector2.Dot(d, Left) - def.gear.slotOffset;
            bool caught = p.z < 6f && lx >= HalfL - 2f && lx <= HalfL + 8f && Mathf.Abs(ly) <= 8f;
            if (caught && GearCapacity > 0) Gears++;
            return caught;
        }

        /// <summary>
        /// A GEAR coming out of a loading-station chute goes straight into the gear slot only if the robot can take
        /// one, faces the station, has its front within `reach` in of the chute, and covers at least `overlap` of
        /// the chute's width with its front, left to right. Otherwise it drops on the carpet.
        /// </summary>
        public bool TakeGearFromChute(LoadingSpec L, Vector2 chute, float reach, float overlap)
        {
            if (def.intake == null || !def.intake.gearFromChute || def.gear == null || !def.gear.enabled || Climb != ClimbPhase.None) return false;
            if (GearCapacity > 0 && Gears >= GearCapacity) return false;
            var inward = new Vector2(L.inward[0], L.inward[1]);
            var along = new Vector2(-inward.y, inward.x);
            if (Vector2.Dot(Forward, -inward) < Mathf.Cos(30f * Mathf.Deg2Rad)) return false; // the slot is on the front
            Vector2 c = Center, f = Forward * HalfL, l = Left * HalfW;
            float depth = Vector2.Dot(c + f - new Vector2(L.a[0], L.a[1]), inward);       // front face to the station face
            if (depth > reach) return false;
            float s1 = Vector2.Dot(c + f + l - chute, along), s2 = Vector2.Dot(c + f - l - chute, along);
            float covered = Mathf.Min(Mathf.Max(s1, s2), L.chuteHalfW) - Mathf.Max(Mathf.Min(s1, s2), -L.chuteHalfW);
            if (covered < overlap * 2f * L.chuteHalfW) return false;
            if (GearCapacity > 0) Gears++;
            return true;
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
            savedConstraints = rb.constraints;
            rb.constraints = RigidbodyConstraints.FreezeRotation; // the robot must be free to rise (Y was frozen)
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
                rb.constraints = savedConstraints;
                rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                drive.enabled = true;
            }
        }
    }
}
