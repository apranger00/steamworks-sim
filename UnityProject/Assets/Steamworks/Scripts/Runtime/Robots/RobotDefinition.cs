using System;
using System.Collections.Generic;
using UnityEngine;

namespace SteamworksSim.Robots
{
    /// <summary>
    /// A robot is data: robot.json in its own folder (optionally with a model.glb and icon.png).
    /// The game implements mechanism TYPES (tank drive, turret shooter, gear slot, rope climber...);
    /// a robot file chooses which ones it has and tunes them. New robots need no code. A new kind of
    /// mechanism (e.g. swerve, an intake) is added once in code and then any robot can use it.
    ///
    /// Units: lengths in inches, speeds in ft/s, accelerations in ft/s^2, angles in degrees,
    /// rates per second, mass in pounds. See docs/ROBOT_MODS.md.
    /// </summary>
    [Serializable]
    public class RobotDefinition
    {
        public int schema = 1;
        public string id;            // defaults to the folder name
        public string name = "Unnamed robot";
        public string author = "";
        public int team = 0;
        public string teamName = "";  // e.g. "KING TeC"; shown with the logo in robot selection
        public string logo = "";      // team logo image in the robot's folder (PNG or JPG), e.g. "logo.png"
        public string description = "";
        public string version = "1.0";

        public FrameSpec frame = new FrameSpec();
        public BumperSpec bumpers = new BumperSpec();
        public float mass = 140f; // lb, including bumpers and battery

        public DrivetrainSpec drivetrain = new DrivetrainSpec();
        public ShooterSpec shooter = new ShooterSpec();
        public DumpSpec lowGoalDump = new DumpSpec();
        public GearSpec gear = new GearSpec();
        public ClimberSpec climber = new ClimberSpec();
        public PreloadSpec preload = new PreloadSpec();
        public IntakeSpec intake = new IntakeSpec();
        public ModelSpec model = new ModelSpec();

        // Filled in by RobotLibrary
        [NonSerialized] public string folder;
        [NonSerialized] public string sourceJson;
        [NonSerialized] public string hash;        // identifies the exact definition across the network
        [NonSerialized] public bool builtIn;

        public float LengthWithBumpers => frame.length + 2f * bumpers.thickness;
        public float WidthWithBumpers => frame.width + 2f * bumpers.thickness;
    }

    [Serializable] public class FrameSpec { public float length = 32f, width = 28f, height = 24f; }
    [Serializable] public class BumperSpec
    {
        public float thickness = 3.25f, bottom = 2f, top = 7f;
        public float frontGap = 0f;   // in: an opening in the middle of the front bumper (floor intake); 0 = none (a FUEL
                                      // floor intake with an open-top hopper gets one automatically)
        public bool numbers = true;   // the team number on the bumpers (when team > 0)
    }

    [Serializable]
    public class DrivetrainSpec
    {
        public string type = "tank";        // tank (implemented); swerve, mecanum (planned)
        public float topSpeed = 12f;        // ft/s free speed
        public float acceleration = 50f;    // ft/s^2 from a stop; motor force fades to 0 at top speed
        public float traction = 1.1f;       // wheel-carpet friction coefficient (caps drive/pushing force)
        public float sideScrub = 0.85f;     // side-slip resistance as a fraction of traction
        public float brake = 0.9f;          // fraction of traction an idle robot resists pushes with
        public float maxTurnRate = 270f;    // deg/s
        public float trackWidth = 24f;      // in
        public float wheelDiameter = 6f;    // in
        public int wheelsPerSide = 3;
    }

    [Serializable]
    public class ShooterSpec
    {
        public bool enabled = true;
        public string type = "turret";      // turret: auto-aims at own high goal (implemented); fixed (planned)
        public float rate = 6f;             // FUEL per second
        public float exitHeight = 30f;      // in above the carpet
        public float[] exitOffset = { 0f, 0f }; // in from robot centre: forward, left
        public float maxExitSpeed = 40f;    // ft/s
        public float minRange = 2.5f;       // ft, horizontal to the goal centre
        public float maxRange = 25f;        // ft
        public float entryAngle = 55f;      // deg below horizontal at the goal opening
        public float aimError = 0.6f;       // deg, 1 sigma, yaw and pitch
        public float speedError = 1.2f;     // percent, 1 sigma
        public int capacity = 0;            // FUEL the robot holds (shooter and dump share it); 0 = what its open-top
                                            // hopper physically holds (intake.hopperBalls), or unlimited without one
    }

    [Serializable]
    public class DumpSpec
    {
        public bool enabled = true;
        public float rate = 10f;            // FUEL per second
        public float height = 22f;          // in, centre of the balls as they leave
        public float speed = 5.83f;         // ft/s out of the front
        public float width = 14f;           // in, spread across the opening
    }

    [Serializable]
    public class GearSpec
    {
        public bool enabled = true;
        public bool back = false;           // the slot is on the back of the robot (true) or the front
        public float slotOffset = 0f;       // in, offset of the gear slot to the robot's left of the centre line
        public float maxDistance = 4f;      // in between peg tip and the bumper face on the slot's side
        public float maxLateral = 3.5f;     // in off centre
        public float maxAngle = 20f;        // deg off square
        public float slider = 0f;           // in: a sideways gear slider moves the GEAR up to this far left or right to
                                            // line it up with the peg (adds to maxLateral)
        public float holdInset = 3.25f;     // in from the bumper face to the centre of the GEAR it carries
        public float holdHeight = 13f;      // in above the carpet, centre of the GEAR it carries
        public int capacity = 0;            // GEARS the robot holds; 0 = unlimited (testing)
    }

