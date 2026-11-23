using SteamworksSim.Robots;
using UnityEngine;

namespace SteamworksSim.Game
{
    /// <summary>
    /// Robot selection: a robot drives itself on or off the field (scripted, no physics). Leave: it turns toward the
    /// exit and speeds away, then is removed. Arrive: it comes in from off screen, slows to a stop on its spot and
    /// turns to face the camera. Positions are spec inches, headings spec degrees (counter-clockwise from +X).
    /// </summary>
    public class PreviewDrive : MonoBehaviour
    {
        Vector2 from, to; float h0, h1, hEnd, t, duration, delay; bool leaving;
        Rigidbody rb; TankDrive drive;

        public static void Leave(GameObject robot, Vector2 exit, float duration = 1.3f) => Get(robot).Begin(true, exit, 0f, duration, 0f);

        public static void Arrive(GameObject robot, Vector2 entry, Vector2 spot, float heading, float delay = 0.35f, float duration = 1.6f)
        {
            var d = Get(robot);
            d.Set(entry, Mathf.Atan2(spot.y - entry.y, spot.x - entry.x) * Mathf.Rad2Deg);
            d.Begin(false, spot, heading, duration, delay);
        }

        /// <summary>True while a robot is still driving in.</summary>
        public static bool Busy(GameObject robot) { var d = robot != null ? robot.GetComponent<PreviewDrive>() : null; return d != null && !d.leaving; }

        static PreviewDrive Get(GameObject robot)
        {
            var d = robot.GetComponent<PreviewDrive>();
            if (d == null) d = robot.AddComponent<PreviewDrive>();
            d.rb = robot.GetComponent<Rigidbody>();
            d.drive = robot.GetComponent<TankDrive>();
            if (d.drive != null) d.drive.enabled = false;
            if (d.rb != null) { d.rb.linearVelocity = Vector3.zero; d.rb.angularVelocity = Vector3.zero; d.rb.isKinematic = true; }
            return d;
        }

        void Begin(bool leave, Vector2 target, float endHeading, float dur, float wait)
        {
            leaving = leave; t = 0f; duration = dur; delay = wait;
            from = Pos(); h0 = Heading();
            to = target; hEnd = endHeading;
            h1 = Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg; // direction of travel
            if (leave) gameObject.name += " (leaving)";
        }

        void Set(Vector2 p, float heading)
        {
            var pos = Units.ToUnity(p.x, p.y, 0f); var rot = Quaternion.Euler(0f, 90f - heading, 0f);
            transform.SetPositionAndRotation(pos, rot);
            if (rb != null) { rb.position = pos; rb.rotation = rot; }
        }

        Vector2 Pos() { var s = Units.ToSpec(transform.position); return new Vector2(s.x, s.y); }
        float Heading() { var f = transform.forward; return Mathf.Atan2(f.z, f.x) * Mathf.Rad2Deg; }

        void Update()
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01((t - delay) / duration);
            if (t < delay) return;
            Vector2 p; float h;
            if (leaving)
            {
                // swing round to the way out over the first third, speeding up all the way (ease in)
                p = Vector2.LerpUnclamped(from, to, u * u);
                h = Mathf.LerpAngle(h0, h1, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 3f)));
            }
            else
            {
                // drive in, slowing to a stop (ease out), then turn to face the camera over the last part
                float e = 1f - (1f - u) * (1f - u);
                p = Vector2.LerpUnclamped(from, to, e);
                h = Mathf.LerpAngle(h1, hEnd, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - 0.6f) / 0.4f)));
            }
            Set(p, h);
            if (u < 1f) return;
            if (leaving) { Destroy(gameObject); return; }
            if (rb != null) rb.isKinematic = false;
            if (drive != null) drive.enabled = true;
            Destroy(this);
        }
    }
}
