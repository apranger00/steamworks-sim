using System.Collections.Generic;
using SteamworksSim.Field;
using UnityEngine;

namespace SteamworksSim.Robots
{
    /// <summary>
    /// Builds a robot GameObject from a RobotDefinition (robot.json): rigidbody, collider, drivetrain and a
    /// model: the robot's own (RobotModel, model.json made from its CAD) with generated alliance bumpers, or else a
    /// generated blockout (frame, alliance bumpers, wheels, superstructure).
    ///
    /// Robot-local axes (Unity): +z forward, +x right, +y up. The root sits on the carpet (y = 0).
    /// </summary>
    public static class RobotFactory
    {
        public const float KgPerLb = 0.45359237f;
        const float In = Units.Inch;

        static Mesh cubeMesh, cylinderMesh;
        static PhysicsMaterial bumperMaterial;

        static Mesh PrimitiveMesh(PrimitiveType t)
        {
            var tmp = GameObject.CreatePrimitive(t);
            var mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(tmp);
            return mesh;
        }

        public static GameObject Build(RobotDefinition d, string alliance, FieldMaterials mats, string label = null)
        {
            if (cubeMesh == null) cubeMesh = PrimitiveMesh(PrimitiveType.Cube);
            if (cylinderMesh == null) cylinderMesh = PrimitiveMesh(PrimitiveType.Cylinder);
            if (bumperMaterial == null)
            {
                // Low friction against walls (robots slide along them); the carpet is never touched because
                // the robot rides at a fixed height and the drivetrain supplies all traction.
                bumperMaterial = new PhysicsMaterial("Robot bumpers")
                {
                    dynamicFriction = 0.2f, staticFriction = 0.2f, bounciness = 0.05f,
                    frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum,
                };
            }

            var root = new GameObject(label ?? $"Robot: {d.name} ({alliance})");
            float L = d.LengthWithBumpers * In, W = d.WidthWithBumpers * In, H = d.frame.height * In;
            const float clearance = 1f * In; // the body rides clear of the carpet (and ignores it, see RobotMechanisms)

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = d.mass * KgPerLb;
            rb.useGravity = false;
            rb.linearDamping = 0f;
            rb.angularDamping = 0f;
            rb.maxAngularVelocity = 20f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            // Step 2: robots stay on the carpet and never tip. (Climbing in step 3 releases Y.)
            rb.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            if (d.intake != null && d.intake.fuelFromChute)
            {
                // Open-top hopper: a low base under the whole robot plus a ring of walls around the hopper, so FUEL
                // can fall in from above (a closed box would act like a lid)
                float floorZ = HopperFloor(d) * In;
                float hl = HopperLength(d) * In, hw = HopperWidth(d) * In;
                float baseH = floorZ - clearance, baseY = clearance + baseH / 2f;
                if (HasFloorIntake(d))
                {
                    // the base, with a notch in the front where the bumper gap is: FUEL rolls in under the intake roller
                    float front = hl / 2f, bayL = L / 2f - front, gap = IntakeGapHalf(d) * In, notch = NotchTop * In;
                    Wall(root, "base", new Vector3(0f, baseY, (front - L / 2f) / 2f), new Vector3(W, baseH, L / 2f + front));
                    float sideW = W / 2f - gap;
                    Wall(root, "base front left", new Vector3(-(gap + sideW / 2f), baseY, front + bayL / 2f), new Vector3(sideW, baseH, bayL));
                    Wall(root, "base front right", new Vector3(gap + sideW / 2f, baseY, front + bayL / 2f), new Vector3(sideW, baseH, bayL));
                    if (floorZ > notch)
                        Wall(root, "base over intake", new Vector3(0f, (notch + floorZ) / 2f, front + bayL / 2f), new Vector3(2f * gap, floorZ - notch, bayL));
                }
                else
                {
                    var col = root.AddComponent<BoxCollider>();
                    col.size = new Vector3(W, baseH, L);
                    col.center = new Vector3(0f, baseY, 0f);
                    col.sharedMaterial = bumperMaterial;
                }
                float wallH = H - floorZ, cy = floorZ + wallH / 2f;
                float ringL = (L - hl) / 2f, ringW = (W - hw) / 2f;
                Wall(root, "hopper front", new Vector3(0f, cy, (hl + ringL) / 2f), new Vector3(W, wallH, ringL));
                Wall(root, "hopper back", new Vector3(0f, cy, -(hl + ringL) / 2f), new Vector3(W, wallH, ringL));
                Wall(root, "hopper left", new Vector3(-(hw + ringW) / 2f, cy, 0f), new Vector3(ringW, wallH, hl));
                Wall(root, "hopper right", new Vector3((hw + ringW) / 2f, cy, 0f), new Vector3(ringW, wallH, hl));
            }
            else
            {
                var col = root.AddComponent<BoxCollider>();
                col.size = new Vector3(W, H - clearance, L);
                col.center = new Vector3(0f, clearance + (H - clearance) / 2f, 0f);
                col.sharedMaterial = bumperMaterial;
            }

            var drive = root.AddComponent<TankDrive>();
            drive.Configure(d);

            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            // the robot's own model (from its CAD) if it has one, with generated alliance bumpers; else the blockout
            if (RobotModel.Build(visual, d, mats, out var leftWheels, out var rightWheels))
            {
                RobotDetail.Bumpers(visual, d, mats.Get(alliance == "blue" ? "blue" : "red"));
                // the colour of a carried GEAR (RobotMechanisms looks for this)
                var gc = new GameObject("Gear marker");
                gc.transform.SetParent(visual, false);
                var mr = gc.AddComponent<MeshRenderer>(); mr.sharedMaterial = mats.Get("gear"); mr.enabled = false;
            }
            else BuildBlockout(visual, d, alliance, mats, out leftWheels, out rightWheels);

            var rv = root.AddComponent<RobotVisual>();
            rv.Init(drive, d.drivetrain.wheelDiameter * In / 2f, leftWheels, rightWheels);

            var info = root.AddComponent<RobotInfo>();
            info.definition = d;
            info.alliance = alliance;
            return root;
        }

