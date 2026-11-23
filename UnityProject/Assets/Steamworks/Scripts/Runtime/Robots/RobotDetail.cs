using SteamworksSim.Field;
using UnityEngine;

namespace SteamworksSim.Robots
{
    /// <summary>
    /// The mechanical look of a robot's mechanisms (the generated model, no physics): the front bumpers with a gap for
    /// a floor intake, the roller intake and conveyor that lift FUEL up into the hopper, a turret (bearing ring, side
    /// plates, flywheel, hood, motor) and the gear slot. Parts RobotMechanisms animates have fixed names:
    /// "Turret yaw", "Flywheel", "Hood", "Intake roller", "Conveyor roller 0..3", "Gear marker".
    ///
    /// Units are inches in robot-local terms (forward, right, up), converted with V().
    /// </summary>
    public static class RobotDetail
    {
        const float In = Units.Inch;
        static Mesh cube, cylinder;

        static void Meshes()
        {
            if (cube != null) return;
            var a = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = a.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(a);
            var b = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder = b.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(b);
        }

        public static Vector3 V(float fwd, float right, float up) => new Vector3(right * In, up * In, fwd * In);

        static Transform Node(Transform parent, string name, Vector3 at)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = at;
            return t;
        }

        /// <summary>A box: centre and size in (fwd, right, up) inches.</summary>
        public static Transform Box(Transform parent, Material m, Vector3 c, Vector3 size, string name, Quaternion? rot = null)
        {
            Meshes();
            var t = Node(parent, name, c);
            t.localScale = size;
            if (rot.HasValue) t.localRotation = rot.Value;
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = cube;
            t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = m;
            return t;
        }

        /// <summary>A cylinder along the robot's right (x), up (y) or forward (z) axis.</summary>
        public static Transform Cyl(Transform parent, Material m, Vector3 c, float diameter, float length, char axis, string name)
        {
            Meshes();
            var t = Node(parent, name, c);
            t.localScale = new Vector3(diameter * In, length / 2f * In, diameter * In);
            t.localRotation = axis == 'x' ? Quaternion.Euler(0f, 0f, 90f) : axis == 'z' ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = cylinder;
            t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = m;
            return t;
        }

        /// <summary>A flat strip from a to b (both in the same right-plane), `width` across (right) and `thick` thick.</summary>
        static Transform Plank(Transform parent, Material m, Vector3 a, Vector3 b, float width, float thick, string name)
        {
            var dir = b - a;
            var t = Box(parent, m, (a + b) / 2f, new Vector3(width * In, thick * In, dir.magnitude), name,
                        Quaternion.LookRotation(dir.normalized, Vector3.Cross(dir.normalized, Vector3.right)));
            return t;
        }

        /// <summary>A shaft along the robot's right axis with `count` wheels; each wheel has a dark mark so it visibly spins.</summary>
        static Transform Roller(Transform parent, string name, Vector3 c, float length, float wheelD, float wheelW, int count,
                                Material wheel, Material mark, Material shaft)
        {
            var pivot = Node(parent, name, c);
            Cyl(pivot, shaft, Vector3.zero, 0.5f, length, 'x', "Shaft");
            for (int i = 0; i < count; i++)
            {
                float x = count == 1 ? 0f : -length / 2f + wheelW / 2f + (length - wheelW) * i / (count - 1);
                Cyl(pivot, wheel, V(0, x, 0), wheelD, wheelW, 'x', "Wheel");
                Box(pivot, mark, V(0, x, wheelD / 2f - 0.12f), V(0.6f, wheelW + 0.04f, 0.3f), "Mark");
            }
            return pivot;
        }