    /// <summary>Game pieces in the robot at the start of a match (2017 limits: 10 FUEL, 1 GEAR; capped by capacity).</summary>
    [Serializable] public class PreloadSpec { public int fuel = 10; public int gears = 1; }

    /// <summary>How the robot takes game pieces in.</summary>
    [Serializable]
    public class IntakeSpec
    {
        public bool fuelFromChute = true;   // FUEL dropped from a loading-station chute lands in an open-top hopper
        public float hopperDepth = 12f;     // in above the robot's top that counts as inside the hopper
        public int hopperBalls = 0;         // the open-top hopper is sized to hold about this many FUEL loosely piled to
                                            // the rim; it then takes what physically fits and the rest overflows
        public bool gearFromChute = true;   // the front gear slot takes a GEAR sliding out of a chute
        public bool fuelFromFloor = false;  // front intake picks FUEL up off the carpet
        public bool gearFromFloor = false;  // front intake picks GEARS up off the carpet
    }

    [Serializable]
    public class ClimberSpec
    {
        public bool enabled = true;
        public float rate = 12f;            // in/s winching up
        public float setback = 3f;          // in behind the front bumper face where the rope is gripped
        public float reachForward = 10f;    // in in front of the bumper where it can grab the rope
        public float reachLateral = 12f;    // in either side of the centre line
    }

    [Serializable]
    public class ModelSpec
    {
        public string file = "";            // "model.json" (tools/step_to_robot_model.py makes one from a STEP file);
                                            // empty = generated blockout from the frame
        public float scale = 1f;            // model units to metres (1 for metres, 0.0254 for inches)
        public float[] offset = { 0f, 0f, 0f };   // in: forward, left, up
        public float[] rotation = { 0f, 0f, 0f }; // deg about the robot's forward, left, up axes
    }

    public static class RobotRules
    {
        // FIRST STEAMWORKS 2017 manual R03: the robot, including BUMPERS and all extensions, must fit one of
        // two volumes: A = 36 x 40 x 24 in tall, or B = 30 x 32 x 36 in tall.
        // R04: at most 120 lb excluding BUMPERS and battery (roughly 150 lb with them; mass here includes them).
        public static readonly Vector3 VolumeA = new Vector3(40f, 36f, 24f); // long side, short side, height
        public static readonly Vector3 VolumeB = new Vector3(32f, 30f, 36f);
        public const float ApproxMaxMassWithBumpersAndBattery = 150f;

        public static bool FitsVolume(RobotDefinition d, Vector3 vol)
        {
            float a = Mathf.Max(d.LengthWithBumpers, d.WidthWithBumpers), b = Mathf.Min(d.LengthWithBumpers, d.WidthWithBumpers);
            return a <= vol.x + 1e-3f && b <= vol.y + 1e-3f && d.frame.height <= vol.z + 1e-3f;
        }

        /// <summary>Problems that make the file unusable (errors) or not competition-legal (warnings).</summary>
        public static void Validate(RobotDefinition d, List<string> errors, List<string> warnings)
        {
            if (d.schema != 1) errors.Add($"Unsupported schema {d.schema} (this game reads schema 1).");
            if (d.frame == null || d.bumpers == null || d.drivetrain == null) { errors.Add("Missing frame, bumpers or drivetrain section."); return; }
            if (d.frame.length <= 0 || d.frame.width <= 0 || d.frame.height <= 0) errors.Add("Frame dimensions must be positive.");
            if (d.mass <= 0) errors.Add("Mass must be positive.");
            if (d.drivetrain.type != "tank") errors.Add($"Drivetrain '{d.drivetrain.type}' is not available yet (supported: tank).");
            if (d.shooter != null && d.shooter.enabled && d.shooter.type != "turret") errors.Add($"Shooter type '{d.shooter.type}' is not available yet (supported: turret).");
            if (d.drivetrain.topSpeed <= 0 || d.drivetrain.acceleration <= 0 || d.drivetrain.traction <= 0) errors.Add("Drivetrain speed, acceleration and traction must be positive.");

            if (!FitsVolume(d, VolumeA) && !FitsVolume(d, VolumeB))
                warnings.Add($"Size {d.LengthWithBumpers:0.#} x {d.WidthWithBumpers:0.#} x {d.frame.height:0.#} in (with bumpers) fits neither 2017 volume (36 x 40 x 24 or 30 x 32 x 36).");
            if (d.mass > ApproxMaxMassWithBumpersAndBattery) warnings.Add($"Weight {d.mass:0.#} lb is heavier than a legal robot with bumpers and battery (about {ApproxMaxMassWithBumpersAndBattery} lb).");
            if (d.drivetrain.topSpeed > 20f) warnings.Add($"Top speed {d.drivetrain.topSpeed} ft/s is unusually fast for 2017.");
            if (d.drivetrain.traction > 1.6f) warnings.Add($"Traction {d.drivetrain.traction} is higher than any real wheel on carpet.");
        }
    }
}
