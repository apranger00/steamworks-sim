using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SteamworksSim.Cameras
{
    /// <summary>
    /// Free look-around camera for the field (step 1). Right-drag orbits, middle-drag pans,
    /// scroll zooms; keys 1-4 jump to presets. Also the menu background (autoSpin, allowInput off).
    /// </summary>
    public class FieldOrbitCamera : MonoBehaviour
    {
        public enum Preset { Overview, TopDown, RedWall, BlueWall }

        public Vector3 target = new Vector3(0f, 0f, 0f);
        public float distance = 17f;
        public float yaw = 0f;     // degrees around +y
        public float pitch = 32f;  // degrees above the horizon
        public float orbitSpeed = 0.25f, panSpeed = 0.0025f, zoomSpeed = 0.0015f;
        [Tooltip("Degrees per second of automatic turning (menu backgrounds).")]
        public float autoSpin;
        [Tooltip("Mouse and preset keys move the camera.")]
        public bool allowInput = true;

        Vector3 goalTarget; float goalDistance, goalYaw, goalPitch; bool tweening;

        void OnEnable()
        {
            goalTarget = target; goalDistance = distance; goalYaw = yaw; goalPitch = pitch;
            Apply();
        }

        public void SnapTo(Preset p)
        {
            SetPreset(p);
            target = goalTarget; distance = goalDistance; yaw = goalYaw; pitch = goalPitch;
            tweening = false;
            Apply();
        }

        /// <summary>Jumps straight to a view (no tween).</summary>
        public void Set(Vector3 newTarget, float newDistance, float newYaw, float newPitch)
        {
            target = goalTarget = newTarget; distance = goalDistance = newDistance;
            yaw = goalYaw = newYaw; pitch = goalPitch = newPitch;
            tweening = false;
            Apply();
        }

        void SetPreset(Preset p)
        {
            // Unity: x toward the blue wall, z toward the scoring table, y up.
            switch (p)
            {
                case Preset.Overview: goalTarget = Vector3.zero; goalDistance = 17f; goalYaw = 0f; goalPitch = 32f; break;
                case Preset.TopDown: goalTarget = Vector3.zero; goalDistance = 24f; goalYaw = 0f; goalPitch = 89f; break;
                case Preset.RedWall: goalTarget = new Vector3(0f, 0.8f, 0f); goalDistance = 10.4f; goalYaw = 90f; goalPitch = 8f; break;
                case Preset.BlueWall: goalTarget = new Vector3(0f, 0.8f, 0f); goalDistance = 10.4f; goalYaw = -90f; goalPitch = 8f; break;
            }
            tweening = true;
        }

        void LateUpdate()
        {
            float dx = 0f, dy = 0f, scroll = 0f; bool orbit = false, pan = false;
#if ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            if (m != null)
            {
                var d = m.delta.ReadValue(); dx = d.x; dy = d.y;
                scroll = m.scroll.ReadValue().y;
                orbit = m.rightButton.isPressed; pan = m.middleButton.isPressed;
            }
            var k = allowInput ? Keyboard.current : null;
            if (k != null)
            {
                if (k.digit1Key.wasPressedThisFrame) SetPreset(Preset.Overview);
                if (k.digit2Key.wasPressedThisFrame) SetPreset(Preset.TopDown);
                if (k.digit3Key.wasPressedThisFrame) SetPreset(Preset.RedWall);
                if (k.digit4Key.wasPressedThisFrame) SetPreset(Preset.BlueWall);
            }
#else
            dx = Input.GetAxis("Mouse X") * 10f; dy = Input.GetAxis("Mouse Y") * 10f;
            scroll = Input.mouseScrollDelta.y * 120f;
            orbit = Input.GetMouseButton(1); pan = Input.GetMouseButton(2);
            if (allowInput && Input.GetKeyDown(KeyCode.Alpha1)) SetPreset(Preset.Overview);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetPreset(Preset.TopDown);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetPreset(Preset.RedWall);
            if (Input.GetKeyDown(KeyCode.Alpha4)) SetPreset(Preset.BlueWall);
#endif
            if (!allowInput) { dx = dy = scroll = 0f; orbit = pan = false; }
            if (autoSpin != 0f) { yaw += autoSpin * Time.unscaledDeltaTime; goalYaw = yaw; }
            if (orbit || pan || Mathf.Abs(scroll) > 0.01f) tweening = false;
            if (tweening)
            {
                float a = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 6f);
                target = Vector3.Lerp(target, goalTarget, a);
                distance = Mathf.Lerp(distance, goalDistance, a);
                yaw = Mathf.LerpAngle(yaw, goalYaw, a);
                pitch = Mathf.Lerp(pitch, goalPitch, a);
            }
            if (orbit) { yaw += dx * orbitSpeed; pitch = Mathf.Clamp(pitch - dy * orbitSpeed, 2f, 89f); }
            if (pan)
            {
                var right = transform.right; var fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                target -= (right * dx + fwd * dy) * panSpeed * distance;
            }
            if (Mathf.Abs(scroll) > 0.01f) distance = Mathf.Clamp(distance * (1f - scroll * zoomSpeed), 1f, 60f);
            Apply();
        }

        void Apply()
        {
            // yaw 0: camera on the far side (-z, behind the boilers) looking across toward the scoring table
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = target + rot * new Vector3(0f, 0f, -distance);
            transform.rotation = rot;
        }
    }
}
