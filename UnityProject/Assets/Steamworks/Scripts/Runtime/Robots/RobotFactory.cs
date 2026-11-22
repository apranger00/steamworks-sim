using System.Collections.Generic;
using SteamworksSim.Field;
using UnityEngine;

namespace SteamworksSim.Robots
{
    /// <summary>
    /// Builds a robot GameObject from a RobotDefinition (robot.json): rigidbody, collider, drivetrain and a
    /// generated blockout model (frame, alliance bumpers, wheels, superstructure). Custom models from the
    /// mod folder (model.glb) come later; the blockout is also what other players see without the model.
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
            const float clearance = 0.25f * In;

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
                const float floorZ = 4.5f * In;
                float hl = HopperLength(d) * In, hw = HopperWidth(d) * In;
                var col = root.AddComponent<BoxCollider>();
                col.size = new Vector3(W, floorZ - clearance, L);
                col.center = new Vector3(0f, clearance + (floorZ - clearance) / 2f, 0f);
                col.sharedMaterial = bumperMaterial;
                float wallH = H - floorZ, cy = floorZ + wallH / 2f;
                float ringL = (L - hl) / 2f, ringW = (W - hw) / 2f;
                Wall(root, "front", new Vector3(0f, cy, (hl + ringL) / 2f), new Vector3(W, wallH, ringL));
                Wall(root, "back", new Vector3(0f, cy, -(hl + ringL) / 2f), new Vector3(W, wallH, ringL));
                Wall(root, "left", new Vector3(-(hw + ringW) / 2f, cy, 0f), new Vector3(ringW, wallH, hl));
                Wall(root, "right", new Vector3((hw + ringW) / 2f, cy, 0f), new Vector3(ringW, wallH, hl));
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
            BuildBlockout(visual, d, alliance, mats, out var leftWheels, out var rightWheels);

            var rv = root.AddComponent<RobotVisual>();
            rv.Init(drive, d.drivetrain.wheelDiameter * In / 2f, leftWheels, rightWheels);

            var info = root.AddComponent<RobotInfo>();
            info.definition = d;
            info.alliance = alliance;
            return root;
        }

        static void Wall(GameObject root, string side, Vector3 center, Vector3 size)
        {
            var go = new GameObject("Collider (hopper " + side + ")");
            go.transform.SetParent(root.transform, false);
            var c = go.AddComponent<BoxCollider>();
            c.center = center; c.size = size;
            c.sharedMaterial = bumperMaterial;
        }

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

            // Bumpers (alliance colour) around the frame perimeter
            Part(t, cubeMesh, bump, V(0, -W / 2 + bt / 2, bz), V(L, bt, bh), "Bumper left");
            Part(t, cubeMesh, bump, V(0, W / 2 - bt / 2, bz), V(L, bt, bh), "Bumper right");
            Part(t, cubeMesh, bump, V(L / 2 - bt / 2, 0, bz), V(bt, fw, bh), "Bumper front");
            Part(t, cubeMesh, bump, V(-L / 2 + bt / 2, 0, bz), V(bt, fw, bh), "Bumper back");
            // Frame, belly pan, superstructure
            Part(t, cubeMesh, alu, V(0, 0, 3.5f), V(fl, fw, 2f), "Frame");
            Part(t, cubeMesh, dark, V(0, 0, 2.6f), V(fl - 4f, fw - 6f, 0.25f), "Belly pan");
            if (d.intake != null && d.intake.fuelFromChute)
            {
                // Open-top FUEL hopper filling the frame: clear walls and an aluminium rim, so you can see the FUEL inside
                var clear = mats.Get("polyHopper");
                float hl = HopperLength(d), hw = HopperWidth(d), z0 = 4.5f, hh = H - z0;
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
            // Yellow bar across the front so you can always tell which way the robot faces
            Part(t, cubeMesh, yellow, V(fl / 2f - 3f, 0, 9f), V(4f, fw - 2f, 3f), "Front marker");
            if (d.shooter != null && d.shooter.enabled)
            {
                var off = d.shooter.exitOffset != null && d.shooter.exitOffset.Length >= 2 ? d.shooter.exitOffset : new[] { 0f, 0f };
                // Unity's cylinder is 2 units tall: y scale = half the height
                Part(t, cylinderMesh, mats.Get("steel"), V(off[0], -off[1], H - 1.5f), new Vector3(10f * In, 1.5f * In, 10f * In), "Shooter turret");
            }

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
