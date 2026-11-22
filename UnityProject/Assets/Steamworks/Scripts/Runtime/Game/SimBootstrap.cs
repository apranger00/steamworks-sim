using System.Collections.Generic;
using SteamworksSim.Cameras;
using SteamworksSim.Field;
using SteamworksSim.InputHandling;
using SteamworksSim.Robots;
using UnityEngine;

namespace SteamworksSim.Game
{
    /// <summary>
    /// Step 2 driving sandbox: loads robot mods, spawns your robot at your alliance station, routes
    /// keyboard/gamepad input to it, and drives the cameras. Single player, no match yet (step 3).
    ///
    /// Keys: WASD drive, Shift precision, C camera, V flip chase, R reset, B defender,
    ///       F1 alliance, F2 station, F3 next robot, F5 reload robot files from disk.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class SimBootstrap : MonoBehaviour
    {
        public FieldBuilder field;
        public RobotCameraRig cameraRig;
        [Tooltip("Robot to start with (folder/id in StreamingAssets/Robots or Mods/Robots).")]
        public string robotId = "sim-6wd";
        public string alliance = "red";
        [Range(0, 2)] public int station = 1; // 0 boiler side, 1 centre, 2 loading side
        [Tooltip("Physics ticks per second (the prototype stepped at 240; 120 is plenty for PhysX).")]
        public float physicsRate = 120f;
        public bool showHud = true;

        // Player-station centres (spec Y, inches): boiler side, centre, loading side
        static readonly float[] StationY = { -88f, -16f, 87f };
        static readonly string[] StationName = { "boiler side", "centre", "loading side" };

        readonly PlayerInputReader reader = new PlayerInputReader();
        RobotLibrary.LoadResult library;
        RobotDefinition current;
        GameObject player, defender;
        TankDrive playerDrive;
        FieldMaterials ownMaterials;
        string message; float messageTime;

        FieldMaterials Mats
        {
            get
            {
                if (field != null && field.Materials != null) return field.Materials;
                return ownMaterials ??= new FieldMaterials(field != null ? field.litShader : null);
            }
        }

        int Side => alliance == "blue" ? 1 : -1;

        void Awake()
        {
            Time.fixedDeltaTime = 1f / Mathf.Max(30f, physicsRate);
            if (cameraRig == null && Camera.main != null) cameraRig = Camera.main.GetComponent<RobotCameraRig>();
            LoadLibrary(robotId);
        }

        void Start() => SpawnPlayer();

        void LoadLibrary(string preferId)
        {
            library = RobotLibrary.LoadAll();
            foreach (var p in library.problems) Debug.LogWarning("[Steamworks] Robot mods: " + p);
            current = library.robots.Find(r => r.id == preferId);
            if (current == null && library.robots.Count > 0) current = library.robots[0];
            if (current == null)
            {
                Debug.LogWarning("[Steamworks] No robot files found; using built-in defaults. Expected " + RobotLibrary.BuiltInDir);
                current = new RobotDefinition { id = "defaults", name = "Default robot" };
            }
            robotId = current.id;
        }

        // ---------------------------------------------------------------- spawning
        void SpawnPlayer()
        {
            if (player != null) Destroy(player);
            player = RobotFactory.Build(current, alliance, Mats, $"Player robot: {current.name}");
            playerDrive = player.GetComponent<TankDrive>();
            ResetPlayer();
            if (cameraRig != null) { cameraRig.robot = player.transform; cameraRig.Snap(); }
            Say($"{current.name} — {alliance.ToUpper()} {StationName[station]}");
        }

        void ResetPlayer()
        {
            // Bumpers touching the alliance wall, facing the field (prototype startPose)
            float x = Side * (326f - current.LengthWithBumpers / 2f - 0.5f);
            Place(player, x, StationY[station], Side < 0 ? 0f : 180f);
            if (cameraRig != null)
            {
                cameraRig.allianceSide = Side;
                cameraRig.stationY = StationY[station];
                cameraRig.Snap();
            }
        }

        /// <summary>Places a robot at spec (x, y) inches facing heading degrees (counter-clockwise from +X).</summary>
        static void Place(GameObject robot, float x, float y, float headingDeg)
        {
            var pos = Units.ToUnity(x, y, 0f);
            var rot = Quaternion.Euler(0f, 90f - headingDeg, 0f); // robot forward is local +z
            var rb = robot.GetComponent<Rigidbody>();
            robot.transform.SetPositionAndRotation(pos, rot);
            if (rb != null)
            {
                rb.position = pos; rb.rotation = rot;
                rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            }
        }

        void ToggleDefender()
        {
            if (defender != null) { Destroy(defender); defender = null; Say("Defender removed"); return; }
            var def = library.robots.Find(r => r.id == "sim-6wd") ?? current;
            string other = alliance == "red" ? "blue" : "red";
            defender = RobotFactory.Build(def, other, Mats, "Defender (idle)");
            defender.GetComponent<TankDrive>().idle = true;
            // Parked sideways in front of your station, on your side of the field
            Place(defender, Side * 60f, StationY[station], 90f);
            Say("Defender added: parked sideways, brakes on");
        }

        // ---------------------------------------------------------------- loop
        void Update()
        {
            var input = reader.Read();
            if (playerDrive != null) playerDrive.input = input;

            if (cameraRig != null)
            {
                if (reader.NextCamera) { cameraRig.NextMode(); Say("Camera: " + cameraRig.mode); }
                if (reader.FlipChase) { cameraRig.FlipChase(); Say(cameraRig.chaseReversed ? "Chase camera: in front" : "Chase camera: behind"); }
            }
            if (reader.ResetRobot) { ResetPlayer(); Say("Robot reset"); }
            if (reader.ToggleDefender) ToggleDefender();
            if (reader.NextAlliance) { alliance = alliance == "red" ? "blue" : "red"; SpawnPlayer(); if (defender != null) { ToggleDefender(); ToggleDefender(); } }
            if (reader.NextStation) { station = (station + 1) % 3; ResetPlayer(); Say("Start: " + StationName[station]); }
            if (reader.NextRobot && library.robots.Count > 0)
            {
                int i = library.robots.IndexOf(current);
                current = library.robots[(i + 1) % library.robots.Count];
                robotId = current.id;
                SpawnPlayer();
            }
            if (reader.ReloadRobots)
            {
                LoadLibrary(robotId);
                SpawnPlayer();
                Say(library.problems.Count == 0 ? "Robot files reloaded" : $"Robot files reloaded with {library.problems.Count} problem(s); see Console");
            }
        }

        void Say(string s) { message = s; messageTime = Time.unscaledTime; }

        // ---------------------------------------------------------------- HUD (temporary, IMGUI)
        GUIStyle box, label, big;

        void OnGUI()
        {
            if (!showHud || player == null) return;
            if (box == null)
            {
                box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, padding = new RectOffset(12, 12, 10, 10) };
                label = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
                big = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, richText = true };
            }
            float w = 300f, h = 236f, x = Screen.width - w - 16f, y = Screen.height - h - 16f;
            GUI.Box(new Rect(x, y, w, h), GUIContent.none, box);
            GUILayout.BeginArea(new Rect(x + 12f, y + 8f, w - 24f, h - 16f));
            float speedFt = playerDrive != null ? playerDrive.Speed / 0.3048f : 0f;
            GUILayout.Label($"<color=#d9a514>{speedFt:0.0}</color> <size=14>ft/s</size>", big);
            var spec = Units.ToSpec(player.transform.position);
            float heading = Mathf.Repeat(90f - player.transform.eulerAngles.y, 360f);
            GUILayout.Label($"{current.name}  <color=#8e99a3>({alliance}, {StationName[station]})</color>", label);
            GUILayout.Label($"Position {spec.x:0}, {spec.y:0} in   Heading {heading:0}°", label);
            GUILayout.Label($"Camera {cameraRig?.mode}{(cameraRig != null && cameraRig.chaseReversed && cameraRig.mode == RobotCameraRig.Mode.Chase ? " (front)" : "")}   Input {reader.LastSource}", label);
            GUILayout.Space(4f);
            GUILayout.Label("<color=#8e99a3>WASD / left stick + right stick  ·  Shift / LB precision\nC / Y camera  ·  V / L3 flip chase  ·  R / Back reset\nB defender  ·  F1 alliance  ·  F2 station  ·  F3 robot  ·  F5 reload</color>", label);
            if (!string.IsNullOrEmpty(message) && Time.unscaledTime - messageTime < 2.5f)
                GUILayout.Label($"<color=#ffffff>{message}</color>", label);
            GUILayout.EndArea();
        }
    }
}
