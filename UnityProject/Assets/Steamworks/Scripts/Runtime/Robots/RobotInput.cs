namespace SteamworksSim.Robots
{
    /// <summary>
    /// Everything a driver can do in one physics tick. Keyboard, gamepad, network clients and (later)
    /// autonomous routines all produce this, and only the simulation authority turns it into motion.
    /// </summary>
    [System.Serializable]
    public struct RobotInput
    {
        public float throttle;   // -1..1, forward positive
        public float turn;       // -1..1, LEFT positive (counter-clockwise seen from above)
        public bool shoot;       // held (step 3)
        public bool dump;        // held (step 3)
        public bool placeGear;   // pressed this tick (step 3)
        public bool climb;       // pressed this tick (step 3)

        public static RobotInput Idle => default;
    }
}
