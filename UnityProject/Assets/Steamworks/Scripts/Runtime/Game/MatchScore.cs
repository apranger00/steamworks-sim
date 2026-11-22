using System.Collections.Generic;

namespace SteamworksSim.Game
{
    /// <summary>
    /// One alliance's points, split the way the field's score screen shows them (2017 manual Table 4-1).
    /// Filled from MatchRules (the port of prototype/web/game.js) every tick.
    /// </summary>
    public class AllianceScore
    {
        public readonly string alliance;
        public AllianceScore(string a) { alliance = a; }

        public int mobility;        // AUTO: 5 per robot that breaks the BASE LINE
        public int autoFuel;        // AUTO: 1 point per kPa
        public int autoRotors;      // AUTO: 60 per ROTOR turning
        public int teleopFuel;      // TELEOP: 1 point per kPa
        public int teleopRotors;    // TELEOP: 40 per ROTOR
        public int takeoff;         // TELEOP: 50 per robot ready for takeoff
        public int penalty;         // foul points awarded by the other alliance's fouls
        public int bonusPressure;   // playoff BONUS: 20 for 40 kPa
        public int bonusRotors;     // playoff BONUS: 100 for all four ROTORS
        public int bonus => bonusPressure + bonusRotors;

        public float kPa;           // pressure accumulated
        public int ninths;          // pressure in ninths of a kPa (the HUD's progress dots show ninths % 9)
        public int rotorsTurning;   // 0..4
        public readonly List<string> robots = new List<string>();

        public int Auto => mobility + autoFuel + autoRotors;
        public int Teleop => teleopFuel + teleopRotors + takeoff + bonus;
        public int Total => Auto + Teleop + penalty;

        public bool PressureRp => kPa >= 40f;     // 40 kPa: +1 ranking point
        public bool RotorsRp => rotorsTurning >= 4; // all four ROTORS: +1 ranking point

        public void Clear()
        {
            mobility = autoFuel = autoRotors = teleopFuel = teleopRotors = takeoff = penalty = bonusPressure = bonusRotors = 0;
            kPa = 0f; ninths = 0; rotorsTurning = 0;
            robots.Clear();
        }
    }

    public class MatchScore
    {
        public const int MobilityPoints = 5;
        public readonly AllianceScore red = new AllianceScore("red"), blue = new AllianceScore("blue");

        public AllianceScore Of(string alliance) => alliance == "blue" ? blue : red;
        public AllianceScore Other(string alliance) => alliance == "blue" ? red : blue;

        public void Clear() { red.Clear(); blue.Clear(); }

        /// <summary>"red", "blue" or "tie".</summary>
        public string Winner => red.Total > blue.Total ? "red" : blue.Total > red.Total ? "blue" : "tie";

        /// <summary>Qualification ranking points: win 2, tie 1, plus 1 each for 40 kPa and four ROTORS.</summary>
        public int RankingPoints(AllianceScore a)
        {
            string w = Winner;
            int rp = w == "tie" ? 1 : w == a.alliance ? 2 : 0;
            if (a.PressureRp) rp++;
            if (a.RotorsRp) rp++;
            return rp;
        }
    }
}
