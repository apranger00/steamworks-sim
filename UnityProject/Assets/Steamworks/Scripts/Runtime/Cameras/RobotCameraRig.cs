using UnityEngine;

namespace SteamworksSim.Cameras
{
    /// <summary>
    /// Camera modes for driving (same as the prototype):
    ///  - Chase: 130 in behind and 94 in above the robot, looking at a point 28 in ahead at 8 in high.
    ///           Flip (L3 / V) puts it in front of the robot looking back; the flip cuts instantly.
    ///  - Driver: eye height (66 in) behind your alliance wall at your station, following the robot.
    ///  - Free: hands over to FieldOrbitCamera, centred on the robot.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class RobotCameraRig : MonoBehaviour
    {
        public enum Mode { Chase, Driver, Free }

        public Mode mode = Mode.Chase;
        public Transform robot;
        public bool chaseReversed;
        [Tooltip("Spec frame: alliance side (-1 red, +1 blue) and station Y in inches, for the driver camera.")]
        public int allianceSide = -1;
        public float stationY = -16f;

        public float chaseBack = 130f, chaseHeight = 94f, chaseAhead = 28f, chaseLookHeight = 8f; // inches
        public float driverEyeHeight = 66f, driverBehindWall = 352f;                               // inches (X from centre)

        FieldOrbitCamera orbit;
        Vector3 pos, look; bool snapped;

        void Awake()
        {
            orbit = GetComponent<FieldOrbitCamera>();
            var cam = GetComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 200f);
            Apply();
        }

        public void SetMode(Mode m)
        {
            mode = m; snapped = false; overhead = false;
            Apply();
        }

        public void NextMode() => SetMode((Mode)(((int)mode + 1) % 3));

        public void FlipChase()
        {
            chaseReversed = !chaseReversed;
            if (mode != Mode.Chase) SetMode(Mode.Chase); else snapped = false;
        }

        public void Snap() => snapped = false;

        bool overhead;
        /// <summary>A fixed view straight down (SET AUTO): centre `at`, `height` m up, the screen's top toward yawDeg.</summary>
        public void ShowOverhead(Vector3 at, float height, float yawDeg)
        {
            overhead = true; mode = Mode.Free;
            if (orbit != null) orbit.enabled = false;
            transform.SetPositionAndRotation(at + Vector3.up * height, Quaternion.Euler(90f, yawDeg, 0f));
        }

        /// <summary>Slowly circling view for menus (free camera, no mouse control).</summary>
        public void ShowOrbit(Vector3 target, float distance, float yaw, float pitch, float spin)
        {
            mode = Mode.Free; snapped = false; overhead = false;
            if (orbit == null) return;
            orbit.enabled = true;
            orbit.Set(target, distance, yaw, pitch);
            orbit.autoSpin = spin;
            orbit.allowInput = false;
        }

        /// <summary>Mouse control of the free camera (off while paused).</summary>
        public void AllowOrbitInput(bool allow)
        {
            if (orbit != null && orbit.autoSpin == 0f) orbit.allowInput = allow;
        }

        void Apply()
        {
            if (orbit != null)
            {
                // set the orbit target before enabling, because enabling snaps the orbit camera to its target
                if (mode == Mode.Free && robot != null) orbit.target = robot.position + Vector3.up * 0.3f;
                orbit.autoSpin = 0f;
                orbit.allowInput = true;
                orbit.enabled = mode == Mode.Free;
            }
        }

        void LateUpdate()
        {
            if (overhead || mode == Mode.Free || robot == null) return;
            const float k = Units.Inch;
            Vector3 fwd = robot.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 p = robot.position;
            Vector3 wantPos, wantLook;
            if (mode == Mode.Chase)
            {
                float d = chaseReversed ? -1f : 1f;
                wantPos = p - fwd * (d * chaseBack * k) + Vector3.up * (chaseHeight * k);
                wantLook = p + fwd * (d * chaseAhead * k) + Vector3.up * (chaseLookHeight * k);
            }
            else
            {
                wantPos = Units.ToUnity(allianceSide * driverBehindWall, stationY, driverEyeHeight);
                wantLook = p + Vector3.up * (12f * k);
            }
            if (!snapped) { pos = wantPos; look = wantLook; snapped = true; }
            float a = 1f - Mathf.Exp(-Time.deltaTime * (mode == Mode.Chase ? 5f : 8f));
            pos = Vector3.Lerp(pos, wantPos, a);
            look = Vector3.Lerp(look, wantLook, a);
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(look - pos, Vector3.up);
        }
    }
}
