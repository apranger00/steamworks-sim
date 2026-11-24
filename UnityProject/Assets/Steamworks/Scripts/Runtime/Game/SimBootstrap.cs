using SteamworksSim.Audio;
using SteamworksSim.Cameras;
using SteamworksSim.Field;
using SteamworksSim.InputHandling;
using SteamworksSim.Robots;
using SteamworksSim.Settings;
using SteamworksSim.UI;
using System.Collections.Generic;
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
        public enum AppState { MainMenu, RobotSelect, SetAuto, Playing, Paused, Results }

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
        public MatchScore Score { get; } = new MatchScore();

        [Tooltip("Seconds after 0:00 before the final score slides in.")]
        public float resultsDelay = 2f;
        public RobotLibrary.LoadResult Library => library;
        public RobotDefinition Current => current;

        readonly PlayerInputReader reader = new PlayerInputReader();
        RobotLibrary.LoadResult library;
        RobotDefinition current;
        GameObject player, defender;
        RobotDefinition defenderDef;
        RobotMechanisms playerMech, defenderMech;
        float resultsAt = -1f;
        TargetsFile targets;
        MatchRules rules;
        FuelPool fuel;
        RopeSystem ropes;
        FieldAnimator animator;
        HopperSystem hoppers;
        GearPool gearPool;
        LoadingStations loading;
        public LoadingStations Loading => loading;
        public MatchRules Rules => rules;
        public RobotMechanisms PlayerMechanisms => playerMech;
        TankDrive playerDrive;
        FieldMaterials ownMaterials;
        MatchSounds sounds;
        /// <summary>The referees (FOULS, TECH FOULS, free climbs, pin counts).</summary>
        public Violations Referee { get; } = new Violations();
        TitleMusic music;
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
            LoadAutoStart();
            LoadLibrary(robotId);

            EnsureEventSystem();
            if (FindAnyObjectByType<AudioListener>() == null)
            {
                var ears = Camera.main != null ? Camera.main.gameObject : gameObject;
                ears.AddComponent<AudioListener>();
                Debug.LogWarning("[Steamworks] The scene had no Audio Listener, so nothing could be heard; added one to " + ears.name + ".");
            }
            sounds = gameObject.AddComponent<MatchSounds>();
            music = gameObject.AddComponent<TitleMusic>();
            Clock.Cue += OnCue;

            // Scoring: rules (port of the prototype's game.js), FUEL, ROPES, moving field parts
            targets = TargetsFile.Load();
            rules = new MatchRules(targets) { playoffs = true }; // playoff scoring: 40 kPa +20, four ROTORS +100
            rules.Log += (al, text) => Debug.Log($"[Match {Clock.Phase}] {(al != null ? al.ToUpper() + ": " : "")}{text}");
            fuel = gameObject.AddComponent<FuelPool>();
            fuel.GoalEntered = (al, goal) =>
                (Clock.Phase == MatchPhase.Auto || Clock.Phase == MatchPhase.AutoPause || Clock.Phase == MatchPhase.Teleop) && rules.FuelLoaded(al, goal);
            ropes = gameObject.AddComponent<RopeSystem>();
            animator = gameObject.AddComponent<FieldAnimator>();
            hoppers = gameObject.AddComponent<HopperSystem>();
            gearPool = gameObject.AddComponent<GearPool>();
            loading = gameObject.AddComponent<LoadingStations>();
            // every FUEL a boiler counts goes on to the other alliance's loading station
            rules.FuelCounted += al => loading.AddFuel(al == "red" ? "blue" : "red", 1);
            hud = gameObject.AddComponent<MatchHud>();
            hud.Init(this);
            menus = gameObject.AddComponent<GameMenus>();
            menus.Init(this);
            // The only messages during a match: a ROTOR starting, and 40 kPa
            rules.RotorStarted += (al, k) => hud.Toast($"{al.ToUpper()} ROTOR {k + 1} IS ACTIVE", AllianceTint(al));
            rules.PressureReached += al => hud.Toast($"{al.ToUpper()} REACHED 40 kPa", AllianceTint(al));
            Referee.Called += c => hud.ShowViolation(c);
        }

        static Color AllianceTint(string al) => al == "red" ? new Color(1f, 0.45f, 0.42f) : new Color(0.45f, 0.65f, 1f);

        void Start()
        {
            // the field has been built by now (FieldBuilder.Awake), so its materials and moving parts exist
            fuel.Init(targets, Mats.Get("fuel"));
            ropes.Init(RopesFile.Load(), targets, Mats.Get("rope"));
            animator.Init(field, targets, Mats);
            hoppers.Init(field, targets, fuel);
            gearPool.Init(Mats.Get("gear"));
            loading.Init(targets, fuel, gearPool, Mats);
            fuel.Catch = (p, v) => { foreach (var m in Mechs()) if (m.TryCatchFuel(p, v)) return true; return false; };
            gearPool.Catch = p => { foreach (var m in Mechs()) if (m.TryCatchGear(p)) return true; return false; };
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
            RemoveDummy();
            State = AppState.RobotSelect;
            hud.SetVisible(false);
            SpawnPlayer(true);
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

        /// <summary>Start (Enter): robot back on its start line, clock back to 2:30, 3-2-1 countdown, go.</summary>
        public void RestartMatch()
        {
            sounds.StopAll();
            Score.Clear();
            rules.Reset();
            Referee.Reset();
            hud.ClearViolations();
            fuel.Clear();
            gearPool.Clear();
            hoppers.Refill();
            ropes.StowAll(); // the PILOTS drop them with 30 s left
            resultsAt = -1f;
            ResetPlayer();
            EnsureDummy();
            PlaceDefender();
            foreach (var m in Mechs()) m.Reload();
            loading.Reset(Mechs()); // station stock; the lanes get the preloads the robots didn't take
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

        /// <summary>From the final score: same robot, same start, new match.</summary>
        public void PlayAgain()
        {
            State = AppState.Playing;
            menus.Show(MenuPage.None);
            hud.SetVisible(true);
            if (cameraRig != null) cameraRig.SetMode(playCamera);
            RestartMatch();
        }

        void ShowResults()
        {
            resultsAt = -1f;
            if (cameraRig != null) playCamera = cameraRig.mode;
            State = AppState.Results;
            hud.SetVisible(false);
            Score.red.robots.Clear(); Score.blue.robots.Clear();
            if (player != null) Score.Of(alliance).robots.Add(RobotLabel(current));
            if (defender != null && defenderDef != null) Score.Other(alliance).robots.Add(RobotLabel(defenderDef));
            // the field stays as it was at 0:00, circling slowly behind the score
            if (cameraRig != null) cameraRig.ShowOrbit(new Vector3(0f, 0.4f, 0f), 15.5f, 20f, 30f, 3f);
            menus.ShowResults(Score);
        }

        static string RobotLabel(RobotDefinition d) => d.team > 0 ? d.team.ToString() : d.name;

        void StopMatch()
        {
            resultsAt = -1f;
            SetFrozen(false);
            if (fuel != null) fuel.Clear();
            if (ropes != null) ropes.HangAll();
            if (hoppers != null) hoppers.Refill();
            if (gearPool != null) gearPool.Clear();
            if (loading != null) loading.Reset(Mechs()); // lanes stocked for the next match
            rules?.Reset();
            Clock.Stop();
            if (sounds != null) sounds.StopAll();
            // remember the camera the player was driving with, for the next match
            if (cameraRig != null && (State == AppState.Playing || State == AppState.Paused)) playCamera = cameraRig.mode;
            Score.Clear();
        }

        void SetFrozen(bool frozen)
        {
            // Single player: pausing stops physics, the match clock and the field sounds
            Time.timeScale = frozen ? 0f : 1f;
            AudioListener.pause = frozen;
            if (cameraRig != null) cameraRig.AllowOrbitInput(!frozen);
        }

        public string SoundStatus => (sounds != null ? sounds.StatusText : "") + (music != null ? "\n" + music.StatusText : "");

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
            SpawnPlayer(true); // the old robot drives off, the new one drives in
        }

        public void SetAlliance(string a)
        {
            alliance = a == "blue" ? "blue" : "red";
            SpawnPlayer(); // bumper colour changes
        }

        public void SetStation(int s)
        {
            station = Mathf.Clamp(s, 0, 2);
            CustomStart = false; // picking a station replaces the start set with SET AUTO
            PlayerPrefs.SetInt(Prefs + "autoSet", 0);
            ResetPlayer();
        }

        // ---------------------------------------------------------------- SET AUTO (starting position)
        /// <summary>The match starts from the position saved with SET AUTO (else from the station's default).</summary>
        public bool CustomStart { get; private set; }
        public bool HasSavedAuto => PlayerPrefs.HasKey(Prefs + "autoD");
        // saved in red's frame (mirrored for blue): distance of the robot centre from the alliance wall, Y, heading
        float autoD, autoY, autoH;
        const float AllianceWallX = 326f, WallYMin = -124f, WallYMax = 124.5f, WallTouch = 1.5f;

        void LoadAutoStart()
        {
            CustomStart = PlayerPrefs.GetInt(Prefs + "autoSet", 0) != 0;
            autoD = PlayerPrefs.GetFloat(Prefs + "autoD", 20f);
            autoY = PlayerPrefs.GetFloat(Prefs + "autoY", 0f);
            autoH = PlayerPrefs.GetFloat(Prefs + "autoH", 0f);
        }

        public void UseCustomStart()
        {
            if (PlayerPrefs.GetFloat(Prefs + "autoD", -1f) < 0f) return; // nothing saved yet
            CustomStart = true;
            PlayerPrefs.SetInt(Prefs + "autoSet", 1);
            ResetPlayer();
        }

        /// <summary>2017: every robot starts touching its ALLIANCE WALL. Any part of it counts, facing any direction
        /// (front, back, a side or a corner).</summary>
        public bool AutoStartValid =>
            playerMech != null && playerMech.TouchingAllianceWall(alliance, AllianceWallX, WallYMin, WallYMax, WallTouch);

        /// <summary>Robot selection > SET AUTO: a bird's-eye view of your end of the field; drive the robot where it should start.</summary>
        public void EnterSetAuto()
        {
            if (player == null) return;
            State = AppState.SetAuto;
            RemoveDummy();
            ResetPlayer();
            if (playerMech != null) playerMech.enabledByMatch = false;
            if (cameraRig != null) cameraRig.ShowOverhead(Units.ToUnity(Side * (AllianceWallX - 125f), 0f, 0f), 6.2f, Side < 0 ? 90f : -90f);
            menus.Show(MenuPage.SetAuto);
        }

        /// <summary>SAVE: keeps the position only if the robot touches its alliance wall. False otherwise (nothing saved).</summary>
        public bool SaveAuto()
        {
            if (State != AppState.SetAuto || !AutoStartValid) return false;
            var p = Units.ToSpec(player.transform.position);
            var f = player.transform.forward;
            float h = Mathf.Atan2(f.z, f.x) * Mathf.Rad2Deg; // spec heading, CCW from +X
            if (Side < 0) { autoD = p.x + AllianceWallX; autoY = p.y; autoH = h; }
            else { autoD = AllianceWallX - p.x; autoY = p.y; autoH = 180f - h; } // mirrored into red's frame
            PlayerPrefs.SetFloat(Prefs + "autoD", autoD); PlayerPrefs.SetFloat(Prefs + "autoY", autoY); PlayerPrefs.SetFloat(Prefs + "autoH", autoH);
            PlayerPrefs.SetInt(Prefs + "autoSet", 1); PlayerPrefs.Save();
            CustomStart = true;
            LeaveSetAuto();
            return true;
        }

        /// <summary>CANCEL: back to robot selection, nothing changed.</summary>
        public void CancelSetAuto() { if (State == AppState.SetAuto) LeaveSetAuto(); }

        /// <summary>RESET: the robot back where the match would start it now.</summary>
        public void ResetSetAuto() { if (State == AppState.SetAuto) PlaceStart(); }

        void LeaveSetAuto()
        {
            State = AppState.RobotSelect;
            menus.Show(MenuPage.RobotSelect);
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
            player = defender = null; playerDrive = null; playerMech = defenderMech = null;
            if (cameraRig != null) cameraRig.robot = null;
        }

        /// <summary>Builds the player's robot. With driveIn in robot selection, the old robot drives off and the new
        /// one drives onto the showcase spot.</summary>
        void SpawnPlayer(bool driveIn = false)
        {
            bool show = driveIn && State == AppState.RobotSelect;
            if (player != null)
            {
                // only a robot parked on the showcase spot drives off; one still driving in (or anywhere else on the
                // field) is just removed, so nothing goes racing across the background
                var at = Units.ToSpec(player.transform.position);
                bool parked = !PreviewDrive.Busy(player) && new Vector2(at.x, at.y).magnitude < 24f;
                if (show && parked) { player.name += " (old)"; PreviewDrive.Leave(player, PreviewExit); }
                else Destroy(player);
            }
            player = RobotFactory.Build(current, alliance, Mats, $"Player robot: {current.name}");
            playerDrive = player.GetComponent<TankDrive>();
            playerMech = player.AddComponent<RobotMechanisms>();
            playerMech.Init(current, alliance, 1, "Your robot", fuel, ropes, () => rules, targets);
            playerMech.Reload();
            if (cameraRig != null) cameraRig.robot = player.transform;
            ResetPlayer();
            if (show) PreviewDrive.Arrive(player, PreviewEntry, PreviewSpot, PreviewHeading);
        }

        // Robot selection: the robot parked in the middle of the field, three-quarters on to a fixed camera
        static readonly Vector2 PreviewSpot = Vector2.zero, PreviewEntry = new Vector2(-190f, 70f), PreviewExit = new Vector2(190f, 60f);
        const float PreviewHeading = -55f;
        static readonly Vector3 PreviewCamera = new Vector3(0f, -80f, 40f), PreviewLook = new Vector3(-27f, 0f, 13f); // spec in

        void ResetPlayer()
        {
            if (player == null) return;
            // still driving in from robot selection? stop that first, or it carries on to the showcase spot
            if (State != AppState.RobotSelect) PreviewDrive.Stop(player);
            if (playerMech != null) playerMech.ResetState(); // off the rope, back on the carpet
            if (State == AppState.RobotSelect) Place(player, PreviewSpot.x, PreviewSpot.y, PreviewHeading);
            else PlaceStart();
            if (cameraRig == null || State == AppState.SetAuto) return;
            cameraRig.allianceSide = Side;
            cameraRig.stationY = StationY[station];
            if (State == AppState.RobotSelect) FocusPreview();
            else cameraRig.Snap();
        }

        /// <summary>The start: the SET AUTO position if one is saved and chosen, else bumpers against the alliance wall at
        /// the station, facing the field (prototype startPose).</summary>
        void PlaceStart()
        {
            if (CustomStart)
            {
                Place(player, Side < 0 ? autoD - AllianceWallX : AllianceWallX - autoD, autoY, Side < 0 ? autoH : 180f - autoH);
                return;
            }
            float x = Side * (AllianceWallX - current.LengthWithBumpers / 2f - 0.5f);
            Place(player, x, StationY[station], Side < 0 ? 0f : 180f);
        }

        void FocusPreview()
        {
            // a fixed shot of the showcase spot; the robot sits right of centre, clear of the selection panel
            cameraRig.ShowFixed(Units.ToUnity(PreviewCamera), Units.ToUnity(PreviewLook));
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
                if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            }
        }

        // ---------------------------------------------------------------- dummy opponent (for trying the fouls)
        /// <summary>Where the dummy opponent stands. B cycles through them (and off).</summary>
        public enum DummySpot { Midfield, Launchpad, RetrievalZone, Guardrail, Rope, Off }
        DummySpot dummySpot = DummySpot.Off; // off unless B brings it back (user, 2026-11-24)
        static readonly string[] DummyNames =
        {
            "parked in midfield", "in its LAUNCHPAD beside its airship (A04)", "in its RETRIEVAL ZONE (G13)",
            "against the guardrail, to pin (G11)", "under its ROPE: climbs when the ropes drop (G07, G20)", "off",
        };

        /// <summary>B: next dummy spot (creating or removing the dummy as needed).</summary>
        void ToggleDefender()
        {
            dummySpot = (DummySpot)(((int)dummySpot + 1) % DummyNames.Length);
            PlayerPrefs.SetInt(Prefs + "dummy2", (int)dummySpot);
            if (dummySpot == DummySpot.Off) { RemoveDummy(); Say("Dummy opponent removed"); return; }
            if (defender == null) CreateDummy();
            PlaceDefender();
            Say("Dummy opponent " + DummyNames[(int)dummySpot]);
        }

        void RemoveDummy() { if (defender != null) Destroy(defender); defender = null; defenderMech = null; }

        void EnsureDummy()
        {
            dummySpot = (DummySpot)Mathf.Clamp(PlayerPrefs.GetInt(Prefs + "dummy2", (int)DummySpot.Off), 0, DummyNames.Length - 1);
            if (dummySpot == DummySpot.Off) { RemoveDummy(); return; }
            if (defender == null) CreateDummy();
        }

        void CreateDummy()
        {
            var def = library.robots.Find(r => r.id == "sim-6wd") ?? current;
            string other = alliance == "red" ? "blue" : "red";
            defenderDef = def;
            defender = RobotFactory.Build(def, other, Mats, "Dummy opponent");
            defender.GetComponent<TankDrive>().idle = true;
            defenderMech = defender.AddComponent<RobotMechanisms>();
            defenderMech.Init(def, other, 2, "Defender", fuel, ropes, () => rules, targets);
            defenderMech.participates = false; // a dummy: no preload, no loading station
        }

        /// <summary>Puts the dummy (brakes on, pushable) at its spot; all spots are the dummy's alliance's areas.</summary>
        void PlaceDefender()
        {
            if (defender == null || defenderDef == null) return;
            if (defenderMech != null) defenderMech.ResetState();
            string opp = alliance == "red" ? "blue" : "red";
            var T = targets != null ? targets.Of(opp) : null;
            float halfL = defenderDef.LengthWithBumpers / 2f, halfW = defenderDef.WidthWithBumpers / 2f;
            switch (dummySpot)
            {
                case DummySpot.Launchpad when T != null:
                    // beside its airship, on the scoring-table side, in its own LAUNCHPAD
                    Place(defender, T.side * (Mathf.Abs(T.launchpadX) + 30f), 100f, 90f);
                    return;
                case DummySpot.RetrievalZone when loading != null && loading.Of(opp) != null:
                {
                    var L = loading.Of(opp).spec;
                    var mid = new Vector2((L.chutes[0] + L.chutes[2]) / 2f, (L.chutes[1] + L.chutes[3]) / 2f);
                    var inward = new Vector2(L.inward[0], L.inward[1]);
                    var at = mid + inward * (halfL + 14f);
                    Place(defender, at.x, at.y, Mathf.Atan2(-inward.y, -inward.x) * Mathf.Rad2Deg); // facing its station
                    return;
                }
                case DummySpot.Guardrail:
                    // lengthwise against the far-side guardrail, on your side of the field: push it into the rail
                    Place(defender, Side * 100f, -162.4f + halfW + 0.3f, 0f);
                    return;
                case DummySpot.Rope when ropes != null:
                    foreach (var R in ropes.ropes)
                    {
                        if (R.alliance != opp || T == null) continue;
                        // facing the rope from outside the airship, its climber right at the rope
                        var a = new Vector2(R.spec.anchor[0], R.spec.anchor[1]);
                        var outDir = (a - new Vector2(T.airship[0], T.airship[1])).normalized;
                        float setback = defenderDef.climber != null ? defenderDef.climber.setback : 3f;
                        var c = a + outDir * (halfL - setback);
                        Place(defender, c.x, c.y, Mathf.Atan2(-outDir.y, -outDir.x) * Mathf.Rad2Deg);
                        return;
                    }
                    break;
            }
            Place(defender, Side * 60f, StationY[station], 90f); // midfield, in front of your station
        }

        // ---------------------------------------------------------------- loop
        void FixedUpdate()
        {
            if (rules == null) return;
            float dt = Time.fixedDeltaTime;
            if (State == AppState.Playing)
            {
                // the tick belongs to the phase it starts in. The 3 s pause after AUTO: FUEL the boilers count
                // still scores at AUTO values (no mobility, pilots and ROTORS already on TELEOP)
                var phase = Clock.Phase;
                if (phase == MatchPhase.Auto || phase == MatchPhase.AutoPause || phase == MatchPhase.Teleop)
                    rules.Advance(dt, phase == MatchPhase.Auto ? Period.Auto : Period.Teleop, RobotsForRules(),
                                  phase == MatchPhase.AutoPause ? Period.Auto : (Period?)null);
                Clock.Tick(dt);
                // the referees watch while robots are enabled (AUTO and TELEOP)
                if (Clock.Running)
                    Referee.Step(dt, new List<RobotMechanisms>(Mechs()), rules, loading, ropes,
                                 phase == MatchPhase.Auto, Clock.Phase == MatchPhase.Teleop && Clock.PeriodLeft <= MatchClock.EndgameLength);
                rules.Fill(Score);
            }
            bool on = State == AppState.Playing && Clock.Running;
            if (playerMech != null) playerMech.enabledByMatch = on;
            if (defenderMech != null)
            {
                // parked; the rope dummy may climb (its drive stays braked, it never shoots)
                bool climber = dummySpot == DummySpot.Rope && on;
                defenderMech.enabledByMatch = climber;
                defenderMech.climbHeld = climber;
                if (climber && Clock.InEndgame && defenderMech.Climb == RobotMechanisms.ClimbPhase.None) defenderMech.StartClimb();
            }
            hoppers.Step(dt, Mechs(), on); // pushing a HOPPER panel pours its 100 FUEL onto the field
            ropes.SetBodies(Bodies());
            animator.Sync(rules, dt, ClimbTop);
        }

        IEnumerable<RobotMechanisms> Mechs()
        {
            if (playerMech != null) yield return playerMech;
            if (defenderMech != null) yield return defenderMech;
        }

        List<RulesRobot> RobotsForRules()
        {
            var list = new List<RulesRobot>();
            foreach (var m in Mechs())
                list.Add(new RulesRobot { id = m.id, name = m.displayName, alliance = m.alliance, corners = m.Corners(), climbTouchpad = m.ClimbTouchpad, topZ = m.TopZ });
            return list;
        }

        IEnumerable<RopeSystem.Body> Bodies() { foreach (var m in Mechs()) yield return m.AsBody(); }

        float ClimbTop(string al, int touchpad)
        {
            foreach (var m in Mechs()) if (m.alliance == al && m.ClimbTouchpad == touchpad) return m.TopZ;
            return -1f;
        }

        void OnCue(MatchCue cue)
        {
            sounds.Play(cue);
            switch (cue)
            {
                case MatchCue.MatchStart: hud.Banner("AUTONOMOUS"); break;
                case MatchCue.AutoEnd: rules.EndAuto(); break;
                case MatchCue.TeleopStart: rules.StartTeleop(); break;
                case MatchCue.Endgame: ropes.DeployAll(); break; // PILOTS drop the ROPES
                case MatchCue.MatchEnd:
                    rules.EndMatch();
                    rules.Fill(Score);
                    resultsAt = Time.time + resultsDelay; // final score slides in shortly after
                    break;
            }
        }

        void Update()
        {
            // title music while the menus are up (title, options, robot selection); fades out for a match
            if (music != null) music.Wanted = State == AppState.MainMenu || State == AppState.RobotSelect || State == AppState.SetAuto;
            var input = reader.Read();
            bool driving = State == AppState.Playing && Clock.Running; // disabled during the countdown and after 0:00
            if (playerDrive != null) playerDrive.input = driving || State == AppState.SetAuto ? input : RobotInput.Idle;
            if (playerMech != null)
            {
                playerMech.shootHeld = driving && input.shoot;
                playerMech.dumpHeld = driving && input.dump;
                playerMech.intakeHeld = driving && input.intake;
                if (driving && reader.PlaceGear) playerMech.PlaceGear();
                // hold Climb to climb; let go and the robot slowly slides back down the rope
                playerMech.climbHeld = driving && reader.ClimbHeld;
                if (driving && reader.Climb) playerMech.StartClimb();
                // loading lane (your wall) or loading station (corner): in its zone, L1 / F for FUEL and R1 / G for a GEAR
                if (driving && reader.ChuteFuel) loading.RequestFuel(playerMech);
                if (driving && reader.ChuteGear) loading.RequestGear(playerMech);
            }
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
                    if (resultsAt > 0f && Clock.Phase == MatchPhase.Over && Time.time >= resultsAt) ShowResults();
                    break;
                case AppState.Results:
                    if (reader.RestartMatch) PlayAgain();
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
            float w = 360f, h = 215f, x = Screen.width - w - 16f, y = Screen.height - h - 16f;
            GUI.Box(new Rect(x, y, w, h), GUIContent.none, box);
            GUILayout.BeginArea(new Rect(x + 12f, y + 8f, w - 24f, h - 16f));
            float speedFt = playerDrive != null ? playerDrive.Speed / 0.3048f : 0f;
            GUILayout.Label($"<color=#d9a514>{speedFt:0.0}</color> <size=14>ft/s</size>", big);
            var spec = Units.ToSpec(player.transform.position);
            float heading = Mathf.Repeat(90f - player.transform.eulerAngles.y, 360f);
            GUILayout.Label($"{current.name}  <color=#8e99a3>({alliance}, {StationName[station]})</color>", label);
            GUILayout.Label($"Position {spec.x:0}, {spec.y:0} in   Heading {heading:0}°", label);
            {
                // level check: the robot's tilt, and the height of whatever is under it (should be the carpet, 0 in)
                var e = player.transform.eulerAngles;
                float pitchT = Mathf.DeltaAngle(0f, e.x), rollT = Mathf.DeltaAngle(0f, e.z);
                string under = "nothing";
                var hits = Physics.RaycastAll(player.transform.position + Vector3.up * 2f, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                foreach (var hit in hits)
                {
                    if (hit.rigidbody != null && hit.rigidbody.gameObject == player) continue;
                    if (hit.distance < best) { best = hit.distance; under = $"{hit.collider.name} at {hit.point.y / Units.Inch:0.0} in, slope {Vector3.Angle(hit.normal, Vector3.up):0.0}°"; }
                }
                GUILayout.Label($"Level: tilt {pitchT:0.0}° / {rollT:0.0}°, under it {under}", label);
            }
            if (playerMech != null)
            {
                GUILayout.Label($"Shooter: {playerMech.ShooterStatus}", label);
                GUILayout.Label($"Carrying: {playerMech.LoadStatus}", label);
                var at = loading.ZoneOf(playerMech);
                foreach (var st in new[] { loading.LaneOf(alliance), loading.Of(alliance) })
                    if (st != null) GUILayout.Label($"{st.Label}: {st.fuel} FUEL, {st.gears} GEARS{(st == at ? "  (in zone)" : "")}", label);
                if (Time.time - playerMech.ChuteGearAt < 4f) GUILayout.Label($"Chute GEAR: {playerMech.ChuteGearStatus}", label);
            }
            GUILayout.Label($"Camera {cameraRig?.mode}{(cameraRig != null && cameraRig.chaseReversed && cameraRig.mode == RobotCameraRig.Mode.Chase ? " (front)" : "")}   Input {reader.LastSource}", label);
            if (!string.IsNullOrEmpty(message) && Time.unscaledTime - messageTime < 2.5f)
                GUILayout.Label($"<color=#ffffff>{message}</color>", label);
            GUILayout.EndArea();
        }
    }
}
