using SteamworksSim.Robots;
using SteamworksSim.Settings;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SteamworksSim.InputHandling
{
    /// <summary>
    /// Turns the player's keyboard and gamepad into a RobotInput (driving) plus one-shot actions.
    /// Bindings live in GameControls (rebindable in Options > Controls). Defaults:
    ///   Scoring: Space / RT shoot (hold), Q / LT intake (hold), Z / Y dump (hold), E / A place GEAR, T / X climb or let go,
    ///            F / LB FUEL from the loading station, G / RB GEAR from the loading station. R3 (hold) precision.
    ///   Keyboard: W/S (or arrows) drive, A/D turn, Shift precision, C camera, V flip chase, R reset robot,
    ///             Enter restart match, Esc pause. Practice keys: B defender, F5 reload robot files.
    ///   Gamepad:  sticks per the chosen layout, R3 precision, D-pad up camera, L3 flip chase, D-pad down reset robot,
    ///             Start restart match, Select (View / Back) pause, B back out of menus.
    /// </summary>
    public class PlayerInputReader
    {
        public enum Source { Keyboard, Gamepad }
        public Source LastSource { get; private set; } = Source.Keyboard;

        public bool NextCamera, FlipChase, ResetRobot, RestartMatch, Pause, Cancel, ToggleDefender, ReloadRobots;
        public bool PlaceGear, Climb, ChuteFuel, ChuteGear; // pressed this frame
        public bool ClimbHeld;                               // held

        const float Deadband = 0.08f, KeyboardTurn = 0.65f, PrecisionScale = 0.4f;

        static float Shape(float v)
        {
            float a = Mathf.Abs(v);
            if (a < Deadband) return 0f;
            float t = (a - Deadband) / (1f - Deadband);
            return Mathf.Sign(v) * t * t; // squared response for fine control
        }

        /// <summary>Call once per rendered frame (Update).</summary>
        public RobotInput Read()
        {
            GameControls.Init();
            var input = new RobotInput();

            float thr = GameControls.Throttle.ReadValue<float>();
            float turn = GameControls.Turn.ReadValue<float>() * KeyboardTurn;
            bool keyboardActive = thr != 0f || turn != 0f;

            Vector2 ls = GameControls.LeftStick.ReadValue<Vector2>(), rs = GameControls.RightStick.ReadValue<Vector2>();
            bool padActive = false;
            switch (GameSettings.Layout)
            {
                case StickLayout.SingleStick:
                {
                    float y = Shape(ls.y), x = -Shape(ls.x); // stick right = turn right = negative (left positive)
                    if (y != 0f || x != 0f) { thr = y; turn = x; padActive = true; }
                    break;
                }
                case StickLayout.Tank:
                {
                    float l = Shape(ls.y), r = Shape(rs.y);
                    if (l != 0f || r != 0f) { input.tank = true; input.left = l; input.right = r; padActive = true; }
                    break;
                }
                default:
                {
                    float y = Shape(ls.y), x = -Shape(rs.x);
                    if (y != 0f || x != 0f) { thr = y; turn = x; padActive = true; }
                    break;
                }
            }
            if (padActive) LastSource = Source.Gamepad;
            else if (keyboardActive) LastSource = Source.Keyboard;

            float scale = GameControls.Precision.IsPressed() ? PrecisionScale : 1f;
            input.throttle = Mathf.Clamp(thr, -1f, 1f) * scale;
            input.turn = Mathf.Clamp(turn, -1f, 1f) * scale;
            input.left = Mathf.Clamp(input.left, -1f, 1f) * scale;
            input.right = Mathf.Clamp(input.right, -1f, 1f) * scale;

            input.shoot = GameControls.Shoot.IsPressed();
            input.dump = GameControls.Dump.IsPressed();
            input.intake = GameControls.Intake.IsPressed();
            PlaceGear = input.placeGear = GameControls.PlaceGear.WasPressedThisFrame();
            Climb = input.climb = GameControls.Climb.WasPressedThisFrame();
            ClimbHeld = GameControls.Climb.IsPressed();
            ChuteFuel = GameControls.ChuteFuel.WasPressedThisFrame();
            ChuteGear = GameControls.ChuteGear.WasPressedThisFrame();
            NextCamera = GameControls.NextCamera.WasPressedThisFrame();
            FlipChase = GameControls.FlipChase.WasPressedThisFrame();
            ResetRobot = GameControls.ResetRobot.WasPressedThisFrame();
            RestartMatch = GameControls.RestartMatch.WasPressedThisFrame();
            Pause = GameControls.Pause.WasPressedThisFrame();

            // Fixed keys: menu back, and practice helpers
            Cancel = ToggleDefender = ReloadRobots = false;
            var k = Keyboard.current;
            if (k != null)
            {
                Cancel |= k.escapeKey.wasPressedThisFrame;
                ToggleDefender = k.bKey.wasPressedThisFrame;
                ReloadRobots = k.f5Key.wasPressedThisFrame;
            }
            var g = Gamepad.current;
            if (g != null) Cancel |= g.buttonEast.wasPressedThisFrame;
            return input;
        }
    }
}
