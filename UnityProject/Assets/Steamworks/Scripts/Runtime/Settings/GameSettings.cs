using System;
using System.Collections.Generic;
using UnityEngine;

namespace SteamworksSim.Settings
{
    public enum StickLayout { SplitArcade, SingleStick, Tank }

    /// <summary>
    /// Player options (audio, graphics, gamepad layout), saved in PlayerPrefs. Key bindings are saved
    /// separately by GameControls. Call Apply() after changing anything.
    /// </summary>
    public static class GameSettings
    {
        public static float MasterVolume = 1f;
        public static float MatchVolume = 1f;
        public static float MusicVolume = 0.6f;

        public static FullScreenMode DisplayMode = FullScreenMode.FullScreenWindow;
        public static int ResolutionWidth, ResolutionHeight;   // 0 = leave as is
        public static bool VSync = true;
        public static int FrameCap;                             // 0 = unlimited (only used with VSync off)
        public static int QualityLevel = -1;                    // -1 = project default
        public static float FieldOfView = 60f;
        public static bool ShowDriveInfo = true;

        public static StickLayout Layout = StickLayout.SplitArcade;

        /// <summary>Raised by Apply(), so components can pick up new values.</summary>
        public static event Action Changed;

        const string P = "steamworks.";
        static bool loaded;

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            MasterVolume = PlayerPrefs.GetFloat(P + "masterVolume", MasterVolume);
            MatchVolume = PlayerPrefs.GetFloat(P + "matchVolume", MatchVolume);
            MusicVolume = PlayerPrefs.GetFloat(P + "musicVolume", MusicVolume);
            DisplayMode = (FullScreenMode)PlayerPrefs.GetInt(P + "displayMode", (int)DisplayMode);
            ResolutionWidth = PlayerPrefs.GetInt(P + "resW", 0);
            ResolutionHeight = PlayerPrefs.GetInt(P + "resH", 0);
            VSync = PlayerPrefs.GetInt(P + "vsync", VSync ? 1 : 0) != 0;
            FrameCap = PlayerPrefs.GetInt(P + "frameCap", FrameCap);
            QualityLevel = PlayerPrefs.GetInt(P + "quality", QualityLevel);
            FieldOfView = PlayerPrefs.GetFloat(P + "fov", FieldOfView);
            ShowDriveInfo = PlayerPrefs.GetInt(P + "driveInfo", ShowDriveInfo ? 1 : 0) != 0;
            Layout = (StickLayout)PlayerPrefs.GetInt(P + "stickLayout", (int)Layout);
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat(P + "masterVolume", MasterVolume);
            PlayerPrefs.SetFloat(P + "matchVolume", MatchVolume);
            PlayerPrefs.SetFloat(P + "musicVolume", MusicVolume);
            PlayerPrefs.SetInt(P + "displayMode", (int)DisplayMode);
            PlayerPrefs.SetInt(P + "resW", ResolutionWidth);
            PlayerPrefs.SetInt(P + "resH", ResolutionHeight);
            PlayerPrefs.SetInt(P + "vsync", VSync ? 1 : 0);
            PlayerPrefs.SetInt(P + "frameCap", FrameCap);
            PlayerPrefs.SetInt(P + "quality", QualityLevel);
            PlayerPrefs.SetFloat(P + "fov", FieldOfView);
            PlayerPrefs.SetInt(P + "driveInfo", ShowDriveInfo ? 1 : 0);
            PlayerPrefs.SetInt(P + "stickLayout", (int)Layout);
            PlayerPrefs.Save();
        }

        public static void Apply()
        {
            AudioListener.volume = Mathf.Clamp01(MasterVolume);

            // Quality first: switching quality level can reset vSyncCount
            if (QualityLevel >= 0 && QualityLevel < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != QualityLevel)
                QualitySettings.SetQualityLevel(QualityLevel, true);
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = FrameCap > 0 ? FrameCap : -1;

            // Window and resolution only mean something in a built game (the editor's Game view ignores them)
            if (!Application.isEditor)
            {
                int w = ResolutionWidth > 0 ? ResolutionWidth : Screen.width;
                int h = ResolutionHeight > 0 ? ResolutionHeight : Screen.height;
                if (Screen.fullScreenMode != DisplayMode || Screen.width != w || Screen.height != h)
                    Screen.SetResolution(w, h, DisplayMode);
            }

            var cam = Camera.main;
            if (cam != null) cam.fieldOfView = Mathf.Clamp(FieldOfView, 40f, 110f);

            Changed?.Invoke();
        }

        // ---------------------------------------------------------------- choices for the options menu
        public static readonly FullScreenMode[] DisplayModes = { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };
        public static readonly string[] DisplayModeNames = { "Fullscreen", "Borderless window", "Windowed" };
        public static readonly int[] FrameCaps = { 30, 60, 120, 144, 165, 240, 0 };
        public static readonly string[] FrameCapNames = { "30", "60", "120", "144", "165", "240", "Unlimited" };
        public static readonly string[] LayoutNames = { "Split arcade", "One-stick arcade", "Tank" };

        /// <summary>Distinct screen sizes the monitor supports, smallest first.</summary>
        public static List<Vector2Int> Resolutions()
        {
            var list = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (!list.Contains(v)) list.Add(v);
            }
            var cur = new Vector2Int(Screen.width, Screen.height);
            if (!list.Contains(cur)) list.Add(cur);
            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return list;
        }
    }
}
