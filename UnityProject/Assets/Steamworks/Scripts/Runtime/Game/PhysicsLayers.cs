using UnityEngine;

namespace SteamworksSim.Game
{
    /// <summary>
    /// Two physics layers the game sets up at runtime (no project settings needed): loose GEARS, and the lid over
    /// each open-top robot hopper, which only GEARS collide with. So FUEL still drops into a hopper while GEARS
    /// bounce off it instead of landing inside.
    /// </summary>
    public static class PhysicsLayers
    {
        public const int Gear = 31;
        public const int GearLid = 30;

        static bool done;

        public static void Setup()
        {
            if (done) return;
            done = true;
            for (int l = 0; l < 32; l++) Physics.IgnoreLayerCollision(GearLid, l, l != Gear);
        }
    }
}