        static void Wall(GameObject root, string side, Vector3 center, Vector3 size)
        {
            var go = new GameObject("Collider (" + side + ")");
            go.transform.SetParent(root.transform, false);
            var c = go.AddComponent<BoxCollider>();
            c.center = center; c.size = size;
            c.sharedMaterial = bumperMaterial;
        }

        /// <summary>The FUEL count the open-top hopper is sized for (intake.hopperBalls, else shooter.capacity), or 0.</summary>
        public static int DesignFill(RobotDefinition d)
        {
            if (d.intake != null && d.intake.hopperBalls > 0) return d.intake.hopperBalls;
            return d.shooter != null && d.shooter.capacity > 0 ? d.shooter.capacity : 0;
        }

        /// <summary>
        /// Height (in) of the open-top hopper's floor: set so that DesignFill FUEL, piled loosely (HopperPile), come up
        /// to the rim.
        /// </summary>
        public static float HopperFloor(RobotDefinition d)
        {
            int n = DesignFill(d);
            if (n <= 0) return 4.5f;
            var pile = HopperPile(d);
            float top = 0f;
            for (int i = 0; i < n && i < pile.Count; i++) top = Mathf.Max(top, pile[i].z + 2.5f);
            return Mathf.Clamp(d.frame.height - top, 4.5f, d.frame.height - 6f);
        }

        static readonly Dictionary<RobotDefinition, int> fits = new Dictionary<RobotDefinition, int>();

        /// <summary>
        /// How much FUEL the open-top hopper physically holds: every ball of the loose pile whose centre is still at or
        /// below the rim. More than that rolls off the top (no number in robot.json caps it).
        /// </summary>
        public static int PhysicalCapacity(RobotDefinition d)
        {
            if (fits.TryGetValue(d, out var c)) return c;
            if (DesignFill(d) <= 0) return 0;
            float rim = d.frame.height - HopperFloor(d);
            c = 0;
            foreach (var p in HopperPile(d)) if (p.z <= rim + 0.01f) c++;
            fits[d] = c;
            return c;
        }

        static readonly Dictionary<RobotDefinition, List<Vector3>> piles = new Dictionary<RobotDefinition, List<Vector3>>();