        // ---------------------------------------------------------------- bumpers
        /// <summary>Alliance bumpers around the frame; with a floor intake the front bumper has a gap for it.</summary>
        public static void Bumpers(Transform t, RobotDefinition d, Material bump)
        {
            float L = d.LengthWithBumpers, W = d.WidthWithBumpers, bt = d.bumpers.thickness, fw = d.frame.width;
            float bz = (d.bumpers.bottom + d.bumpers.top) / 2f, bh = d.bumpers.top - d.bumpers.bottom;
            Box(t, bump, V(0, -W / 2 + bt / 2, bz), V(L, bt, bh), "Bumper left");
            Box(t, bump, V(0, W / 2 - bt / 2, bz), V(L, bt, bh), "Bumper right");
            Box(t, bump, V(-L / 2 + bt / 2, 0, bz), V(bt, fw, bh), "Bumper back");
            if (RobotFactory.HasFloorIntake(d))
            {
                float g = RobotFactory.IntakeGapHalf(d), seg = fw / 2f - g;
                Box(t, bump, V(L / 2 - bt / 2, -(g + seg / 2f), bz), V(bt, seg, bh), "Bumper front left");
                Box(t, bump, V(L / 2 - bt / 2, g + seg / 2f, bz), V(bt, seg, bh), "Bumper front right");
            }
            else Box(t, bump, V(L / 2 - bt / 2, 0, bz), V(bt, fw, bh), "Bumper front");
        }

        // ---------------------------------------------------------------- intake
        /// <summary>Where (fwd in) FUEL rides up the conveyor, between the rollers and the clear front panel.</summary>
        public const float ConveyorPathFwdFromHopper = 3.1f;

