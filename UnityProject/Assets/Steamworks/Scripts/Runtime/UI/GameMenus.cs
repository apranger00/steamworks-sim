using System.Collections.Generic;
using SteamworksSim.Game;
using SteamworksSim.Robots;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SteamworksSim.UI
{
    public enum MenuPage { None, Main, RobotSelect, Multiplayer, Options, Pause }

    /// <summary>
    /// Main menu, robot selection, multiplayer placeholder, options and pause menu. Built in code with
    /// placeholder styling; SimBootstrap decides which page is showing.
    /// </summary>
    public class GameMenus : MonoBehaviour
    {
        SimBootstrap sim;
        Canvas canvas;
        Image dim;
        OptionsScreen options;
        readonly Dictionary<MenuPage, RectTransform> pages = new Dictionary<MenuPage, RectTransform>();
        readonly Dictionary<MenuPage, Selectable> firstSelect = new Dictionary<MenuPage, Selectable>();
        MenuPage optionsReturn = MenuPage.Main;

        OptionRow robotRow, allianceRow, stationRow;
        Text robotInfo;

        public MenuPage Page { get; private set; } = MenuPage.None;
        public bool Rebinding => options != null && options.Rebinding;

        public void Init(SimBootstrap owner)
        {
            sim = owner;
            canvas = Ui.Canvas("Menus", 10, transform);
            dim = Ui.Fill(canvas.transform, "Dim", new Color(0f, 0f, 0f, 0.4f));
            BuildMain();
            BuildRobotSelect();
            BuildMultiplayer();
            BuildPause();
            options = gameObject.AddComponent<OptionsScreen>();
            pages[MenuPage.Options] = options.Build(canvas.transform, () => Back(), () => sim.PlayTestSound());
            firstSelect[MenuPage.Options] = options.First;
            Show(MenuPage.None);
        }

        public void Show(MenuPage page)
        {
            Page = page;
            foreach (var kv in pages) kv.Value.gameObject.SetActive(kv.Key == page);
            dim.gameObject.SetActive(page != MenuPage.None && page != MenuPage.RobotSelect);
            dim.color = new Color(0f, 0f, 0f, page == MenuPage.Main || page == MenuPage.Multiplayer ? 0.25f : 0.5f);
            if (page == MenuPage.RobotSelect) RefreshRobotSelect();
            if (page == MenuPage.Options) options.Refresh();
            var es = EventSystem.current;
            if (es != null)
            {
                es.SetSelectedGameObject(null);
                if (firstSelect.TryGetValue(page, out var f) && f != null) es.SetSelectedGameObject(f.gameObject);
            }
        }

        public void OpenOptions(MenuPage returnTo)
        {
            optionsReturn = returnTo;
            Show(MenuPage.Options);
        }

        /// <summary>Esc / gamepad B / Select: one step back.</summary>
        public void Back()
        {
            if (Rebinding) return;
            switch (Page)
            {
                case MenuPage.Options:
                    if (options.Rebinding) return;
                    GameSettingsSave();
                    Show(optionsReturn);
                    break;
                case MenuPage.Multiplayer: Show(MenuPage.Main); break;
                case MenuPage.RobotSelect: sim.ShowMainMenu(); break;
                case MenuPage.Pause: sim.Resume(); break;
            }
        }

        static void GameSettingsSave() => Settings.GameSettings.Save();

        void Update()
        {
            // keep something selected so keyboard / gamepad navigation never gets stuck after a mouse click
            if (Page == MenuPage.None || Rebinding) return;
            var es = EventSystem.current;
            if (es == null) return;
            var sel = es.currentSelectedGameObject;
            if ((sel == null || !sel.activeInHierarchy) && firstSelect.TryGetValue(Page, out var f) && f != null)
                es.SetSelectedGameObject(f.gameObject);
        }

        RectTransform NewPage(MenuPage page, string name)
        {
            var r = Ui.Stretch(Ui.Rect(name, canvas.transform));
            pages[page] = r;
            return r;
        }

        // ---------------------------------------------------------------- main menu
        void BuildMain()
        {
            var page = NewPage(MenuPage.Main, "Main menu");
            var col = Ui.Column(page, "Main", 560f, 14f);
            Ui.Anchor(col, new Vector2(0.5f, 0.5f), new Vector2(560f, 0f), new Vector2(0f, 20f));
            Ui.Label(col, "FIRST STEAMWORKS", 68, Ui.Gold, TextAnchor.MiddleCenter, FontStyle.Bold, 84f);
            Ui.Label(col, "2017 FRC game simulator", 26, Ui.Muted, TextAnchor.MiddleCenter, FontStyle.Normal, 36f);
            Ui.Spacer(col, 36f);
            firstSelect[MenuPage.Main] = Ui.Button(col, "Singleplayer", () => sim.EnterRobotSelect());
            Ui.Button(col, "Multiplayer", () => Show(MenuPage.Multiplayer));
            Ui.Button(col, "Options", () => OpenOptions(MenuPage.Main));
            Ui.Button(col, "Quit game", () => sim.ExitGame());

            var version = Ui.Label(page, "Prototype build: placeholder art", 18, Ui.Muted, TextAnchor.LowerRight);
            Ui.Anchor(version.rectTransform, new Vector2(1f, 0f), new Vector2(600f, 40f), new Vector2(-24f, 16f));
        }

        // ---------------------------------------------------------------- robot selection
        void BuildRobotSelect()
        {
            var page = NewPage(MenuPage.RobotSelect, "Robot select");
            var side = Ui.Rect("Panel", page);
            side.anchorMin = new Vector2(0f, 0f); side.anchorMax = new Vector2(0f, 1f); side.pivot = new Vector2(0f, 0.5f);
            side.sizeDelta = new Vector2(720f, 0f); side.anchoredPosition = Vector2.zero;
            side.gameObject.AddComponent<Image>().color = Ui.Backdrop;

            var col = Ui.Column(side, "Content", 640f, 12f);
            Ui.Anchor(col, new Vector2(0.5f, 1f), new Vector2(640f, 0f), new Vector2(0f, -60f));
            Ui.Label(col, "Choose your robot", 44, Ui.Gold, TextAnchor.MiddleLeft, FontStyle.Bold, 64f);
            Ui.Spacer(col, 8f);
            robotRow = Ui.Option(col, "Robot", new[] { "-" }, 0, i => { sim.SelectRobot(i); RefreshRobotInfo(); }, true, 52f, 210f);
            allianceRow = Ui.Option(col, "Alliance", new[] { "Red", "Blue" }, 0, i => { sim.SetAlliance(i == 0 ? "red" : "blue"); RefreshRobotInfo(); }, true, 52f, 210f);
            var stations = new string[SimBootstrap.StationName.Length];
            for (int i = 0; i < stations.Length; i++) stations[i] = char.ToUpper(SimBootstrap.StationName[i][0]) + SimBootstrap.StationName[i].Substring(1);
            stationRow = Ui.Option(col, "Start position", stations, 1, i => sim.SetStation(i), true, 52f, 210f);
            Ui.Spacer(col, 6f);
            robotInfo = Ui.Label(col, "", 21, Ui.TextColor, TextAnchor.UpperLeft);
            robotInfo.GetComponent<LayoutElement>().minHeight = 230f;
            Ui.Spacer(col, 6f);
            Ui.Button(col, "Start match", () => sim.StartMatchFromSelect(), 66f, 28);
            Ui.Button(col, "Reload robot files", () => { sim.ReloadRobots(); RefreshRobotSelect(); }, 52f, 22);
            Ui.Button(col, "Back", () => sim.ShowMainMenu(), 52f, 22);
            firstSelect[MenuPage.RobotSelect] = robotRow;
        }

        void RefreshRobotSelect()
        {
            var lib = sim.Library;
            var names = new string[lib != null ? lib.robots.Count : 0];
            for (int i = 0; i < names.Length; i++) names[i] = lib.robots[i].name;
            int idx = lib != null ? lib.robots.IndexOf(sim.Current) : -1;
            robotRow.SetValues(names.Length > 0 ? names : new[] { sim.Current != null ? sim.Current.name : "-" }, Mathf.Max(0, idx));
            allianceRow.SetIndex(sim.alliance == "blue" ? 1 : 0);
            stationRow.SetIndex(sim.station);
            RefreshRobotInfo();
        }

        void RefreshRobotInfo()
        {
            var d = sim.Current;
            if (d == null) { robotInfo.text = ""; return; }
            var errors = new List<string>(); var warnings = new List<string>();
            RobotRules.Validate(d, errors, warnings);
            string team = d.team > 0 ? $"  ·  Team {d.team}" : "";
            string by = string.IsNullOrEmpty(d.author) ? "" : $"by {d.author}  ·  ";
            string s =
                $"<b>{d.name}</b>{team}\n" +
                $"<color=#9aa3ad>{by}{(d.builtIn ? "built-in" : "mod")}  ·  v{d.version}</color>\n";
            if (!string.IsNullOrEmpty(d.description)) s += d.description + "\n";
            s += $"\n{d.WidthWithBumpers:0.#} x {d.LengthWithBumpers:0.#} x {d.frame.height:0.#} in with bumpers  ·  {d.mass:0} lb\n" +
                 $"Top speed {d.drivetrain.topSpeed:0.#} ft/s  ·  {d.drivetrain.wheelsPerSide * 2} wheels, {d.drivetrain.wheelDiameter:0.#} in\n";
            if (errors.Count > 0) s += $"\n<color=#e0604c>Not legal: {string.Join("; ", errors)}</color>";
            else if (warnings.Count > 0) s += $"\n<color=#d9a514>{string.Join("; ", warnings)}</color>";
            var lib = sim.Library;
            if (lib != null && lib.problems.Count > 0) s += $"\n<color=#e0604c>{lib.problems.Count} robot file problem(s); see the Console.</color>";
            robotInfo.text = s;
        }

        // ---------------------------------------------------------------- multiplayer (placeholder)
        void BuildMultiplayer()
        {
            var page = NewPage(MenuPage.Multiplayer, "Multiplayer");
            var col = Ui.Column(page, "Multiplayer", 760f, 16f, 30);
            Ui.Anchor(col, new Vector2(0.5f, 0.5f), new Vector2(760f, 0f));
            col.gameObject.AddComponent<Image>().color = Ui.Backdrop;
            Ui.Label(col, "Multiplayer", 44, Ui.Gold, TextAnchor.MiddleCenter, FontStyle.Bold, 64f);
            Ui.Label(col, "Online matches (host a game, join with a code, up to six robots) are coming in a later build.", 24, Ui.TextColor, TextAnchor.MiddleCenter);
            Ui.Spacer(col, 10f);
            firstSelect[MenuPage.Multiplayer] = Ui.Button(col, "Back", () => Show(MenuPage.Main));
        }

        // ---------------------------------------------------------------- pause
        void BuildPause()
        {
            var page = NewPage(MenuPage.Pause, "Pause menu");
            var col = Ui.Column(page, "Pause", 560f, 14f, 30);
            Ui.Anchor(col, new Vector2(0.5f, 0.5f), new Vector2(560f, 0f));
            col.gameObject.AddComponent<Image>().color = Ui.Backdrop;
            Ui.Label(col, "Paused", 48, Ui.Gold, TextAnchor.MiddleCenter, FontStyle.Bold, 70f);
            firstSelect[MenuPage.Pause] = Ui.Button(col, "Resume", () => sim.Resume());
            Ui.Button(col, "Options", () => OpenOptions(MenuPage.Pause));
            Ui.Button(col, "Return to robot selection", () => sim.EnterRobotSelect());
            Ui.Button(col, "Quit to main menu", () => sim.ShowMainMenu());
            Ui.Button(col, "Exit game", () => sim.ExitGame());
        }
    }
}