        /// <summary>
        /// Where the FUEL sits in a loose pile in this robot's hopper (in: forward, left, up from the hopper floor),
        /// bottom of the pile first: balls dropped one by one, each trying 100 random spots and settling where it comes
        /// to rest lowest, on the floor or on the balls below. Same pile every time for a given robot.
        /// </summary>
        public static List<Vector3> HopperPile(RobotDefinition d)
        {
            if (piles.TryGetValue(d, out var cached)) return cached;
            // twice the design fill: the first DesignFill balls are the same whatever the count (same seed, one by
            // one), and the rest show the pile heaping up to the rim before it overflows
            int count = Mathf.Max(60, 2 * DesignFill(d));
            float r = 2.5f, ax = HopperLength(d) / 2f - r, ay = HopperWidth(d) / 2f - r, touch = 4f * r * r;
            var rnd = new System.Random(2017);
            var pts = new List<Vector3>(count);
            for (int n = 0; n < count; n++)
            {
                Vector3 best = default; float bestZ = float.MaxValue;
                for (int k = 0; k < 100; k++)
                {
                    float x = ((float)rnd.NextDouble() * 2f - 1f) * ax, y = ((float)rnd.NextDouble() * 2f - 1f) * ay, z = r;
                    foreach (var q in pts)
                    {
                        float d2 = (x - q.x) * (x - q.x) + (y - q.y) * (y - q.y);
                        if (d2 < touch) z = Mathf.Max(z, q.z + Mathf.Sqrt(touch - d2));
                    }
                    if (z < bestZ) { bestZ = z; best = new Vector3(x, y, z); }
                }
                pts.Add(best);
            }
            piles[d] = pts;
            return pts;
        }

        // ---- floor intake in a gap in the front bumper
        /// <summary>The robot has an open-top hopper and a floor intake for FUEL (in a gap in the front bumper).</summary>
        public static bool HasFloorIntake(RobotDefinition d) => d.intake != null && d.intake.fuelFromChute && d.intake.fuelFromFloor;
        /// <summary>Half the width (in) of the gap in the front bumper where the intake is.</summary>
        public static float IntakeGapHalf(RobotDefinition d) => Mathf.Clamp(d.frame.width / 2f - 4f, 4f, 10f);
        /// <summary>Height (in) of the opening under the intake roller.</summary>
        public const float NotchTop = 8f;

        /// <summary>Inside of the open-top hopper (in): the frame less 2 in each side.</summary>
        public static float HopperLength(RobotDefinition d) => d.frame.length - 4f;
        public static float HopperWidth(RobotDefinition d) => d.frame.width - 4f;

