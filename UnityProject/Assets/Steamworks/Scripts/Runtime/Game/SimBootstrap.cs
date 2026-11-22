using SteamworksSim.Audio;
using SteamworksSim.Cameras;
using SteamworksSim.Field;
using SteamworksSim.InputHandling;
using SteamworksSim.Robots;
using SteamworksSim.Settings;
using SteamworksSim.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace SteamworksSim.Game
{
    /// <summary>
    /// Runs the game in one scene: main menu (field circling behind it), robot selection (your robot on its
    /// start line), the match (15 s AUTO + 2:15 TELEOP, both driver-controlled, with field sounds) and the
    /// pause menu. Menus and HUD are built at runtime, so the scene only needs this object, the field and
    /// the camera rig (Steamworks > Set Up Drive Test).
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class SimBootstrap : MonoBehaviour
    {
        public enum AppState { MainMenu, RobotSelect, Playing, Paused }

        public FieldBuilder field;
        public RobotCameraRig cameraRig;
        [Tooltip("Robot to start with (folder/id in StreamingAssets/Robots or Mods/Robots).")]
        public string robotId = "sim-6wd";
        public string alliance = "red";
        [Range(0, 2)] public int station = 1; // 0 boiler side, 1 centre, 2 loading side
        [Tooltip("Physics ticks per second (the prototype stepped at 240; 120 is plenty for PhysX).")]
        public float physicsRate = 120f;
        [Tooltip("Skip the menus and start a match straight away (quick testing in the editor).")]
        public bool skipMenus;

        // Player-station centres (spec Y, inches): boiler side, centre, loading side
        static readonly float[] StationY = { -88f, -16f, 87f };
        public static readonly string[] StationName = { "boiler side", "centre", "loading side" };
        const string Prefs = "steamworks.";

        public AppState State { get; private set; } = AppState.MainMenu;
        public MatchClock Clock { get; } = new MatchClock();
        public RobotLibrary.LoadResult Library => library;
        public RobotDefinition Current => current;

        readonly PlayerInputReader reader = new PlayerInputReader();
        RobotLibrary.LoadResult library;
        RobotDefinition current;
        GameObject player, defender;
        TankDrive playerDrive;
        FieldMaterials ownMaterials;
        MatchSounds sounds;
        GameMenus menus;
        MatchHud hud;
        RobotCameraRig.Mode playCamera = RobotCameraRig.Mode.Chase;
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

        // ---------------------------------------------------------------- startup
        void Awake()
        {
            Time.fixedDeltaTime = 1f / Mathf.Max(30f, physicsRate);
            Time.timeScale = 1f;
            if (cameraRig == null && Camera.main != null) cameraRig = Camera.main.GetComponent<RobotCameraRig>();
            if (field == null) field = FindAnyObjectByType<FieldBuilder>();

            GameControls.Init();
            GameSettings.Load();
            robotId = PlayerPrefs.GetString(Prefs + "robot", robotId);
            alliance = PlayerPrefs.GetString(Prefs + "alliance", alliance) == "blue" ? "blue" : "red";
            station = Mathf.Clamp(PlayerPrefs.GetInt(Prefs + "station", station), 0, 2);
            LoadLibrary(robotId);

            EnsureEventSystem();
            sounds = gameObject.AddComponent<MatchSounds>();
            Clock.Cue += OnCue;
            hud = gameObject.AddComponent<MatchHud>();
            hud.Init(this);
            menus = gameObject.AddComponent<GameMenus>();
            menus.Init(this);
        }

        void Start()
        {
            GameSettings.Apply();
            if (skipMenus) { SpawnPlayer(); StartMatchFromSelect(); }
            else ShowMainMenu();
        }

        void OnDestroy()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        void LoadLibrary(string preferId)
        {
            library = RobotLibrary.LoadAll();
            foreach (var p in library.problems) Debug.LogWarning("[Steamworks] Robot mods: " + p);
            current = library.robots.Find(r => r.id == preferId);
            if (current == null) current = library.robots.Find(r => r.id == "sim-6wd");
            if (current == null && library.robots.Count > 0) current = library.robots[0];
            if (current == null)
            {
                Debug.LogWarning("[Steamworks] No robot files found; using built-in defaults. Expected " + RobotLibrary.BuiltInDir);
                current = new RobotDefinition { id = "defaults", name = "Default robot" };
            }
            robotId = current.id;
        }

        // ---------------------------------------------------------------- app states (called by the menus)
        public void ShowMainMenu()
        {
            StopMatch();
            DestroyRobots();
            State = AppState.MainMenu;
            hud.SetVisible(false);
            if (cameraRig != null) cameraRig.ShowOrbit(new Vector3(0f, 0.4f, 0f), 15.5f, 20f, 24f, 4f);
            menus.Show(MenuPage.Main);
        }

        public void EnterRobotSelect()
        {
            StopMatch();
            if (defender != null) { Destroy(defender); defender = null; }
            State = AppState.RobotSelect;
            hud.SetVisible(false);
            SpawnPlayer();
            menus.Show(MenuPage.RobotSelect);
        }

        public void StartMatchFromSelect()
        {
            PlayerPrefs.SetString(Prefs + "robot", robotId);
            PlayerPrefs.SetString(Prefs + "alliance", alliance);
            PlayerPrefs.SetInt(Prefs + "station", station);
            PlayerPrefs.Save();
            if (player == null) SpawnPlayer();
            State = AppState.Playing;
            menus.Show(MenuPage.None);
            hud.SetVisible(true);
            if (cameraRig != null) cameraRig.SetMode(playCamera);
            RestartMatch();
        }

        /// <summary>Start (Enter): robot back on its start line, clock back to 2:30, match starts at once.</summary>
        public void RestartMatch()
        {
            sounds.StopAll();
            ResetPlayer();
            if (defender != null) PlaceDefender();
            Clock.Start();
        }

        public void Pause()
        {
            if (State != AppState.Playing) return;
            State = AppState.Paused;
            SetFrozen(true);
            menus.Show(MenuPage.Pause);
        }

        public void Resume()
        {
            if (State != AppState.Paused) return;
            State = AppState.Playing;
            SetFrozen(false);
            menus.Show(MenuPage.None);
        }

        public void ExitGame()
        {
            GameSettings.Save();
            PlayerPrefs.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void StopMatch()
        {
            SetFrozen(false);
            Clock.Stop();
            if (sounds != null) sounds.StopAll();
            // remember the camera the player was driving with, for the next match
            if (cameraRig != null && (State == AppState.Playing || State == AppState.Paused)) playCamera = cameraRig.mode;
        }

        void SetFrozen(bool frozen)
        {
            // Single player: pausing stops physics, the match clock and the field sounds
            Time.timeScale = frozen ? 0f : 1f;
            AudioListener.pause = frozen;
            if (cameraRig != null) cameraRig.AllowOrbitInput(!frozen);
        }

        public void PlayTestSound()
        {
            sounds.StopAll();
            sounds.Play(MatchCue.MatchStart);
        }

        // ---------------------------------------------------------------- robot selection
        public void SelectRobot(int index)
        {
            if (library.robots.Count == 0) return;
            current = library.robots[Mathf.Clamp(index, 0, library.robots.Count - 1)];
            robotId = current.id;
            SpawnPlayer();
        }

        public void SetAlliance(string a)
        {
            alliance = a == "blue" ? "blue" : "red";
            SpawnPlayer(); // bumper colour changes
        }

        public void SetStation(int s)
        {
            station = Mathf.Clamp(s, 0, 2);
            ResetPlayer();
        }

        public void ReloadRobots()
        {
            LoadLibrary(robotId);
            SpawnPlayer();
            Say(library.problems.Count == 0 ? "Robot files reloaded" : $"Robot files reloaded with {library.problems.Count} problem(s); see Console");
        }

        // ---------------------------------------------------------------- robots
        void DestroyRobots()
        {
            if (player != null) Destroy(player);
            if (defender != null) Destroy(defender);
            player = defender = null; playerDrive = null;
            if (cameraRig != null) cameraRig.robot = null;
        }

        void SpawnPlayer()
        {
            if (player != null) Destroy(player);
            player = RobotFactory.Build(current, alliance, Mats, $"Player robot: {current.name}");
            playerDrive = player.GetComponent<TankDrive>();
            if (cameraRig != null) cameraRig.robot = player.transform;
            ResetPlayer();
        }

        void ResetPlayer()
        {
            if (player == null) return;
            // Bumpers touching the alliance wall, facing the field (prototype startPose)
            float x = Side * (326f - current.LengthWithBumpers / 2f - 0.5f);
            Place(player, x, StationY[station], Side < 0 ? 0f : 180f);
            if (cameraRig == null) return;
            cameraRig.allianceSide = Side;
            cameraRig.stationY = StationY[station];
            if (State == AppState.RobotSelect) FocusPreview();
            else cameraRig.Snap();
        }

        void FocusPreview()
        {
            // Circle the robot, starting at a three-quarter front view
            var f = player.transform.forward;
            float yaw = Mathf.Atan2(-f.x, -f.z) * Mathf.Rad2Deg + 35f;
            cameraRig.ShowOrbit(player.transform.position + Vector3.up * 0.3f, 3.3f, yaw, 16f, 9f);
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
            PlaceDefender();
            Say("Defender added: parked sideways, brakes on");
        }

        // Parked sideways in front of your station, on your side of the field
        void PlaceDefender() => Place(defender, Side * 60f, StationY[station], 90f);

        // ---------------------------------------------------------------- loop
        void FixedUpdate()
        {
            if (State == AppState.Playing) Clock.Tick(Time.fixedDeltaTime);
        }

        void OnCue(MatchCue cue)
        {
            sounds.Play(cue);
            switch (cue)
            {
                case MatchCue.MatchStart: hud.Banner("AUTONOMOUS"); break;
                case MatchCue.AutoEnd: hud.Banner("TELEOPERATED"); break;
                case MatchCue.Endgame: hud.Banner("30 SECONDS: ENDGAME"); break;
                case MatchCue.MatchEnd: hud.Banner("MATCH OVER"); break;
            }
        }

        void Update()
        {
            var input = reader.Read();
            bool driving = State == AppState.Playing && Clock.Phase != MatchPhase.Over;
            if (playerDrive != null) playerDrive.input = driving ? input : RobotInput.Idle;
            if (menus.Rebinding) return;

            switch (State)
            {
                case AppState.Playing:
                    if (reader.Pause) { Pause(); return; }
                    if (reader.RestartMatch) RestartMatch();
                    if (cameraRig != null)
                    {
                        if (reader.NextCamera) { cameraRig.NextMode(); Say("Camera: " + cameraRig.mode); }
                        if (reader.FlipChase) { cameraRig.FlipChase(); Say(cameraRig.chaseReversed ? "Chase camera: in front" : "Chase camera: behind"); }
                    }
                    if (reader.ResetRobot) { ResetPlayer(); Say("Robot reset"); }
                    if (reader.ToggleDefender) ToggleDefender();
                    if (reader.ReloadRobots) ReloadRobots();
                    break;
                case AppState.Paused:
                    if (reader.Pause || reader.Cancel) menus.Back();
                    break;
                default:
                    if (reader.Cancel) menus.Back();
                    break;
            }
        }

        void Say(string s) { message = s; messageTime = Time.unscaledTime; }

        // ---------------------------------------------------------------- drive info (temporary, IMGUI)
        GUIStyle box, label, big;

        void OnGUI()
        {
            if (State != AppState.Playing || !GameSettings.ShowDriveInfo || player == null) return;
            if (box == null)
            {
                box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, padding = new RectOffset(12, 12, 10, 10) };
                label = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
                big = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, richText = true };
            }
            float w = 300f, h = 150f, x = Screen.width - w - 16f, y = Screen.height - h - 16f;
            GUI.Box(new Rect(x, y, w, h), GUIContent.none, box);
            GUILayout.BeginArea(new Rect(x + 12f, y + 8f, w - 24f, h - 16f));
            float speedFt = playerDrive != null ? playerDrive.Speed / 0.3048f : 0f;
            GUILayout.Label($"<color=#d9a514>{speedFt:0.0}</color> <size=14>ft/s</size>", big);
            var spec = Units.ToSpec(player.transform.position);
            float heading = Mathf.Repeat(90f - player.transform.eulerAngles.y, 360f);
            GUILayout.Label($"{current.name}  <color=#8e99a3>({alliance}, {StationName[station]})</color>", label);
            GUILayout.Label($"Position {spec.x:0}, {spec.y:0} in   Heading {heading:0}°", label);
            GUILayout.Label($"Camera {cameraRig?.mode}{(cameraRig != null && cameraRig.chaseReversed && cameraRig.mode == RobotCameraRig.Mode.Chase ? " (front)" : "")}   Input {reader.LastSource}", label);
            if (!string.IsNullOrEmpty(message) && Time.unscaledTime - messageTime < 2.5f)
                GUILayout.Label($"<color=#ffffff>{message}</color>", label);
            GUILayout.EndArea();
        }
    }
}
