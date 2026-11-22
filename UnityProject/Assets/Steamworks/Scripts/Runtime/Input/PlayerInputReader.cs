using SteamworksSim.Robots;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SteamworksSim.InputHandling
{
    /// <summary>
    /// Reads keyboard and gamepad into a RobotInput (driving) plus one-shot UI actions.
    /// Mapping matches the browser prototype:
    ///   Keyboard: W/S or arrows drive, A/D turn, Shift precision (40%), C next camera, V flip chase camera,
    ///             R reset robot, B add/remove defender, F1 alliance, F2 start station, F3 next robot, F5 reload robot files.
    ///   Gamepad:  left stick Y drive, right stick X turn, LB precision, Y next camera, L3 flip chase camera,
    ///             Back reset robot.
    /// </summary>
    public class PlayerInputReader
    {
        public enum Source { Keyboard, Gamepad }
        public Source LastSource { get; private set; } = Source.Keyboard;

        public bool NextCamera, FlipChase, ResetRobot, ToggleDefender, NextAlliance, NextStation, NextRobot, ReloadRobots;

        const float Deadband = 0.08f, KeyboardTurn = 0.65f, Precision = 0.4f;

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
            NextCamera = FlipChase = ResetRobot = ToggleDefender = NextAlliance = NextStation = NextRobot = ReloadRobots = false;
            float thr = 0f, turn = 0f; bool precise = false, kbActive = false;
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k != null)
            {
                if (k.wKey.isPressed || k.upArrowKey.isPressed) thr += 1f;
                if (k.sKey.isPressed || k.downArrowKey.isPressed) thr -= 1f;
                if (k.aKey.isPressed || k.leftArrowKey.isPressed) turn += KeyboardTurn;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed) turn -= KeyboardTurn;
                precise |= k.shiftKey.isPressed;
                kbActive = thr != 0f || turn != 0f;
                NextCamera |= k.cKey.wasPressedThisFrame;
                FlipChase |= k.vKey.wasPressedThisFrame;
                ResetRobot |= k.rKey.wasPressedThisFrame;
                ToggleDefender |= k.bKey.wasPressedThisFrame;
                NextAlliance |= k.f1Key.wasPressedThisFrame;
                NextStation |= k.f2Key.wasPressedThisFrame;
                NextRobot |= k.f3Key.wasPressedThisFrame;
                ReloadRobots |= k.f5Key.wasPressedThisFrame;
            }
            var g = Gamepad.current;
            if (g != null)
            {
                float gy = Shape(g.leftStick.ReadValue().y);
                float gx = -Shape(g.rightStick.ReadValue().x); // stick right = turn right = negative (left positive)
                if (gy != 0f || gx != 0f) { thr = gy; turn = gx; LastSource = Source.Gamepad; }
                else if (kbActive) LastSource = Source.Keyboard;
                precise |= g.leftShoulder.isPressed;
                NextCamera |= g.buttonNorth.wasPressedThisFrame;
                FlipChase |= g.leftStickButton.wasPressedThisFrame;
                ResetRobot |= g.selectButton.wasPressedThisFrame;
            }
            else if (kbActive) LastSource = Source.Keyboard;
#else
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) thr += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) thr -= 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) turn += KeyboardTurn;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) turn -= KeyboardTurn;
            precise = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            NextCamera = Input.GetKeyDown(KeyCode.C); FlipChase = Input.GetKeyDown(KeyCode.V);
            ResetRobot = Input.GetKeyDown(KeyCode.R); ToggleDefender = Input.GetKeyDown(KeyCode.B);
            NextAlliance = Input.GetKeyDown(KeyCode.F1); NextStation = Input.GetKeyDown(KeyCode.F2);
            NextRobot = Input.GetKeyDown(KeyCode.F3); ReloadRobots = Input.GetKeyDown(KeyCode.F5);
#endif
            float scale = precise ? Precision : 1f;
            return new RobotInput { throttle = Mathf.Clamp(thr, -1f, 1f) * scale, turn = Mathf.Clamp(turn, -1f, 1f) * scale };
        }
    }
}
