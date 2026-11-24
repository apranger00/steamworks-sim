using UnityEngine;

namespace SteamworksSim.Robots
{
    /// <summary>
    /// Traction-limited tank (skid-steer) drive on a PhysX rigidbody. Same model as the browser prototype
    /// (prototype/web/physics.js, Robot.drive), tuned in the prototype and set from robot.json:
    ///  - wheel speeds L/R = v -/+ w * track/2, scaled so neither exceeds free speed (turning at speed costs speed)
    ///  - speeding up in the direction of travel is motor-limited: a = stall * (1 - |v| / vmax), capped at traction * g
    ///  - slowing down or reversing is traction-limited (traction * g)
    ///  - side slip is resisted at sideScrub * traction * g
    ///  - an idle robot resists being pushed at brake * traction * g
    /// The controller adds bounded velocity changes each physics tick; PhysX resolves contacts, so pushing
    /// matches between robots emerge from mass and these limits.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class TankDrive : MonoBehaviour
    {
        public RobotInput input;
        [Tooltip("Idle robots hold position with braked wheels (test defender).")]
        public bool idle;

        public float TopSpeed { get; private set; }      // m/s
        public float LeftWheelSpeed { get; private set; }  // m/s surface speed
        public float RightWheelSpeed { get; private set; }
        public float Speed => rb != null ? new Vector2(rb.linearVelocity.x, rb.linearVelocity.z).magnitude : 0f;

        const float G = 9.81f, FtToM = 0.3048f;
        Rigidbody rb;
        float stall, traction, sideScrub, brake, maxTurn, track, strafeTop;
        bool mecanum;
        public bool Mecanum => mecanum;

        public void Configure(RobotDefinition d)
        {
            rb = GetComponent<Rigidbody>();
            var dt = d.drivetrain;
            TopSpeed = dt.topSpeed * FtToM;
            stall = dt.acceleration * FtToM;
            traction = dt.traction;
            sideScrub = dt.sideScrub;
            brake = dt.brake;
            maxTurn = dt.maxTurnRate * Mathf.Deg2Rad;
            track = Mathf.Max(0.1f, dt.trackWidth * Units.Inch);
            mecanum = dt.type == "mecanum";
            strafeTop = TopSpeed * Mathf.Clamp(dt.strafeSpeed, 0.1f, 1f);
        }

        void Awake() { rb = GetComponent<Rigidbody>(); }

        void FixedUpdate()
        {
            if (rb == null || TopSpeed <= 0f) return;
            float dt = Time.fixedDeltaTime;

            Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x); // Unity is left-handed: right of forward
            Vector3 v = rb.linearVelocity;
            float vf = Vector3.Dot(v, fwd), vr = Vector3.Dot(v, right);
            float w = -rb.angularVelocity.y; // counter-clockwise seen from above is positive (left turn)

            float tv = 0f, tw = 0f, ts = 0f; // targets: forward and sideways speed (m/s), turn rate (rad/s)
            if (!idle && mecanum && !input.tank)
            {
                // mecanum, robot-oriented: forward, strafe and turn share the wheels' speed (each wheel runs
                // f +- s +- r), so together they never ask more than full speed of any wheel
                float f = Mathf.Clamp(input.throttle, -1f, 1f), s = Mathf.Clamp(input.strafe, -1f, 1f), r = Mathf.Clamp(input.turn, -1f, 1f);
                float sum = Mathf.Abs(f) + Mathf.Abs(s) + Mathf.Abs(r);
                if (sum > 1f) { f /= sum; s /= sum; r /= sum; }
                tv = f * TopSpeed; ts = s * strafeTop; tw = r * maxTurn;
            }
            else if (!idle)
            {
                float half = track / 2f;
                if (input.tank)
                {
                    // each stick drives one side; spinning is still limited to the robot's max turn rate
                    float l = Mathf.Clamp(input.left, -1f, 1f) * TopSpeed, r = Mathf.Clamp(input.right, -1f, 1f) * TopSpeed;
                    tv = (l + r) / 2f;
                    tw = Mathf.Clamp((r - l) / track, -maxTurn, maxTurn);
                }
                else
                {
                    tv = Mathf.Clamp(input.throttle, -1f, 1f) * TopSpeed;
                    tw = Mathf.Clamp(input.turn, -1f, 1f) * maxTurn;
                }
                float L = tv - tw * half, R = tv + tw * half;
                float m = Mathf.Max(Mathf.Abs(L), Mathf.Abs(R));
                if (m > TopSpeed) { L *= TopSpeed / m; R *= TopSpeed / m; tv = (L + R) / 2f; tw = (R - L) / (2f * half); }
            }

            if (!idle && mecanum && input.tank) ts = Mathf.Clamp(input.strafe, -1f, 1f) * strafeTop;

            float tg = traction * G;
            float dv = tv - vf, lim;
            if (idle) lim = tg * brake;
            else if (Mathf.Abs(vf) < 1e-4f || Mathf.Sign(dv) == Mathf.Sign(vf)) lim = Mathf.Min(tg, stall * Mathf.Max(0f, 1f - Mathf.Abs(vf) / TopSpeed));
            else lim = tg;

            float dvf = Mathf.Clamp(dv, -lim * dt, lim * dt);
            float side = tg * sideScrub * dt;
            // tank wheels resist sliding sideways; mecanum rollers drive sideways (toward the strafe target) but give
            // way much more easily, so a mecanum robot is pushed around more
            float dvr = mecanum && !idle ? Mathf.Clamp(ts - vr, -side, side) : Mathf.Clamp(-vr, -side, side);
            float angAccel = 2f * Mathf.Min(tg, stall) / track;
            float dw = Mathf.Clamp(tw - w, -angAccel * dt, angAccel * dt);

            rb.AddForce(fwd * dvf + right * dvr, ForceMode.VelocityChange);
            rb.AddTorque(Vector3.up * -dw, ForceMode.VelocityChange);

            float vfNew = vf + dvf, wNew = w + dw;
            LeftWheelSpeed = vfNew - wNew * track / 2f;
            RightWheelSpeed = vfNew + wNew * track / 2f;
        }
    }
}