        static Transform Part(Transform parent, Mesh mesh, Material mat, Vector3 center, Vector3 size, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        // Inches in robot-local terms: forward, right, up -> Unity local (x right, y up, z forward).
        static Vector3 V(float fwd, float right, float up) => new Vector3(right * In, up * In, fwd * In);

        static void BuildBlockout(Transform t, RobotDefinition d, string alliance, FieldMaterials mats,
            out List<Transform> leftWheels, out List<Transform> rightWheels)
        {
            float L = d.LengthWithBumpers, W = d.WidthWithBumpers, bt = d.bumpers.thickness;
            float fl = d.frame.length, fw = d.frame.width, H = d.frame.height;
            float bz = (d.bumpers.bottom + d.bumpers.top) / 2f, bh = d.bumpers.top - d.bumpers.bottom;
            var bump = mats.Get(alliance == "blue" ? "blue" : "red");
            var alu = mats.Get("alu");
            var dark = mats.Get("steelDark");
            var yellow = mats.Get("gear");
            var hub = mats.Get("steel");

            // Bumpers (alliance colour) around the frame perimeter, with a gap in the front for a floor intake
            RobotDetail.Bumpers(t, d, bump);
            // Frame, belly pan, superstructure
            Part(t, cubeMesh, alu, V(0, 0, 3.5f), V(fl, fw, 2f), "Frame");
            Part(t, cubeMesh, dark, V(0, 0, 2.6f), V(fl - 4f, fw - 6f, 0.25f), "Belly pan");
            if (d.intake != null && d.intake.fuelFromChute)
            {
                // Open-top FUEL hopper filling the frame: clear walls and an aluminium rim, so you can see the FUEL inside
                var clear = mats.Get("polyHopper");
                float hl = HopperLength(d), hw = HopperWidth(d), z0 = HopperFloor(d), hh = H - z0;
                if (z0 > 6f) Part(t, cubeMesh, dark, V(0, 0, (4.5f + z0) / 2f), V(hl, hw, z0 - 4.5f), "Electronics bay");
                Part(t, cubeMesh, alu, V(0, 0, z0 - 0.25f), V(hl, hw, 0.5f), "Hopper floor");
                Part(t, cubeMesh, clear, V(hl / 2f, 0, z0 + hh / 2f), V(0.25f, hw, hh), "Hopper wall front");
                Part(t, cubeMesh, clear, V(-hl / 2f, 0, z0 + hh / 2f), V(0.25f, hw, hh), "Hopper wall back");
                Part(t, cubeMesh, clear, V(0, hw / 2f, z0 + hh / 2f), V(hl, 0.25f, hh), "Hopper wall left");
                Part(t, cubeMesh, clear, V(0, -hw / 2f, z0 + hh / 2f), V(hl, 0.25f, hh), "Hopper wall right");
                Part(t, cubeMesh, alu, V(hl / 2f, 0, H - 0.5f), V(1f, hw + 1f, 1f), "Hopper rim");
                Part(t, cubeMesh, alu, V(-hl / 2f, 0, H - 0.5f), V(1f, hw + 1f, 1f), "Hopper rim");
                Part(t, cubeMesh, alu, V(0, hw / 2f, H - 0.5f), V(hl + 1f, 1f, 1f), "Hopper rim");
                Part(t, cubeMesh, alu, V(0, -hw / 2f, H - 0.5f), V(hl + 1f, 1f, 1f), "Hopper rim");
            }
            else
            {
                float towerH = Mathf.Max(2f, H - 4.5f - 1.5f);
                Part(t, cubeMesh, dark, V(-3f, 0, 4.5f + towerH / 2f), V(Mathf.Max(4f, fl - 10f), Mathf.Max(4f, fw - 8f), towerH), "Superstructure");
            }
            // Mechanisms: floor intake and conveyor (front), turret, gear slot. Without an intake, a yellow bar across
            // the front shows which way the robot faces.
            if (RobotFactory.HasFloorIntake(d)) RobotDetail.Intake(t, d, mats);
            else Part(t, cubeMesh, yellow, V(fl / 2f - 3f, 0, 9f), V(4f, fw - 2f, 3f), "Front marker");
            if (d.shooter != null && d.shooter.enabled) RobotDetail.Turret(t, d, mats);
            if (d.gear != null && d.gear.enabled) RobotDetail.GearSlot(t, d, mats);

            // Wheels (drop-centre look comes from the frame; wheels are evenly spaced along the frame)
            leftWheels = new List<Transform>(); rightWheels = new List<Transform>();
            int n = Mathf.Max(1, d.drivetrain.wheelsPerSide);
            float wd = d.drivetrain.wheelDiameter, span = Mathf.Max(0f, fl - wd - 2f);
            var tread = mats.Get("bin");
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < n; i++)
                {
                    float along = n == 1 ? 0f : -span / 2f + span * i / (n - 1);
                    var pivot = new GameObject(side < 0 ? $"Wheel L{i}" : $"Wheel R{i}").transform;
                    pivot.SetParent(t, false);
                    pivot.localPosition = V(along, side * d.drivetrain.trackWidth / 2f, wd / 2f);
                    // Unity's cylinder is 2 units tall along y with diameter 1: lay it on its side (axis = robot x)
                    var tire = Part(pivot, cylinderMesh, tread, Vector3.zero, new Vector3(wd * In, 0.75f * In, wd * In), "Tire");
                    tire.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    Part(pivot, cubeMesh, hub, Vector3.zero, new Vector3(1.6f * In, wd * 0.8f * In, 0.8f * In), "Spoke");
                    (side < 0 ? leftWheels : rightWheels).Add(pivot);
                }
            }
        }
    }
}
