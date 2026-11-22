using System.Collections.Generic;
using UnityEngine;

namespace SteamworksSim.Robots
{
    /// <summary>Spins the wheels from the drivetrain's wheel surface speeds.</summary>
    public class RobotVisual : MonoBehaviour
    {
        TankDrive drive; float radius; List<Transform> left, right; float aL, aR;

        public void Init(TankDrive d, float wheelRadius, List<Transform> l, List<Transform> r)
        {
            drive = d; radius = Mathf.Max(0.01f, wheelRadius); left = l; right = r;
        }

        void Update()
        {
            if (drive == null) return;
            aL += drive.LeftWheelSpeed / radius * Mathf.Rad2Deg * Time.deltaTime;
            aR += drive.RightWheelSpeed / radius * Mathf.Rad2Deg * Time.deltaTime;
            foreach (var w in left) w.localRotation = Quaternion.Euler(aL, 0f, 0f);
            foreach (var w in right) w.localRotation = Quaternion.Euler(aR, 0f, 0f);
        }
    }
}
