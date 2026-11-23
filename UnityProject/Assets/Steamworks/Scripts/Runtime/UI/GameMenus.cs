using System.Collections.Generic;
using SteamworksSim.Game;
using SteamworksSim.Robots;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SteamworksSim.UI
{
    public enum MenuPage { None, Main, RobotSelect, SetAuto, Multiplayer, Options, Pause, Results }

    /// <summary>
    /// Main menu, robot selection, multiplayer placeholder, options and pause menu. Built in code with
    /// placeholder styling; SimBootstrap decides which page is showing.
    /// </summary>
    public class GameMenus : MonoBehaviour
    {
        SimBootstrap sim;
        Canvas canvas;
        Image dim;
        GameObject wallpaper;
        OptionsScreen options;
        ResultsScreen results;
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
            BuildWallpaper();
            dim = Ui.Fill(canvas.transform, "Dim", new Color(0f, 0f, 0f, 0.4f));
            BuildMain();
            BuildRobotSelect();
            BuildSetAuto();
            BuildMultiplayer();
            BuildPause();
            options = gameObject.AddComponent<OptionsScreen>();
            pages[MenuPage.Options] = options.Build(canvas.transform, () => Back(), () => sim.PlayTestSound(), () => sim.SoundStatus);
            firstSelect[MenuPage.Options] = options.First;
            results = gameObject.AddComponent<ResultsScreen>();
            pages[MenuPage.Results] = results.Build(canvas.transform, () => sim.PlayAgain(), () => sim.EnterRobotSelect(), () => sim.ShowMainMenu());
            firstSelect[MenuPage.Results] = results.First;
            Show(MenuPage.None);
        }

        public void Show(MenuPage page)
        {
            Page = page;
            foreach (var kv in pages) kv.Value.gameObject.SetActive(kv.Key == page);
            bool onTitle = page == MenuPage.Main || page == MenuPage.Multiplayer || (page == MenuPage.Options && optionsReturn == MenuPage.Main);
            wallpaper.SetActive(onTitle);
            dim.gameObject.SetActive(page == MenuPage.Pause || page == MenuPage.Options);
            dim.color = new Color(0f, 0f, 0f, onTitle ? 0.3f : 0.5f);
            if (page == MenuPage.RobotSelect) RefreshRobotSelect();
            if (page == MenuPage.Options) options.Refresh();
            var es = EventSystem.current;
            if (es != null)
            {
                es.SetSelectedGameObject(null);
                if (firstSelect.TryGetValue(page, out var f) && f != null) es.SetSelectedGameObject(f.gameObject);
            }
        }

        public void ShowResults(MatchScore score)
        {
            Show(MenuPage.Results);
            results.Show(score);
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
                case MenuPage.SetAuto: sim.CancelSetAuto(); break;
                case MenuPage.Pause: sim.Resume(); break;
            }
        }

        static void GameSettingsSave() => Settings.GameSettings.Save();

        void Update()
        {
            // keep something selected so keyboard / gamepad navigation never gets stuck after a mouse click
            UpdateSetAuto();
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
        void BuildWallpaper()
        {
            // Title wallpaper (Resources/UI/TitleWallpaper), whole picture in view; the rest of the screen is
            // filled with the beige of the picture's own border
            var r = Ui.Stretch(Ui.Rect("Wallpaper", canvas.transform));
            r.gameObject.AddComponent<Image>().color = new Color(0.867f, 0.694f, 0.569f, 1f);
            var pic = Ui.Rect("Picture", r);
            pic.anchorMin = pic.anchorMax = pic.pivot = new Vector2(0.5f, 0.5f);
            wallpaper = r.gameObject;
            var picture = pic.gameObject.AddComponent<RawImage>();
            picture.raycastTarget = false;
            var tex = Resources.Load<Texture2D>("UI/TitleWallpaper");
            if (tex != null)
            {
                picture.texture = tex;
                var fit = pic.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fit.aspectRatio = tex.width / (float)tex.height;
            }
            else
            {
                Ui.Stretch(pic);
                picture.color = new Color(0.12f, 0.09f, 0.07f, 1f);
            }
        }

        /// <summary>White rounded card in the middle of the title screen.</summary>
        RectTransform TitleCard(RectTransform page, string name, float width)
        {
            var col = Ui.Column(page, name, width, 14f, 44);
            Ui.Anchor(col, new Vector2(0.5f, 0.5f), new Vector2(width, 0f));
            Ui.Round(col.gameObject.AddComponent<Image>(), 28f).color = Ui.Paper;
            col.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0f, -6f);
            return col;
        }

        // ---------------------------------------------------------------- main menu
        void BuildMain()
        {
            var page = NewPage(MenuPage.Main, "Main menu");
            var col = TitleCard(page, "Main", 600f);
            var title = Ui.Title(col, "STEAMWORKS SIM", 54, Ui.Ink, TextAnchor.MiddleCenter, 70f);
            title.horizontalOverflow = HorizontalWrapMode.Overflow; // one line, never wrapped
            Ui.Spacer(col, 10f);
            firstSelect[MenuPage.Main] = Ui.Button(col, "SINGLEPLAYER", () => sim.EnterRobotSelect(), 64f, 32, true, true);
            Ui.Button(col, "MULTIPLAYER", () => Show(MenuPage.Multiplayer), 64f, 32, true, true);
            Ui.Button(col, "OPTIONS", () => OpenOptions(MenuPage.Main), 64f, 32, true, true);
            Ui.Button(col, "QUIT GAME", () => sim.ExitGame(), 64f, 32, true, true);

            var version = Ui.Label(page, "Prototype build", 18, Ui.TextColor, TextAnchor.LowerRight);
            Ui.Anchor(version.rectTransform, new Vector2(1f, 0f), new Vector2(600f, 40f), new Vector2(-24f, 16f));
            version.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(1f, -1f);
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
            Ui.Title(col, "CHOOSE YOUR ROBOT", 56, Ui.Gold, TextAnchor.MiddleLeft, 72f);
            Ui.Spacer(col, 8f);
            robotRow = Ui.Option(col, "Robot", new[] { "-" }, 0, i => { sim.SelectRobot(i); RefreshRobotInfo(); }, true, 52f, 210f);
            allianceRow = Ui.Option(col, "Alliance", new[] { "Red", "Blue" }, 0, i => { sim.SetAlliance(i == 0 ? "red" : "blue"); RefreshRobotInfo(); }, true, 52f, 210f);
            var stations = new string[SimBootstrap.StationName.Length];
            for (int i = 0; i < stations.Length; i++) stations[i] = char.ToUpper(SimBootstrap.StationName[i][0]) + SimBootstrap.StationName[i].Substring(1);
            stationRow = Ui.Option(col, "Start position", stations, 1, i => { if (i < SimBootstrap.StationName.Length) sim.SetStation(i); else sim.UseCustomStart(); }, true, 52f, 210f);
            Ui.Button(col, "SET AUTO", () => sim.EnterSetAuto(), 56f, 26, false, true);
            Ui.Spacer(col, 6f);
            robotInfo = Ui.Label(col, "", 21, Ui.TextColor, TextAnchor.UpperLeft);
            robotInfo.GetComponent<LayoutElement>().minHeight = 230f;
            Ui.Spacer(col, 6f);
            Ui.Button(col, "START MATCH", () => sim.StartMatchFromSelect(), 68f, 34, false, true);
            Ui.Button(col, "Reload robot files", () => { sim.ReloadRobots(); RefreshRobotSelect(); }, 52f, 22);
            Ui.Button(col, "BACK", () => sim.ShowMainMenu(), 52f, 26, false, true);
            firstSelect[MenuPage.RobotSelect] = robotRow;
        }

        // ---------------------------------------------------------------- SET AUTO overlay (bird's-eye view)
        Text autoStatus;
        float autoNotSavedAt = -100f;

        void BuildSetAuto()
        {
            var page = NewPage(MenuPage.SetAuto, "Set auto");
            var cancel = Ui.Button(page, "CANCEL", () => sim.CancelSetAuto(), 60f, 28, false, true);
            Ui.Anchor(cancel.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(230f, 60f), new Vector2(24f, -24f));
            var save = Ui.Button(page, "SAVE", () => { if (!sim.SaveAuto()) autoNotSavedAt = Time.unscaledTime; }, 60f, 28, false, true);
            Ui.Anchor(save.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(230f, 60f), new Vector2(24f, -96f));
            var reset = Ui.Button(page, "RESET", () => sim.ResetSetAuto(), 60f, 28, false, true);
            Ui.Anchor(reset.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(200f, 60f), new Vector2(-24f, -24f));
            var msg = Ui.Anchor(Ui.Rect("Message", page), new Vector2(0.5f, 1f), new Vector2(980f, 84f), new Vector2(0f, -24f));
            msg.gameObject.AddComponent<Image>().color = Ui.Backdrop;
            var t = Ui.Label(msg, "Drive around to set your starting position, but remember:
you can only save it if your robot is touching your ALLIANCE WALL!", 26, Ui.TextColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(t.rectTransform, 8f);
            var st = Ui.Anchor(Ui.Rect("Status", page), new Vector2(0.5f, 1f), new Vector2(640f, 46f), new Vector2(0f, -116f));
            st.gameObject.AddComponent<Image>().color = Ui.Backdrop;
            autoStatus = Ui.Label(st, "", 24, Ui.TextColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(autoStatus.rectTransform, 4f);
            firstSelect[MenuPage.SetAuto] = save;
        }

        void UpdateSetAuto()
        {
            if (Page != MenuPage.SetAuto || autoStatus == null) return;
            if (Time.unscaledTime - autoNotSavedAt < 2f)
                autoStatus.text = "<color=#ff5a64>NOT SAVED: your robot must touch your alliance wall</color>";
            else
                autoStatus.text = sim.AutoStartValid ? "<color=#5fd35f>Touching your alliance wall: SAVE to keep it</color>"
                                                     : "<color=#ffb020>Not touching your alliance wall</color>";
        }

        void RefreshRobotSelect()
        {
            var lib = sim.Library;
            var names = new string[lib != null ? lib.robots.Count : 0];
            for (int i = 0; i < names.Length; i++) names[i] = lib.robots[i].name;
            int idx = lib != null ? lib.robots.IndexOf(sim.Current) : -1;
            robotRow.SetValues(names.Length > 0 ? names : new[] { sim.Current != null ? sim.Current.name : "-" }, Mathf.Max(0, idx));
            allianceRow.SetIndex(sim.alliance == "blue" ? 1 : 0);
            var starts = new List<string>();
            foreach (var n in SimBootstrap.StationName) starts.Add(char.ToUpper(n[0]) + n.Substring(1));
            if (sim.HasSavedAuto || sim.CustomStart) starts.Add("Set with SET AUTO");
            stationRow.SetValues(starts.ToArray(), sim.CustomStart ? starts.Count - 1 : sim.station);
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
            var col = TitleCard(page, "Multiplayer", 760f);
            Ui.Title(col, "MULTIPLAYER", 60, Ui.Ink, TextAnchor.MiddleCenter, 80f);
            Ui.Label(col, "Online matches (host a game, join with a code, up to six robots) are coming in a later build.", 24, Ui.Ink, TextAnchor.MiddleCenter);
            Ui.Spacer(col, 10f);
            firstSelect[MenuPage.Multiplayer] = Ui.Button(col, "BACK", () => Show(MenuPage.Main), 64f, 32, true, true);
        }

        // ---------------------------------------------------------------- pause
        void BuildPause()
        {
            var page = NewPage(MenuPage.Pause, "Pause menu");
            var col = Ui.Column(page, "Pause", 560f, 14f, 36);
            Ui.Anchor(col, new Vector2(0.5f, 0.5f), new Vector2(560f, 0f));
            Ui.Round(col.gameObject.AddComponent<Image>(), 28f).color = Ui.Paper;
            Ui.Title(col, "PAUSED", 64, Ui.Ink, TextAnchor.MiddleCenter, 84f);
            firstSelect[MenuPage.Pause] = Ui.Button(col, "RESUME", () => sim.Resume(), 60f, 30, true, true);
            Ui.Button(col, "OPTIONS", () => OpenOptions(MenuPage.Pause), 60f, 30, true, true);
            Ui.Button(col, "RETURN TO ROBOT SELECTION", () => sim.EnterRobotSelect(), 60f, 30, true, true);
            Ui.Button(col, "QUIT TO MAIN MENU", () => sim.ShowMainMenu(), 60f, 30, true, true);
            Ui.Button(col, "EXIT GAME", () => sim.ExitGame(), 60f, 30, true, true);
        }
    }
}