        /// <summary>
        /// Floor intake in the front bumper gap: an over-the-ball roller of green compliant wheels, side plates, a
        /// conveyor of black wheels up the front of the hopper with a clear front panel (you see the FUEL ride up),
        /// a deflector at the top that turns it into the hopper, belts, and the motor.
        /// </summary>
        public static void Intake(Transform t, RobotDefinition d, FieldMaterials mats)
        {
            float H = d.frame.height, L = d.LengthWithBumpers, front = RobotFactory.HopperLength(d) / 2f;
            float g = RobotFactory.IntakeGapHalf(d);
            Material plate = mats.Get("plate"), hole = mats.Get("hole"), green = mats.Get("wheelGreen"), black = mats.Get("bin"),
                     steel = mats.Get("steel"), belt = mats.Get("belt"), motor = mats.Get("motor"), cap = mats.Get("motorCap"),
                     clear = mats.Get("polyHopper"), dark = mats.Get("steelDark");
            float pfwd = front + ConveyorPathFwdFromHopper;  // ball path
            float rollFwd = front - 0.4f;                    // conveyor rollers, behind the path
            float panelFwd = pfwd + 2.6f;                    // clear front panel
            float inFwd = L / 2f + 0.35f;                    // intake roller, at the bumper line
            float plateFwd0 = front - 1.5f, plateFwd1 = L / 2f + 1.2f;

            // side plates with lightening pockets
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * (g + 0.4f);
                Box(t, plate, V((plateFwd0 + plateFwd1) / 2f, x, (2.5f + H + 3f) / 2f), V(plateFwd1 - plateFwd0, 0.25f, H + 0.5f), "Intake side plate");
                for (int k = 0; k < 3; k++)
                    for (int face = -1; face <= 1; face += 2)
                        Box(t, hole, V((plateFwd0 + plateFwd1) / 2f, x + face * 0.14f, 9.5f + k * 5.5f), V(plateFwd1 - plateFwd0 - 2.2f, 0.04f, 3.4f), "Pocket");
            }
            // rollers: intake (over the ball) and the conveyor wheels behind the FUEL path
            Roller(t, "Intake roller", V(inFwd, 0, 6.25f), 2f * g - 0.4f, 2.75f, 1.3f, 7, green, black, steel);
            float[] zs = { 9.5f, 13.5f, 17.5f, 21.5f };
            for (int k = 0; k < zs.Length; k++)
                Roller(t, $"Conveyor roller {k}", V(rollFwd, 0, zs[k]), 2f * g - 0.4f, 2.5f, 1f, 6, black, steel, steel);
            // clear front panel over the conveyor, and the deflector at the top that turns FUEL into the hopper
            Box(t, clear, V(panelFwd, 0, (9f + H + 1f) / 2f), V(0.2f, 2f * g, H - 8f), "Intake front panel");
            Plank(t, dark, V(panelFwd, 0, H + 1f), V(front - 1.5f, 0, H + 4.5f), 2f * g, 0.25f, "Intake deflector");
            Cyl(t, steel, V(panelFwd, 0, 9f), 0.5f, 2f * g, 'x', "Standoff");
            Cyl(t, steel, V(panelFwd, 0, H + 1f), 0.5f, 2f * g, 'x', "Standoff");
            // belts and pulleys on the outside of the left plate
            float bx = -(g + 0.85f);
            var axles = new[] { V(inFwd, bx, 6.25f), V(rollFwd, bx, zs[0]), V(rollFwd, bx, zs[1]), V(rollFwd, bx, zs[2]), V(rollFwd, bx, zs[3]) };
            for (int i = 0; i < axles.Length; i++)
            {
                Cyl(t, black, axles[i], 1.7f, 0.7f, 'x', "Pulley");
                if (i + 1 < axles.Length) Plank(t, belt, axles[i], axles[i + 1], 0.6f, 1.75f, "Belt");
            }
            // motor on the right, driving the conveyor
            Cyl(t, motor, V(front + 3f, g + 2.1f, 13.5f), 2.3f, 3f, 'x', "Intake motor");
            Cyl(t, cap, V(front + 3f, g + 3.75f, 13.5f), 2.3f, 0.3f, 'x', "Motor cap");
        }

        /// <summary>Path (fwd, left, up in) a FUEL takes from the intake mouth up the conveyor and over into the hopper.</summary>
        public static Vector3[] IntakePath(RobotDefinition d, float left)
        {
            float H = d.frame.height, L = d.LengthWithBumpers, front = RobotFactory.HopperLength(d) / 2f;
            float pfwd = front + ConveyorPathFwdFromHopper;
            return new[]
            {
                new Vector3(L / 2f, left, 2.5f),
                new Vector3(pfwd, left * 0.6f, 6.5f),
                new Vector3(pfwd, left * 0.3f, H - 1f),
                new Vector3(pfwd - 1f, left * 0.2f, H + 2.75f),
                new Vector3(front - 4f, left * 0.1f, H + 3.25f),
            };
        }

        // ---------------------------------------------------------------- turret
        /// <summary>
        /// Turret on a toothed bearing ring: side plates, a two-wheel flywheel, a curved hood that tilts with the shot,
        /// an indexer roller, the motor and standoffs. "Turret yaw" turns to the goal; its +z (forward) is the shot.
        /// </summary>
        public static void Turret(Transform t, RobotDefinition d, FieldMaterials mats)
        {
            var off = d.shooter.exitOffset != null && d.shooter.exitOffset.Length >= 2 ? d.shooter.exitOffset : new[] { 0f, 0f };
            float H = d.frame.height;
            Material plate = mats.Get("plate"), hole = mats.Get("hole"), dark = mats.Get("steelDark"), white = mats.Get("hdpe"),
                     black = mats.Get("bin"), steel = mats.Get("steel"), motor = mats.Get("motor"), cap = mats.Get("motorCap");
            var c = V(off[0], -off[1], H);
            // bearing ring with gear teeth (fixed to the robot)
            Cyl(t, dark, c + V(0, 0, 0.3f), 11f, 0.6f, 'y', "Turret bearing");
            for (int i = 0; i < 32; i++)
            {
                float a = i * Mathf.PI * 2f / 32f;
                var tooth = Box(t, plate, c + V(Mathf.Cos(a) * 5.65f, Mathf.Sin(a) * 5.65f, 0.3f), V(0.55f, 0.5f, 0.6f), "Tooth");
                tooth.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
            }
            var yaw = Node(t, "Turret yaw", c + V(0, 0, 0.6f));
            Box(yaw, plate, V(0, 0, 0.125f), V(9f, 8f, 0.25f), "Turret base");
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * 3.1f;
                Box(yaw, plate, V(0, x, 2.75f), V(8.5f, 0.25f, 5f), "Turret plate");
                Box(yaw, plate, V(-1f, x, 7f), V(6f, 0.25f, 3.5f), "Turret plate");
                for (int face = -1; face <= 1; face += 2)
                {
                    Box(yaw, hole, V(2.2f, x + face * 0.14f, 2.6f), V(2.6f, 0.04f, 2.6f), "Pocket");
                    Box(yaw, hole, V(-2.4f, x + face * 0.14f, 2.6f), V(2.6f, 0.04f, 2.6f), "Pocket");
                }
            }
            // flywheel (two white wheels) and the hood arc around it
            var fly = Roller(yaw, "Flywheel", V(-0.5f, 0, 5f), 5.4f, 4f, 1.7f, 2, white, black, steel);
            fly.name = "Flywheel";
            var hood = Node(yaw, "Hood", V(-0.5f, 0, 5f));
            const float R = 6.2f;
            for (int k = 0; k < 6; k++)
            {
                float a0 = (100f - k * 15f) * Mathf.Deg2Rad, a1 = (100f - (k + 1) * 15f) * Mathf.Deg2Rad;
                Plank(hood, dark, V(Mathf.Cos(a0) * R, 0, Mathf.Sin(a0) * R), V(Mathf.Cos(a1) * R, 0, Mathf.Sin(a1) * R), 5.6f, 0.3f, "Hood panel");
            }
            for (int s = -1; s <= 1; s += 2)
                Plank(hood, plate, V(Mathf.Cos(100f * Mathf.Deg2Rad) * R, s * 2.9f, Mathf.Sin(100f * Mathf.Deg2Rad) * R),
                      V(Mathf.Cos(10f * Mathf.Deg2Rad) * R, s * 2.9f, Mathf.Sin(10f * Mathf.Deg2Rad) * R), 0.25f, 1.2f, "Hood rib");
            // indexer, motor and standoffs
            Roller(yaw, "Indexer", V(-3.6f, 0, 1.9f), 5.6f, 2f, 1f, 3, black, steel, steel);
            Cyl(yaw, motor, V(-0.5f, 4.8f, 5f), 2.4f, 3f, 'x', "Flywheel motor");
            Cyl(yaw, cap, V(-0.5f, 6.45f, 5f), 2.4f, 0.3f, 'x', "Motor cap");
            Cyl(yaw, steel, V(3.6f, 0, 1.2f), 0.5f, 6.2f, 'x', "Standoff");
            Cyl(yaw, steel, V(-3.8f, 0, 8.3f), 0.5f, 6.2f, 'x', "Standoff");
        }

        // ---------------------------------------------------------------- gear slot
        /// <summary>The gear slot: two guide plates with yellow tips, a ledge and a crossbar, on the front or back.</summary>
        public static void GearSlot(Transform t, RobotDefinition d, FieldMaterials mats)
        {
            var G = d.gear;
            float sgn = G.back ? -1f : 1f, L = d.LengthWithBumpers;
            float f = sgn * (L / 2f - 3.25f), x = -G.slotOffset;
            Material plate = mats.Get("plate"), dark = mats.Get("steelDark"), steel = mats.Get("steel"), yellow = mats.Get("gear");
            for (int s = -1; s <= 1; s += 2)
            {
                Box(t, plate, V(f, x + s * 6.4f, 13.25f), V(3f, 0.25f, 13.5f), "Gear guide");
                Box(t, yellow, V(f, x + s * 6.4f, 20.2f), V(3.2f, 0.45f, 0.8f), s < 0 ? "Gear marker" : "Gear guide tip");
            }
            Box(t, dark, V(f, x, 7f), V(3f, 12.8f, 0.4f), "Gear ledge");
            Cyl(t, steel, V(f - sgn * 1.2f, x, 20.3f), 0.6f, 12.8f, 'x', "Gear crossbar");
        }
    }
}
