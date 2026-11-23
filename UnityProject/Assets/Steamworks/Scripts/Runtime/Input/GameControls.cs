using System;
using System.Collections.Generic;
using SteamworksSim.Settings;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SteamworksSim.InputHandling
{
    /// <summary>
    /// The game's Input System actions, built in code so there is no asset to keep in sync. Every button
    /// action has one keyboard and one gamepad binding, both rebindable from Options > Controls; changes
    /// are saved in PlayerPrefs. Gamepad driving uses the sticks through the layout chosen in Options
    /// (split arcade, one-stick arcade or tank) rather than rebinding.
    /// </summary>
    public static class GameControls
    {
        public const string KeyboardGroup = "Keyboard", GamepadGroup = "Gamepad";
        const string PrefsKey = "steamworks.bindings";

        public static InputActionAsset Asset { get; private set; }
        public static InputAction Throttle, Turn, LeftStick, RightStick;
        public static InputAction Precision, NextCamera, FlipChase, ResetRobot, RestartMatch, Pause;
        public static InputAction Shoot, Intake, Dump, PlaceGear, Climb, ChuteFuel, ChuteGear;

        /// <summary>One line in the Controls menu.</summary>
        public class Entry
        {
            public string label;
            public InputAction action;
            public int keyboardIndex = -1, gamepadIndex = -1; // binding indices; -1 = not rebindable there
            public int driveDirection = -1;                   // 0 fwd, 1 back, 2 left, 3 right: gamepad comes from the stick layout
        }

        public static readonly List<Entry> Entries = new List<Entry>();

        public static void Init()
        {
            if (Asset != null) return;
            Asset = ScriptableObject.CreateInstance<InputActionAsset>();
            Asset.name = "Steamworks controls";
            var map = Asset.AddActionMap("Player");

            // Keyboard driving: 1D composites (bindings 0 = composite, 1 = Negative, 2 = Positive; arrows are 3-5)
            Throttle = map.AddAction("Throttle", InputActionType.Value, expectedControlLayout: "Axis");
            Throttle.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/s", KeyboardGroup).With("Positive", "<Keyboard>/w", KeyboardGroup);
            Throttle.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/downArrow", "Arrows").With("Positive", "<Keyboard>/upArrow", "Arrows");
            Turn = map.AddAction("Turn", InputActionType.Value, expectedControlLayout: "Axis"); // left positive
            Turn.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/d", KeyboardGroup).With("Positive", "<Keyboard>/a", KeyboardGroup);
            Turn.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/rightArrow", "Arrows").With("Positive", "<Keyboard>/leftArrow", "Arrows");

            LeftStick = map.AddAction("Left stick", InputActionType.Value, "<Gamepad>/leftStick", expectedControlLayout: "Vector2");
            RightStick = map.AddAction("Right stick", InputActionType.Value, "<Gamepad>/rightStick", expectedControlLayout: "Vector2");

            Shoot = Button(map, "Shoot (high goal)", "<Keyboard>/space", "<Gamepad>/rightTrigger");
            Intake = Button(map, "Intake FUEL (hold)", "<Keyboard>/q", "<Gamepad>/leftTrigger");
            Dump = Button(map, "Dump (low goal)", "<Keyboard>/z", "<Gamepad>/buttonNorth");
            PlaceGear = Button(map, "Place GEAR", "<Keyboard>/e", "<Gamepad>/buttonSouth");
            Climb = Button(map, "Climb (hold)", "<Keyboard>/t", "<Gamepad>/buttonWest");
            ChuteFuel = Button(map, "Loading station: FUEL", "<Keyboard>/f", "<Gamepad>/leftShoulder");
            ChuteGear = Button(map, "Loading station: GEAR", "<Keyboard>/g", "<Gamepad>/rightShoulder");
            Precision = Button(map, "Precision", "<Keyboard>/shift", "<Gamepad>/rightStickPress");
            NextCamera = Button(map, "Next camera", "<Keyboard>/c", "<Gamepad>/dpad/up");
            FlipChase = Button(map, "Flip chase camera", "<Keyboard>/v", "<Gamepad>/leftStickPress");
            ResetRobot = Button(map, "Reset robot", "<Keyboard>/r", "<Gamepad>/dpad/down");
            RestartMatch = Button(map, "Restart match", "<Keyboard>/enter", "<Gamepad>/start");
            Pause = Button(map, "Pause", "<Keyboard>/escape", "<Gamepad>/select");

            Entries.Clear();
            Entries.Add(new Entry { label = "Drive forward", action = Throttle, keyboardIndex = 2, driveDirection = 0 });
            Entries.Add(new Entry { label = "Drive backward", action = Throttle, keyboardIndex = 1, driveDirection = 1 });
            Entries.Add(new Entry { label = "Turn left", action = Turn, keyboardIndex = 2, driveDirection = 2 });
            Entries.Add(new Entry { label = "Turn right", action = Turn, keyboardIndex = 1, driveDirection = 3 });
            foreach (var a in new[] { Shoot, Intake, Dump, PlaceGear, Climb, ChuteFuel, ChuteGear, Precision, NextCamera, FlipChase, ResetRobot, RestartMatch, Pause })
                Entries.Add(new Entry { label = a == Precision ? "Precision (slow) driving" : a.name, action = a, keyboardIndex = 0, gamepadIndex = 1 });

            string json = PlayerPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { Asset.LoadBindingOverridesFromJson(json); }
                catch (Exception e) { Debug.LogWarning("[Steamworks] Could not load saved key bindings: " + e.Message); }
            }
            Asset.Enable();
        }

        static InputAction Button(InputActionMap map, string name, string keyboard, string gamepad)
        {
            var a = map.AddAction(name, InputActionType.Button);
            a.AddBinding(keyboard, groups: KeyboardGroup);
            a.AddBinding(gamepad, groups: GamepadGroup);
            return a;
        }

        public static void Save()
        {
            PlayerPrefs.SetString(PrefsKey, Asset.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        public static void ResetToDefaults()
        {
            Asset.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
        }

        public static string Display(InputAction a, int index)
        {
            if (a == null || index < 0 || index >= a.bindings.Count) return "-";
            string s = a.GetBindingDisplayString(index);
            return string.IsNullOrEmpty(s) ? "-" : s;
        }

        /// <summary>"Enter / Start" style text for on-screen hints.</summary>
        public static string Both(InputAction a) => $"{Display(a, 0)} / {Display(a, 1)}";

        /// <summary>What drives the robot in each direction on a gamepad, for the Controls menu.</summary>
        public static string GamepadDrive(int direction)
        {
            switch (GameSettings.Layout)
            {
                case StickLayout.SingleStick:
                    return new[] { "Left stick up", "Left stick down", "Left stick left", "Left stick right" }[direction];
                case StickLayout.Tank:
                    return new[] { "Both sticks up", "Both sticks down", "Right stick up, left down", "Left stick up, right down" }[direction];
                default:
                    return new[] { "Left stick up", "Left stick down", "Right stick left", "Right stick right" }[direction];
            }
        }

        /// <summary>
        /// Listens for the next key (or gamepad button) and binds it. Escape cancels; gives up after 8 s.
        /// </summary>
        public static void StartRebind(InputAction action, int bindingIndex, bool gamepad, Action finished)
        {
            action.Disable();
            var op = action.PerformInteractiveRebinding(bindingIndex)
                .WithExpectedControlType("Button")
                .WithCancelingThrough("<Keyboard>/escape")
                .WithControlsExcluding("<Mouse>")
                .WithTimeout(8f)
                .OnMatchWaitForAnother(0.05f);
            if (gamepad)
                op.WithControlsHavingToMatchPath("<Gamepad>")
                  .WithControlsExcluding("<Gamepad>/leftStick")
                  .WithControlsExcluding("<Gamepad>/rightStick");
            else
                op.WithControlsHavingToMatchPath("<Keyboard>")
                  .WithControlsExcluding("<Keyboard>/anyKey");
            op.OnComplete(o => { o.Dispose(); action.Enable(); Save(); finished?.Invoke(); });
            op.OnCancel(o => { o.Dispose(); action.Enable(); finished?.Invoke(); });
            op.Start();
        }
    }
}
