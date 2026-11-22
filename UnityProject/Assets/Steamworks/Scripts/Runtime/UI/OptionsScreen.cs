using System;
using System.Collections;
using System.Collections.Generic;
using SteamworksSim.Audio;
using SteamworksSim.InputHandling;
using SteamworksSim.Settings;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace SteamworksSim.UI
{
    /// <summary>Options: Controls (rebinding, gamepad layout), Audio, Graphics. Opened from the main menu and the pause menu.</summary>
    public class OptionsScreen : MonoBehaviour
    {
        public RectTransform Root { get; private set; }
        public Selectable First { get; private set; }
        public bool Rebinding { get; private set; }

        Action onBack, onTestSound;
        Func<string> soundStatus;
        Text soundStatusText;
        readonly Button[] tabButtons = new Button[3];
        readonly RectTransform[] tabs = new RectTransform[3];
        static readonly string[] TabNames = { "Controls", "Audio", "Graphics" };
        int tab;

        class BindingRow { public GameControls.Entry entry; public Text keyboard, gamepad; }
        readonly List<BindingRow> bindingRows = new List<BindingRow>();
        Text controlsStatus;
        OptionRow layoutRow, masterRow, matchRow, displayRow, resolutionRow, vsyncRow, capRow, qualityRow, fovRow, infoRow;
        List<Vector2Int> resolutions = new List<Vector2Int>();

        public RectTransform Build(Transform parent, Action back, Action testSound, Func<string> status)
        {
            onBack = back; onTestSound = testSound; soundStatus = status;
            Root = Ui.Stretch(Ui.Rect("Options", parent));

            var panel = Ui.Anchor(Ui.Rect("Panel", Root), new Vector2(0.5f, 0.5f), new Vector2(1180f, 960f));
            panel.gameObject.AddComponent<Image>().color = Ui.Backdrop;

            var col = Ui.Column(panel, "Content", 1100f, 10f);
            Ui.Anchor(col, new Vector2(0.5f, 1f), new Vector2(1100f, 0f), new Vector2(0f, -28f));
            Ui.Label(col, "Options", 44, Ui.Gold, TextAnchor.MiddleCenter, FontStyle.Bold, 64f);

            var tabRow = Ui.Row(col, 56f, 10f);
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                tabButtons[i] = Ui.Button(tabRow, TabNames[i], () => ShowTab(k), 56f, 24);
                Ui.SetWidth(tabButtons[i], 200f, 1f);
            }
            First = tabButtons[0];
            Ui.Spacer(col, 6f);

            tabs[0] = BuildControls(col);
            tabs[1] = BuildAudio(col);
            tabs[2] = BuildGraphics(col);

            Ui.Spacer(col, 8f);
            Ui.Button(col, "Back", () => Back(), 56f, 24);
            ShowTab(0);
            return Root;
        }

        public void Back()
        {
            if (Rebinding) return;
            GameSettings.Save();
            onBack?.Invoke();
        }

        void ShowTab(int i)
        {
            tab = i;
            for (int k = 0; k < 3; k++)
            {
                tabs[k].gameObject.SetActive(k == i);
                Ui.LabelOf(tabButtons[k]).color = k == i ? Ui.Gold : Ui.TextColor;
            }
        }

        /// <summary>Re-reads every value (call when the screen opens).</summary>
        public void Refresh()
        {
            RefreshBindings();
            layoutRow.SetIndex((int)GameSettings.Layout);
            masterRow.SetIndex(Mathf.RoundToInt(GameSettings.MasterVolume * 20f));
            matchRow.SetIndex(Mathf.RoundToInt(GameSettings.MatchVolume * 20f));
            displayRow.SetIndex(Mathf.Max(0, Array.IndexOf(GameSettings.DisplayModes, GameSettings.DisplayMode)));
            RefreshResolutions();
            vsyncRow.SetIndex(GameSettings.VSync ? 0 : 1);
            int cap = Array.IndexOf(GameSettings.FrameCaps, GameSettings.FrameCap);
            capRow.SetIndex(cap >= 0 ? cap : GameSettings.FrameCaps.Length - 1);
            qualityRow.SetIndex(GameSettings.QualityLevel >= 0 ? GameSettings.QualityLevel : QualitySettings.GetQualityLevel());
            fovRow.SetIndex(Mathf.RoundToInt((GameSettings.FieldOfView - 50f) / 5f));
            infoRow.SetIndex(GameSettings.ShowDriveInfo ? 0 : 1);
            if (soundStatusText != null) soundStatusText.text = soundStatus != null ? soundStatus() : "";
            ShowTab(tab);
        }

        // ---------------------------------------------------------------- controls
        RectTransform BuildControls(Transform parent)
        {
            var c = Ui.Column(parent, "Controls", 1100f, 6f, 0, false);
            var header = Ui.Row(c, 34f, 12f);
            Ui.SetWidth(Ui.Label(header, "", 20, Ui.Muted), 200f, 1f);
            Ui.SetWidth(Ui.Label(header, "Keyboard", 20, Ui.Muted, TextAnchor.MiddleCenter), 300f);
            Ui.SetWidth(Ui.Label(header, "Gamepad", 20, Ui.Muted, TextAnchor.MiddleCenter), 300f);

            bindingRows.Clear();
            foreach (var e in GameControls.Entries)
            {
                var entry = e;
                var row = Ui.Row(c, 42f, 12f);
                Ui.SetWidth(Ui.Label(row, entry.label, 22, Ui.TextColor), 200f, 1f);
                var br = new BindingRow { entry = entry };
                Text kbLabel = null;
                var kb = Ui.Button(row, "", () => StartRebind(entry, false, kbLabel), 42f, 21);
                kbLabel = Ui.LabelOf(kb);
                Ui.SetWidth(kb, 300f);
                br.keyboard = kbLabel;
                if (entry.gamepadIndex >= 0)
                {
                    Text padLabel = null;
                    var pad = Ui.Button(row, "", () => StartRebind(entry, true, padLabel), 42f, 21);
                    padLabel = Ui.LabelOf(pad);
                    Ui.SetWidth(pad, 300f);
                    br.gamepad = padLabel;
                }
                else
                {
                    br.gamepad = Ui.Label(row, "", 20, Ui.Muted, TextAnchor.MiddleCenter);
                    Ui.SetWidth(br.gamepad, 300f);
                }
                bindingRows.Add(br);
            }

            Ui.Spacer(c, 4f);
            layoutRow = Ui.Option(c, "Gamepad drive layout", GameSettings.LayoutNames, (int)GameSettings.Layout, i =>
            {
                GameSettings.Layout = (StickLayout)i;
                GameSettings.Apply();
                RefreshBindings();
            }, true, 44f);

            var bottom = Ui.Row(c, 44f, 16f);
            var reset = Ui.Button(bottom, "Reset controls to defaults", () =>
            {
                if (Rebinding) return;
                GameControls.ResetToDefaults();
                RefreshBindings();
                controlsStatus.text = "Controls reset to defaults.";
            }, 44f, 21);
            Ui.SetWidth(reset, 380f);
            controlsStatus = Ui.Label(bottom, "Select a binding and press Enter / A, then the new key or button.", 19, Ui.Muted);
            Ui.SetWidth(controlsStatus, 300f, 1f);
            return c;
        }

        void RefreshBindings()
        {
            foreach (var br in bindingRows)
            {
                var e = br.entry;
                br.keyboard.text = GameControls.Display(e.action, e.keyboardIndex);
                br.gamepad.text = e.gamepadIndex >= 0 ? GameControls.Display(e.action, e.gamepadIndex)
                                                      : GameControls.GamepadDrive(e.driveDirection);
            }
        }

        void StartRebind(GameControls.Entry e, bool gamepad, Text label)
        {
            if (Rebinding || label == null) return;
            StartCoroutine(RebindRoutine(e, gamepad, label));
        }

        IEnumerator RebindRoutine(GameControls.Entry e, bool gamepad, Text label)
        {
            Rebinding = true;
            var es = EventSystem.current;
            if (es != null) es.sendNavigationEvents = false; // the new key must not also move the menu
            label.text = gamepad ? "Press a button..." : "Press a key...";
            controlsStatus.text = $"Binding \"{e.label}\". Esc cancels.";

            yield return WaitForRelease(); // the press that opened this must not become the binding
            bool done = false;
            GameControls.StartRebind(e.action, gamepad ? e.gamepadIndex : e.keyboardIndex, gamepad, () => done = true);
            while (!done) yield return null;
            yield return WaitForRelease();

            if (es != null) es.sendNavigationEvents = true;
            Rebinding = false;
            RefreshBindings();
            controlsStatus.text = $"{e.label}: {label.text}";
        }

        static IEnumerator WaitForRelease()
        {
            yield return null;
            while (AnyHeld()) yield return null;
        }

        static bool AnyHeld()
        {
            var k = Keyboard.current;
            if (k != null && k.anyKey.isPressed) return true;
            var g = Gamepad.current;
            if (g != null)
                foreach (var c in g.allControls)
                    if (c is ButtonControl b && !c.synthetic && b.isPressed) return true;
            return false;
        }

        // ---------------------------------------------------------------- audio
        RectTransform BuildAudio(Transform parent)
        {
            var c = Ui.Column(parent, "Audio", 1100f, 10f, 0, false);
            masterRow = Ui.Option(c, "Master volume", Ui.Percentages(), 20, i => { GameSettings.MasterVolume = i / 20f; GameSettings.Apply(); }, false);
            matchRow = Ui.Option(c, "Match sounds", Ui.Percentages(), 20, i => { GameSettings.MatchVolume = i / 20f; GameSettings.Apply(); }, false);
            Ui.Spacer(c, 6f);
            var buttons = Ui.Row(c, 48f, 12f);
            Ui.SetWidth(Ui.Button(buttons, "Play test sound (match start)", () => onTestSound?.Invoke(), 48f, 22), 420f);
            Ui.SetWidth(Ui.Button(buttons, "Open sounds folder", () => MatchSounds.OpenFolder(), 48f, 22), 320f);
            Ui.Spacer(c, 6f);
            soundStatusText = Ui.Label(c, "", 20, Ui.TextColor);
            Ui.Spacer(c, 4f);
            Ui.Label(c, "Match sounds are read from: " + MatchSounds.Folder + "\nPut the files there (names as in sounds.json), then restart Play. Robot and game-piece sounds will get their own slider when they exist.", 18, Ui.Muted);
            return c;
        }

        // ---------------------------------------------------------------- graphics
        RectTransform BuildGraphics(Transform parent)
        {
            var c = Ui.Column(parent, "Graphics", 1100f, 10f, 0, false);
            displayRow = Ui.Option(c, "Display mode", GameSettings.DisplayModeNames, 1, i => { GameSettings.DisplayMode = GameSettings.DisplayModes[i]; GameSettings.Apply(); });
            resolutionRow = Ui.Option(c, "Resolution", new[] { "-" }, 0, i =>
            {
                if (i < 0 || i >= resolutions.Count) return;
                GameSettings.ResolutionWidth = resolutions[i].x; GameSettings.ResolutionHeight = resolutions[i].y;
                GameSettings.Apply();
            }, false);
            vsyncRow = Ui.Option(c, "VSync", new[] { "On", "Off" }, 0, i => { GameSettings.VSync = i == 0; GameSettings.Apply(); });
            capRow = Ui.Option(c, "Frame rate limit (VSync off)", GameSettings.FrameCapNames, GameSettings.FrameCaps.Length - 1, i => { GameSettings.FrameCap = GameSettings.FrameCaps[i]; GameSettings.Apply(); }, false);
            qualityRow = Ui.Option(c, "Quality", QualitySettings.names, QualitySettings.GetQualityLevel(), i => { GameSettings.QualityLevel = i; GameSettings.Apply(); }, false);
            var fovs = new string[11];
            for (int i = 0; i < fovs.Length; i++) fovs[i] = (50 + i * 5) + "°";
            fovRow = Ui.Option(c, "Field of view", fovs, 2, i => { GameSettings.FieldOfView = 50f + i * 5f; GameSettings.Apply(); }, false);
            infoRow = Ui.Option(c, "Drive info panel", new[] { "Shown", "Hidden" }, 0, i => { GameSettings.ShowDriveInfo = i == 0; GameSettings.Apply(); });
            Ui.Spacer(c, 6f);
            Ui.Label(c, "Display mode and resolution apply in the built game; the Unity editor's Game view ignores them.", 19, Ui.Muted);
            return c;
        }

        void RefreshResolutions()
        {
            resolutions = GameSettings.Resolutions();
            var names = new string[resolutions.Count];
            for (int i = 0; i < names.Length; i++) names[i] = $"{resolutions[i].x} x {resolutions[i].y}";
            var want = GameSettings.ResolutionWidth > 0
                ? new Vector2Int(GameSettings.ResolutionWidth, GameSettings.ResolutionHeight)
                : new Vector2Int(Screen.width, Screen.height);
            int idx = resolutions.IndexOf(want);
            resolutionRow.SetValues(names, idx >= 0 ? idx : resolutions.Count - 1);
        }
    }
}
