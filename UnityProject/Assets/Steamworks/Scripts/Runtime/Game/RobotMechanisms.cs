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
        /// <summary>
        /// FUEL it can hold: shooter.capacity if robot.json sets one, else what its open-top hopper physically holds
        /// (RobotFactory.PhysicalCapacity; more overflows), else 0 = unlimited (testing).
        /// </summary>
        public int FuelCapacity => fuelCap >= 0 ? fuelCap : fuelCap = def.shooter != null && def.shooter.capacity > 0 ? def.shooter.capacity
                                   : def.intake != null && def.intake.fuelFromChute ? RobotFactory.PhysicalCapacity(def) : 0;
        int fuelCap = -1;
        bool HopperRobot => def.shooter != null && def.shooter.capacity <= 0 && FuelCapacity > 0;
        public int GearCapacity => def.gear != null ? def.gear.capacity : 0;
        bool HasFuel => FuelCapacity <= 0 || Fuel > 0;
        bool HasGear => GearCapacity <= 0 || Gears > 0;
        public int PreloadedFuel => def.preload == null || !CarriesFuel ? 0 : (FuelCapacity > 0 ? Mathf.Min(def.preload.fuel, FuelCapacity) : def.preload.fuel);
        public int PreloadedGears => def.preload == null ? 0 : (GearCapacity > 0 ? Mathf.Min(def.preload.gears, GearCapacity) : def.preload.gears);
        /// <summary>It has some way to take in or score FUEL (a robot with none never carries any).</summary>
        bool CarriesFuel => (def.shooter != null && def.shooter.enabled) || (def.lowGoalDump != null && def.lowGoalDump.enabled)
                            || (def.intake != null && (def.intake.fuelFromChute || def.intake.fuelFromFloor));
        public string LoadStatus => !CarriesFuel ? $"no FUEL mechanism, {(GearCapacity > 0 ? $"{Gears} / {GearCapacity}" : "unlimited")} GEAR" : $"{(HopperRobot ? $"{Fuel}{(Fuel >= FuelCapacity ? " (full: overflowing)" : "")}" : FuelCapacity > 0 ? $"{Fuel} / {FuelCapacity}" : "unlimited")} FUEL, {(GearCapacity > 0 ? $"{Gears} / {GearCapacity}" : "unlimited")} GEAR";
        public bool shootHeld, dumpHeld, intakeHeld, climbHeld;

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
            // the drivetrain holds the robot at a fixed height: no part of it may rub on the carpet
            if (FieldBuilder.Carpet != null) foreach (var c in body) if (c != null) Physics.IgnoreCollision(c, FieldBuilder.Carpet, true);
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

        // ---------------------------------------------------------------- for the referees (Violations)
        readonly System.Collections.Generic.Dictionary<RobotMechanisms, float> robotContact = new System.Collections.Generic.Dictionary<RobotMechanisms, float>();
        float staticContact = -100f;
        const float ContactHold = 0.06f; // s: a contact counts until this long after the last report

        void OnCollisionStay(Collision c) => NoteContact(c);
        void OnCollisionEnter(Collision c) => NoteContact(c);
        void NoteContact(Collision c)
        {
            var other = c.rigidbody != null ? c.rigidbody.GetComponent<RobotMechanisms>() : null;
            if (other != null && other != this) robotContact[other] = Time.fixedTime;
            else if (c.rigidbody == null && c.collider != FieldBuilder.Carpet)
            {
                staticContact = Time.fixedTime; // wall or field element
                var n = c.collider.name;
                if (n.Contains("ALLIANCE WALL") || n.Contains("PLAYER STATION") || n.Contains("OVERFLOW LOADING STATION"))
                {
                    if (n.Contains("RED ")) redWallContact = Time.fixedTime;
                    else if (n.Contains("BLUE ")) blueWallContact = Time.fixedTime;
                }
            }
        }
        float redWallContact = -100f, blueWallContact = -100f;

        /// <summary>Some part of the robot touches that alliance's ALLIANCE WALL, from any side: either PhysX reports a
        /// contact with it, or a corner of any of the robot's colliders is within <paramref name="tolerance"/> in of
        /// the wall plane (x = ±wallX) between <paramref name="yMin"/> and <paramref name="yMax"/>.</summary>
        public bool TouchingAllianceWall(string al, float wallX, float yMin, float yMax, float tolerance)
        {
            float t = al == "blue" ? blueWallContact : redWallContact;
            if (Time.fixedTime - t <= 0.15f) return true;
            float side = al == "blue" ? 1f : -1f;
            foreach (var c in Corners())
                if (side * c.x >= wallX - tolerance && c.y >= yMin && c.y <= yMax) return true;
            if (body == null) return false;
            foreach (var col in body)
            {
                var b = col as BoxCollider;
                if (b == null || b.isTrigger || !b.enabled) continue;
                var h = b.size / 2f;
                for (int i = 0; i < 8; i++)
                {
                    var local = b.center + new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
                    var sp = Units.ToSpec(b.transform.TransformPoint(local));
                    if (side * sp.x >= wallX - tolerance && sp.y >= yMin && sp.y <= yMax) return true;
                }
            }
            return false;
        }

        /// <summary>In contact with that robot (bumper to bumper, either way).</summary>
        public bool Touching(RobotMechanisms o) =>
            (robotContact.TryGetValue(o, out var t) && Time.fixedTime - t <= ContactHold) ||
            (o.robotContact.TryGetValue(this, out var u) && Time.fixedTime - u <= ContactHold);
        /// <summary>Against a wall or a field element.</summary>
        public bool TouchingField => Time.fixedTime - staticContact <= ContactHold;
        /// <summary>Spec velocity (in/s) on the carpet.</summary>
        public Vector2 Velocity => SpecVelocity;
        /// <summary>The driver is driving it (a pinner has to be pushing).</summary>
        public bool Driving
        {
            get
            {
                if (drive == null || !drive.enabled || !enabledByMatch) return false;
                var i = drive.input;
                float strafe = drive.Mecanum ? Mathf.Abs(i.strafe) : 0f;
                return i.tank ? Mathf.Abs(i.left) + Mathf.Abs(i.right) + strafe > 0.25f : Mathf.Abs(i.throttle) + Mathf.Abs(i.turn) + strafe > 0.25f;
            }
        }
        /// <summary>The driver is driving it toward that point (spec in): forward or backward, whichever faces it.</summary>
        public bool PushingToward(Vector2 target)
        {
            if (!Driving) return false;
            var i = drive.input;
            float th = i.tank ? (i.left + i.right) / 2f : i.throttle;
            float st = drive.Mecanum ? i.strafe : 0f;                // mecanum robots also push sideways
            var push = Forward * th - Left * st;
            if (push.magnitude < 0.2f) return false;
            return Vector2.Dot(push, target - Center) > 0f;
        }

        /// <summary>Some part of its bumpers is in that alliance's LAUNCHPAD (wall to the launchpad line, tape included).</summary>
        public bool InLaunchpad(string al)
        {
            var T = targets != null ? targets.Of(al) : null;
            if (T == null) return false;
            float edge = Mathf.Abs(T.launchpadX);
            foreach (var c in Corners()) if (T.side * c.x >= edge) return true;
            return false;
        }
        /// <summary>Some part of its bumpers is in that alliance's KEY (the boiler corner behind the key tape).</summary>
        public bool InKey(string al)
        {
            var T = targets != null ? targets.Of(al) : null;
            if (T == null || T.key == null || T.key.Length < 4) return false;
            Vector2 a = new Vector2(T.key[0], T.key[1]), b = new Vector2(T.key[2], T.key[3]), boiler = new Vector2(T.highGoal.c[0], T.highGoal.c[1]);
            float Side(Vector2 p) { var d = b - a; var q = p - a; return d.x * q.y - d.y * q.x; }
            float sb = Side(boiler);
            foreach (var c in Corners()) if (Side(c) * sb >= 0f) return true;
            return false;
        }

        public RopeSystem.Body AsBody() => new RopeSystem.Body
        {
            center = Center, fwd = Forward, halfL = HalfL, halfW = HalfW, lift = Lift, height = Height, id = id,
            climbing = Climb != ClimbPhase.None ? ClimbRope : null, climber = ClimberPoint(),
        };

        Vector2 ClimberPoint() => Center + Forward * GripFwd;
        float ClimbSign => def.climber != null && def.climber.back ? -1f : 1f;

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

            // moving parts of the generated model (RobotDetail): turret, flywheel, hood, intake and conveyor rollers
            turret = visual.Find("Turret yaw");
            if (turret != null) { flywheel = turret.Find("Flywheel"); hood = turret.Find("Hood"); }
            intakeRoller = visual.Find("Intake roller");
            indexer = visual.Find("Indexer");
            gearSlider = visual.Find("Gear slider");
            if (gearSlider != null) sliderHome = gearSlider.localPosition;
            var conv = new System.Collections.Generic.List<Transform>();
            for (int k = 0; k < 8; k++) { var c = visual.Find($"Conveyor roller {k}"); if (c != null) conv.Add(c); }
            conveyor = conv.ToArray();
            // FUEL in the open-top hopper, so you can see how full the robot is. The balls lie loose, as a real pile
            // does: each one settles into the lowest gap it can find on top of the ones already there.
            if (def.intake != null && def.intake.fuelFromChute && fuel != null)
            {
                var sphereMesh = BallMesh;
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
            // An indexer with pockets (model parts "Indexer/Pocket k"): the first FUEL sit in its pockets and go round
            // with it; the rest of the load is the pile above
            if (indexer != null && fuel != null)
            {
                var list = new System.Collections.Generic.List<GameObject>();
                for (int k = 0; k < 16; k++)
                {
                    var pk = indexer.Find($"Pocket {k}");
                    if (pk == null) break;
                    var b = new GameObject("FUEL");
                    b.transform.SetParent(pk, false);
                    b.transform.localScale = Vector3.one * (2f * FuelPool.RadiusIn * In);
                    b.AddComponent<MeshFilter>().sharedMesh = BallMesh;
                    b.AddComponent<MeshRenderer>().sharedMaterial = fuel.BallMaterial;
                    b.SetActive(false);
                    list.Add(b);
                }
                pocketBalls = list.ToArray();
            }
            // The GEAR it carries, standing in the gear slot (front or back)
            if (def.gear != null && def.gear.enabled)
            {
                heldGear = new GameObject("Held GEAR").transform;
                heldGear.SetParent(visual, false);
                heldGear.localPosition = HeldGearAt();
                heldGear.localRotation = Quaternion.FromToRotation(Vector3.up, Vector3.forward);
                heldGear.gameObject.AddComponent<MeshFilter>().sharedMesh = FieldAnimator.GearMesh();
                var fm = transform.Find("Visual/Gear marker") ?? transform.Find("Visual/Front marker");
                heldGear.gameObject.AddComponent<MeshRenderer>().sharedMaterial = fm != null ? fm.GetComponent<MeshRenderer>().sharedMaterial : dark;
                // a gear mechanism that folds: the GEAR rides on it
                gearIntake = visual.Find("Gear intake");
                if (gearIntake != null)
                {
                    gearIntakeHome = gearIntake.localRotation;
                    heldGear.SetParent(gearIntake, true);
                    heldHome = heldGear.localPosition;
                }
            }
            // FUEL riding up the intake conveyor (visual; counted when it drops into the hopper)
            if (RobotFactory.HasFloorIntake(def) && fuel != null)
            {
                var holder = new GameObject("Intake FUEL").transform;
                holder.SetParent(visual, false);
                riders = new Rider[10];
                for (int i = 0; i < riders.Length; i++)
                {
                    var b = new GameObject("FUEL");
                    b.transform.SetParent(holder, false);
                    b.transform.localScale = Vector3.one * (2f * FuelPool.RadiusIn * In);
                    b.AddComponent<MeshFilter>().sharedMesh = BallMesh;
                    b.AddComponent<MeshRenderer>().sharedMaterial = fuel.BallMaterial;
                    b.SetActive(false);
                    riders[i] = new Rider { go = b };
                }
            }
        }

        // ---------------------------------------------------------------- reset
        /// <summary>Start of a match: just the preload on board.</summary>
        public void Reload()
        {
            Fuel = PreloadedFuel; Gears = PreloadedGears; overflow = 0;
            if (riders != null) foreach (var r in riders) { r.active = false; r.go.SetActive(false); }
        }

        /// <summary>Off the rope and back on the carpet (the load stays as it is).</summary>
        public void ResetState()
        {
            Climb = ClimbPhase.None; ClimbRope = null; Lift = 0f;
            shootT = dumpT = 0f; gearCooldown = 0f; SliderPos = 0f; foldNow = 0f;
            if (rb != null) { rb.isKinematic = false; if (Climb == ClimbPhase.None) rb.constraints = savedConstraints; }
            if (drive != null) drive.enabled = true;
            var p = transform.position; p.y = 0f; transform.position = p;
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f); // upright (it may have been swinging on a rope)
            if (rb != null) { rb.position = p; rb.rotation = transform.rotation; }
        }

        // ---------------------------------------------------------------- per tick
        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            rules = getRules != null ? getRules() : null;
            if (gearCooldown > 0f) gearCooldown -= dt;
            if (heldGear != null) heldGear.gameObject.SetActive(gearCooldown <= 0f && Climb == ClimbPhase.None && HasGear);
            StepRiders(dt);
            StepGearFold(dt);
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

        Vector2 TurretCenter()
        {
            var off = def.shooter.exitOffset != null && def.shooter.exitOffset.Length >= 2 ? def.shooter.exitOffset : new[] { 0f, 0f };
            return Center + Forward * off[0] + Left * off[1];
        }

        /// <summary>
        /// Where a ball leaves the turret for a shot toward `dir` at `pitch` (rad): it rides the flywheel under the hood
        /// and leaves tangentially, so the exit sits on the ball's path circle at 90 deg + pitch from the forward axis
        /// (behind and above the axle). The axle is at shooter.exitHeight.
        /// </summary>
        Vector3 ExitPoint(Vector2 dir, float pitch)
        {
            var c = TurretCenter() + dir * (RobotDetail.FlywheelFwd - RobotDetail.BallPathR * Mathf.Sin(pitch));
            return new Vector3(c.x, c.y, def.shooter.exitHeight + Lift + RobotDetail.BallPathR * Mathf.Cos(pitch));
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
            if (!InLaunchpad(alliance)) { reason = "Not in your LAUNCHPAD"; return null; } // shoot only from your own launchpad
            var hg = targets.Of(alliance).highGoal;
            var goal = new Vector3(hg.c[0], hg.c[1], hg.z + FuelPool.RadiusIn + FuelPool.HighGoalSenseLift); // where the goal counts it
            var dir = (new Vector2(goal.x, goal.y) - TurretCenter()).normalized;
            // the exit point depends on the pitch: solve, move the exit to where that pitch leaves the hood, solve again
            Shot s = default; float pitch = 60f * Mathf.Deg2Rad; bool solved = false;
            for (int it = 0; it < 3; it++)
            {
                if (!Solve(ExitPoint(dir, pitch), goal, S.entryAngle, out s)) { solved = false; break; }
                solved = true; pitch = s.pitch;
            }
            if (!solved) { reason = "No shot"; return null; }
            if (s.range < S.minRange * 12f) { reason = $"Too close ({s.range / 12f:0.0} ft)"; return null; }
            if (s.range > S.maxRange * 12f || s.speed > S.maxExitSpeed * 12f) { reason = $"Out of range ({s.range / 12f:0.0} ft)"; return null; }
            reason = S.accuracy > 0f ? $"In range {s.range / 12f:0.0} ft ({MakeChance(s.range / 12f) * 100f:0}%)" : $"In range {s.range / 12f:0.0} ft";
            return s;
        }

        void UpdateShooterStatus(out Shot? shot)
        {
            shot = Aim(out var reason);
            ShooterStatus = reason;
            // the hood tilts with the shot: steeper close in, flatter far out
            if (shot != null) hoodTilt = Mathf.Lerp(hoodTilt, -shot.Value.pitch * Mathf.Rad2Deg, 0.2f);
            if (turret == null) return;
            // point the hood at the goal (yaw only)
            var hg = targets.Of(alliance).highGoal;
            var d = new Vector2(hg.c[0], hg.c[1]) - Center;
            float worldYaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg; // Unity yaw: spec (x, y) -> (x, z)
            float local = Mathf.DeltaAngle(transform.eulerAngles.y, worldYaw);
            turret.localRotation = Quaternion.Slerp(turret.localRotation, Quaternion.Euler(0f, local, 0f), 0.25f);
        }

        /// <summary>Chance a shot from `ft` feet goes in, with the shooter's make-rate model (shooter.accuracy).</summary>
        public float MakeChance(float ft)
        {
            var S = def.shooter;
            if (S == null || S.accuracy <= 0f) return 1f;
            float p = S.accuracy - S.accuracyDrop * Mathf.Max(0f, ft - S.accurateRange);
            return Mathf.Clamp(p, Mathf.Min(S.accuracyMin, S.accuracy), S.accuracy);
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
            if (S.accuracy > 0f && rnd.NextDouble() >= MakeChance(s.range / 12f))
            {
                // a miss: aim at a point just outside the goal's capture circle (3-10 in past it), in a random
                // direction: sideways by turning the turret, long or short by changing the speed (range ~ speed^2)
                var hg = targets.Of(alliance).highGoal;
                float off = hg.capture + 3f + 7f * (float)rnd.NextDouble(), th = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float range = Mathf.Max(12f, s.range);
                yaw += Mathf.Sin(th) * off / range;
                sp *= Mathf.Sqrt(Mathf.Max(0.5f, 1f + Mathf.Cos(th) * off / range));
            }
            float vh = sp * Mathf.Cos(pitch);
            // the turret leads for the robot's own motion, so the ball leaves the robot with exactly the solved velocity
            fuel.Spawn(s.p0, new Vector3(vh * Mathf.Cos(yaw), vh * Mathf.Sin(yaw), sp * Mathf.Sin(pitch)), body);
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
        /// <summary>Robot-local position (Unity) of the GEAR it carries, slider included.</summary>
        Vector3 HeldGearAt()
        {
            var G = def.gear;
            return new Vector3(-(G.slotOffset + SliderPos) * In, G.holdHeight * In, GearSign * (HalfL - G.holdInset) * In);
        }
        float GearSign => def.gear != null && def.gear.back ? -1f : 1f;
        /// <summary>The direction the gear slot faces (forward, or backward for a slot on the back).</summary>
        Vector2 GearDir => Forward * GearSign;

        int GearAlign(out bool ok, out float dist, out float lat, out float angle)
        {
            ok = false; dist = lat = angle = 0f;
            var G = def.gear;
            if (G == null || !G.enabled) return -1;
            Vector2 f = GearDir;
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
                float side = Vector2.Dot(-rel, Left); // where the peg is, left of the slot's centre
                // a gear slider carries the GEAR sideways toward the peg (up to G.slider either way)
                float lt = Mathf.Abs(side - SliderPos);
                float score = Mathf.Abs(d) + Mathf.Max(0f, Mathf.Abs(side) - G.slider) + ang / 5f;
                if (score < bestScore)
                {
                    bestScore = score; best = i;
                    ok = ang <= G.maxAngle && lt <= G.maxLateral && (G.slider <= 0f || Mathf.Abs(side) <= G.maxLateral + G.slider) && d <= G.maxDistance && d >= -2f;
                    dist = d; lat = lt; angle = ang; pegSide = side;
                }
            }
            return best;
        }

        /// <summary>Gear slider (robot.json gear.slider): how far (in) it has carried the GEAR to the robot's left.</summary>
        public float SliderPos { get; private set; }
        float pegSide; // from the last GearAlign: the nearest peg, in left of the slot's centre
        const float SliderSpeed = 18f; // in/s

        void UpdateGearStatus()
        {
            if (def.gear == null || !def.gear.enabled) { GearStatus = "No gear slot"; return; }
            int i = GearAlign(out bool ok, out float dist, out _, out float angle);
            // the slider follows the peg while the slot side is coming up to it, else goes back to the middle
            var G = def.gear;
            if (G.slider > 0f)
            {
                bool near = i >= 0 && dist <= G.maxDistance + 18f && dist >= -2f && angle <= G.maxAngle + 10f && enabledByMatch;
                float want = near ? Mathf.Clamp(pegSide, -G.slider, G.slider) : 0f;
                SliderPos = Mathf.MoveTowards(SliderPos, want, SliderSpeed * Time.fixedDeltaTime);
            }
            GearStatus = ok ? $"Lined up: {targets.Of(alliance).lifts[i].name} lift" : "-";
        }

        /// <summary>Place GEAR (E / A): onto the lift peg the front slot is lined up with.</summary>
        public void PlaceGear()
        {
            if (!enabledByMatch || Climb != ClimbPhase.None || def.gear == null || !def.gear.enabled || gearCooldown > 0f || !HasGear) return;
            int i = GearAlign(out bool ok, out float dist, out float lat, out float angle);
            if (i < 0 || !ok) { Toast?.Invoke(def.gear.back ? "Not lined up with a peg: back the gear slot onto it" : "Not lined up with a peg: drive the gear slot onto it"); return; }
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
            int pockets = pocketBalls != null ? pocketBalls.Length : 0;
            for (int k = 0; k < pockets; k++)
            {
                bool on = FuelCapacity <= 0 || k < Fuel;
                if (pocketBalls[k].activeSelf != on) pocketBalls[k].SetActive(on);
            }
            if (hopperBalls == null) return;
            int slots = hopperBalls.Length, n;
            if (FuelCapacity <= 0) n = slots;                                         // unlimited (testing): shown full
            else if (FuelCapacity <= slots) n = Mathf.Max(0, Fuel - pockets);         // one ball per FUEL carried
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
            if (caught) { if (FuelCapacity > 0) Fuel++; return true; }
            // floor intake in the gap in the front bumper: the ball is pulled in under the roller, rides up the
            // conveyor and drops into the hopper (counted then; if the hopper is full, one rolls off the top)
            if (def.intake.fuelFromFloor && enabledByMatch && intakeHeld && p.z <= FuelPool.RadiusIn + 2f && RideFree() >= 0)
            {
                bool inMouth = RobotFactory.HasFloorIntake(def)
                    // the roller sits at the bumper line: it grabs a ball as soon as it touches it, anywhere from just
                    // inside the bumpers to just in front of them, so the ball doesn't have to be shoved into the notch
                    ? lx >= HalfL - def.bumpers.thickness - FuelPool.RadiusIn && lx <= HalfL + FuelPool.RadiusIn + 0.5f
                      && Mathf.Abs(ly) <= RobotFactory.IntakeGapHalf(def) + (RobotFactory.WideIntake(def) ? 0.5f : 1f - FuelPool.RadiusIn)
                    : lx >= HalfL - 1f && lx <= HalfL + FuelPool.RadiusIn + 2f && Mathf.Abs(ly) <= HalfW - 3f;
                if (inMouth)
                {
                    if (riders == null) { if (full) overflow++; else if (FuelCapacity > 0) Fuel++; }
                    else Ride(ly);
                    return true;
                }
            }
            return false;
        }

        int overflow; // balls to push out over the top of a full hopper (spawned next physics step, not mid-loop)

        // ---- FUEL riding the intake conveyor (visual), counted when it drops into the hopper
        class Rider { public GameObject go; public bool active; public float s; public Vector3[] path; }
        Rider[] riders;
        const float RideSpeed = 75f; // in/s along the path

        int RideFree() { if (riders == null) return 0; for (int i = 0; i < riders.Length; i++) if (!riders[i].active) return i; return -1; }

        void Ride(float left)
        {
            int i = RideFree();
            if (i < 0) return;
            var r = riders[i];
            r.active = true; r.s = 0f;
            var path = RobotDetail.IntakePath(def, left);
            // last leg: down into the pile where the next ball goes (or the middle when it's heaped up)
            var pile = RobotFactory.HopperPile(def);
            int slot = Mathf.Min(Fuel + Riding() - 1, pile.Count - 1); // Riding() already counts this one
            float floor = RobotFactory.HopperFloor(def);
            var end = slot >= 0 && slot < pile.Count ? new Vector3(pile[slot].x, pile[slot].y, floor + pile[slot].z) : new Vector3(0f, 0f, Height);
            r.path = new Vector3[path.Length + 1];
            path.CopyTo(r.path, 0);
            r.path[path.Length] = end;
            r.go.SetActive(true);
            Place(r);
        }

        int Riding() { int n = 0; if (riders != null) foreach (var r in riders) if (r.active) n++; return n; }

        void Place(Rider r)
        {
            float s = r.s;
            for (int k = 0; k + 1 < r.path.Length; k++)
            {
                float len = Vector3.Distance(r.path[k], r.path[k + 1]);
                if (s <= len || k + 2 == r.path.Length)
                {
                    var p = Vector3.Lerp(r.path[k], r.path[k + 1], len > 0f ? Mathf.Clamp01(s / len) : 1f);
                    r.go.transform.localPosition = RobotDetail.V(p.x, -p.y, p.z);
                    return;
                }
                s -= len;
            }
        }

        static float PathLength(Vector3[] p) { float l = 0f; for (int k = 0; k + 1 < p.Length; k++) l += Vector3.Distance(p[k], p[k + 1]); return l; }

        void StepRiders(float dt)
        {
            if (riders == null) return;
            foreach (var r in riders)
            {
                if (!r.active) continue;
                r.s += RideSpeed * dt;
                if (r.s >= PathLength(r.path))
                {
                    r.active = false; r.go.SetActive(false);
                    if (FuelCapacity > 0 && Fuel >= FuelCapacity) overflow++; // full: one rolls off the top
                    else if (FuelCapacity > 0) Fuel++;
                    continue;
                }
                Place(r);
            }
        }

        static Mesh ballMesh;
        static Mesh BallMesh
        {
            get
            {
                if (ballMesh != null) return ballMesh;
                var mb = new MeshBuilder();
                mb.SmoothSphere(Vector3.zero, 0.5f / Units.Inch, 20, 12);
                return ballMesh = mb.ToMesh("Robot FUEL");
            }
        }

        // ---- moving parts
        Transform flywheel, hood, intakeRoller, indexer;
        GameObject[] pocketBalls;
        Transform[] conveyor;
        float spinIntake, spinFly, hoodTilt;

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            bool feeding = enabledByMatch && (intakeHeld || dumpHeld) || Riding() > 0;
            spinIntake = Mathf.MoveTowards(spinIntake, feeding ? (dumpHeld && !intakeHeld ? -1f : 1f) : 0f, dt * 4f);
            spinFly = Mathf.MoveTowards(spinFly, enabledByMatch && shootHeld ? 1f : 0f, dt * 1.5f);
            float deg = 900f * dt;
            if (intakeRoller != null) intakeRoller.Rotate(-spinIntake * deg, 0f, 0f, Space.Self);
            if (conveyor != null) foreach (var c in conveyor) c.Rotate(spinIntake * deg, 0f, 0f, Space.Self);
            if (flywheel != null) flywheel.Rotate(spinFly * 2.5f * deg, 0f, 0f, Space.Self); // back of the wheel moves up
            // the indexer turns while shooting (feeding the shooter) and slowly while taking FUEL in (sorting it)
            if (indexer != null) indexer.Rotate(0f, Mathf.Max(spinFly * 0.45f, Mathf.Abs(spinIntake) * 0.15f) * deg, 0f, Space.Self);
            if (hood != null) hood.localRotation = Quaternion.Euler(hoodTilt, 0f, 0f);
            if (gearSlider != null) gearSlider.localPosition = new Vector3(sliderHome.x - SliderPos * In, sliderHome.y, sliderHome.z);
            if (heldGear != null && def.gear != null && (def.gear.slider > 0f || def.gear.holdWidth > 0f))
                heldGear.localPosition = gearIntake != null ? heldHome + Vector3.left * (SliderPos * In) : HeldGearAt();
            if (gearIntake != null) gearIntake.localRotation = gearIntakeHome * Quaternion.Euler(foldNow, 0f, 0f);
        }
        Transform gearIntake; Quaternion gearIntakeHome; Vector3 heldHome;
        float foldNow; // deg the gear mechanism is folded forward (0 = standing up)

        /// <summary>How far (in) either way of centre a wide gear mechanism can carry the GEAR (gear.holdWidth).</summary>
        float HoldRange => def.gear != null && def.gear.holdWidth > 0f ? Mathf.Max(0f, def.gear.holdWidth / 2f - 5.5f) : 0f;
        bool Folds => gearIntake != null && def.gear != null && def.gear.pickupFold > 0f;

        /// <summary>Gear pick-up that folds to the floor: down while Intake is held and there's room for a GEAR, back up
        /// (with the GEAR) otherwise. About a third of a second each way.</summary>
        void StepGearFold(float dt)
        {
            if (!Folds) return;
            bool down = intakeHeld && enabledByMatch && Climb == ClimbPhase.None && def.intake != null && def.intake.gearFromFloor
                        && (GearCapacity <= 0 || Gears < GearCapacity);
            foldNow = Mathf.MoveTowards(foldNow, down ? def.gear.pickupFold : 0f, 320f * dt);
        }
        Transform gearSlider; Vector3 sliderHome;

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
            // the floor intake is on the front (in the bumper opening, if there is one); a GEAR lying flat goes in
            var d = new Vector2(p.x, p.y) - Center;
            float lx = Vector2.Dot(d, Forward), ly = Vector2.Dot(d, Left);
            bool caught;
            if (Folds)
            {
                // folded flat in front of the robot: a GEAR slides in anywhere across the mechanism
                if (foldNow < def.gear.pickupFold * 0.85f) return false;
                float half = def.gear.holdWidth > 0f ? def.gear.holdWidth / 2f : 8f;
                caught = p.z < 6f && lx >= HalfL - 2f && lx <= HalfL + 12f && Mathf.Abs(ly - def.gear.slotOffset) <= half;
            }
            else
            {
                float gap = RobotDetail.FrontGapHalf(def), halfOpening = gap > 0f ? gap + 2f : 8f;
                caught = p.z < 6f && lx >= HalfL - 4f && lx <= HalfL + 7f && Mathf.Abs(ly) <= halfOpening;
            }
            if (!caught) return false;
            if (GearCapacity > 0) Gears++;
            SliderPos = Mathf.Clamp(ly - def.gear.slotOffset, -HoldRange, HoldRange); // carried where it went in (0 if centred)
            if (def.gear.slider > 0f) SliderPos = 0f;
            return true;
        }

        /// <summary>
        /// A GEAR coming out of a loading-station chute goes straight into the gear slot only if the robot can take
        /// one, faces the station, has its front within `reach` in of the chute, and covers at least `overlap` of
        /// the chute's width with its front, left to right. Otherwise it drops on the carpet.
        /// </summary>
        /// <summary>What happened at the last GEAR request from a chute (drive info panel), and when.</summary>
        public string ChuteGearStatus { get; private set; } = "";
        public float ChuteGearAt { get; private set; } = -100f;

        public bool TakeGearFromChute(LoadingSpec L, Vector2 chute, float reach, float overlap)
        {
            ChuteGearAt = Time.time;
            if (def.intake == null || !def.intake.gearFromChute || def.gear == null || !def.gear.enabled) { ChuteGearStatus = "this robot can't take GEARS from a chute"; return false; }
            if (Climb != ClimbPhase.None) { ChuteGearStatus = "climbing"; return false; }
            if (GearCapacity > 0 && Gears >= GearCapacity) { ChuteGearStatus = $"already carrying {Gears} GEAR{(Gears == 1 ? "" : "S")} (place it first)"; return false; }
            var inward = new Vector2(L.inward[0], L.inward[1]);
            var along = new Vector2(-inward.y, inward.x);
            float facing = Vector2.Angle(GearDir, -inward);
            if (facing > 30f) { ChuteGearStatus = $"gear slot not facing the chute ({facing:0}° off, 30° max)"; return false; }
            Vector2 c = Center, f = GearDir * HalfL, l = Left * HalfW;
            float depth = Vector2.Dot(c + f - new Vector2(L.a[0], L.a[1]), inward);       // slot-side face to the station face
            if (depth > reach) { ChuteGearStatus = $"gear slot {depth:0} in from the wall ({reach:0} in max)"; return false; }
            float s1 = Vector2.Dot(c + f + l - chute, along), s2 = Vector2.Dot(c + f - l - chute, along);
            float covered = Mathf.Min(Mathf.Max(s1, s2), L.chuteHalfW) - Mathf.Max(Mathf.Min(s1, s2), -L.chuteHalfW);
            if (covered < overlap * 2f * L.chuteHalfW) { ChuteGearStatus = $"not in front of the chute ({Mathf.Max(0f, covered) / (2f * L.chuteHalfW) * 100f:0}% covered, {overlap * 100f:0}% needed)"; return false; }
            if (GearCapacity > 0) Gears++;
            if (HoldRange > 0f) SliderPos = Mathf.Clamp(Vector2.Dot(chute - (c + f), Left) - def.gear.slotOffset, -HoldRange, HoldRange);
            ChuteGearStatus = "GEAR in the slot";
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
                case ClimbPhase.Hold: ClimbStatus = "Pressing TOUCHPAD (keep holding Climb)"; return;
                case ClimbPhase.Down: ClimbStatus = "Sliding down: hold Climb"; return;
            }
            ClimbStatus = FindRope() != null ? "Rope in reach: hold Climb" : AnyRopeDeployed() ? "-" : "Ropes drop at 0:30";
        }

        bool AnyRopeDeployed() { if (ropes == null) return false; foreach (var R in ropes.ropes) if (R.deployed) return true; return false; }

        RopeSystem.Rope FindRope()
        {
            if (ropes == null || def.climber == null || !def.climber.enabled) return null;
            var b = AsBody();
            b.fwd *= ClimbSign; // reach is measured out from the climber's end of the robot
            foreach (var R in ropes.ropes)
                if (R.alliance == alliance && RopeSystem.InReach(R, b, def.climber.reachForward, def.climber.reachLateral)) return R;
            return null;
        }

        /// <summary>
        /// Climb (T / X), pressed: latch onto an own rope in front of the bumper. Climbing then follows climbHeld:
        /// held, the robot winches up until it pushes the TOUCHPAD up; released, it slowly slides back down
        /// (CreepRate) and lets go of the rope at the carpet.
        /// </summary>
        public void StartClimb()
        {
            if (!enabledByMatch || def.climber == null || !def.climber.enabled) return;
            if (Climb != ClimbPhase.None) return; // already on the rope: holding the button is what climbs
            var R = FindRope();
            if (R == null) { Toast?.Invoke(AnyRopeDeployed() ? "Drive your front bumper into one of your ropes first" : "ROPES drop with 30 seconds left"); return; }
            ClimbRope = R;
            climbTopZ = targets.Of(alliance).touchpads[R.touchpad].z + FullClimbPress;
            Climb = ClimbPhase.Attach;
            drive.enabled = false;
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            savedConstraints = rb.constraints;
            rb.constraints = RigidbodyConstraints.None; // kinematic: moved (and swung) by ClimbStep
            rb.isKinematic = true;
            climbC = Center;
            climbYaw = rb.rotation.eulerAngles.y;
            swingAng = swingVel = Vector2.zero; pullT = 0f;
            Toast?.Invoke($"Latched: climbing to TOUCHPAD {R.touchpad + 1}");
        }

        // ---- swinging on the rope: the robot hangs from its grip like a pendulum from the TOUCHPAD cut-out the rope
        // is threaded through, swaying fore-aft and side to side; each pull of the winch nudges it, and the sway
        // fades as it gets short near the top
        Vector2 climbC;           // spec centre of the robot, unswung
        float climbYaw;           // Unity yaw while on the rope
        Vector2 swingAng, swingVel; // rope angle (rad): x fore-aft, y side to side (robot frame)
        float pullT;
        const float SwingDamping = 0.12f, MaxSwing = 0.3f, PullPeriod = 0.45f;

        Vector2 YawFwd => new Vector2(Mathf.Sin(climbYaw * Mathf.Deg2Rad), Mathf.Cos(climbYaw * Mathf.Deg2Rad));
        float GripFwd => ClimbSign * (HalfL - (def.climber != null ? def.climber.setback : 3f)); // signed: negative for a climber on the back

        void StepSwing(float dt, float ropeLen, bool pulling)
        {
            if (pulling)
            {
                pullT += dt;
                if (pullT >= PullPeriod)
                {
                    pullT -= PullPeriod;
                    // hand over hand: a tug toward the rope and a little to one side
                    swingVel += new Vector2(0.12f + 0.08f * (float)rnd.NextDouble(), ((float)rnd.NextDouble() * 2f - 1f) * 0.18f);
                }
            }
            float w2 = Gravity / ropeLen, c = 2f * SwingDamping * Mathf.Sqrt(w2);
            swingVel += (-w2 * swingAng - c * swingVel) * dt;
            swingAng += swingVel * dt;
            swingAng = Vector2.ClampMagnitude(swingAng, MaxSwing);
        }

        /// <summary>Puts the kinematic robot where its grip is, hanging along the swinging rope.</summary>
        void PoseOnRope(float ropeLen)
        {
            Vector2 f = YawFwd, l = new Vector2(-f.y, f.x);
            float k = Mathf.Clamp01(Lift / 4f); // no sway while the wheels are still on the carpet
            var a = swingAng * k;
            var grip = climbC + f * GripFwd + (f * Mathf.Sin(a.x) + l * Mathf.Sin(a.y)) * ropeLen;
            var q = Quaternion.Euler(0f, climbYaw, 0f) * Quaternion.Euler(-a.x * Mathf.Rad2Deg, 0f, -a.y * Mathf.Rad2Deg);
            var gripLocal = new Vector3(0f, Height * In, GripFwd * In);
            var root = Units.ToUnity(grip.x, grip.y, Lift + Height) - q * gripLocal;
            rb.MovePosition(root);
            rb.MoveRotation(q);
        }

        const float FullClimbPress = 3.5f; // in: a full climb pushes the TOUCHPAD up this far (visible)
        const float CreepRate = 2f;        // in/s: sliding back down the rope when Climb isn't held

        void ClimbStep(float dt)
        {
            float rate = def.climber.rate;
            float pinZ = ClimbRope.spec.pin != null && ClimbRope.spec.pin.Length >= 3 ? ClimbRope.spec.pin[2] : ClimbRope.spec.anchor[2];
            float ropeLen = Mathf.Max(6f, pinZ - (Lift + Height));
            bool pulling = false;
            if (Climb == ClimbPhase.Attach)
            {
                var a = new Vector2(ClimbRope.spec.anchor[0], ClimbRope.spec.anchor[1]);
                var d = a - (climbC + YawFwd * GripFwd);
                float step = 30f * dt;
                if (d.magnitude <= step) { climbC += d; Climb = ClimbPhase.Up; swingVel.x += 0.35f; } // swings in as it lifts off
                else climbC += d.normalized * step;
            }
            else if (enabledByMatch) // after 0:00 the robot stays where it is
            {
                float target = climbTopZ - Height;
                if (climbHeld)
                {
                    pulling = Lift < target;
                    Lift = Mathf.Min(target, Lift + rate * dt);
                    Climb = Lift >= target ? ClimbPhase.Hold : ClimbPhase.Up;
                }
                else
                {
                    Lift = Mathf.Max(0f, Lift - CreepRate * dt);
                    Climb = ClimbPhase.Down;
                }
            }
            if (Climb != ClimbPhase.Attach) StepSwing(dt, ropeLen, pulling);
            PoseOnRope(ropeLen);
            if (Climb == ClimbPhase.Down && Lift <= 0f)
            {
                Climb = ClimbPhase.None; ClimbRope = null;
                // back on the carpet, upright, where it hung
                var up = Units.ToUnity(climbC.x, climbC.y, 0f);
                rb.position = up; rb.rotation = Quaternion.Euler(0f, climbYaw, 0f);
                transform.SetPositionAndRotation(up, rb.rotation);
                rb.isKinematic = false;
                rb.constraints = savedConstraints;
                rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                drive.enabled = true;
            }
        }
    }
}
