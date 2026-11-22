using UnityEngine;

namespace SteamworksSim.Robots
{
    /// <summary>Which robot definition and alliance a robot GameObject was built from.</summary>
    public class RobotInfo : MonoBehaviour
    {
        public RobotDefinition definition;
        public string alliance;
    }
}
