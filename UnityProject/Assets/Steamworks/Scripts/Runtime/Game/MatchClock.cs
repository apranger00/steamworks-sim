using System;

namespace SteamworksSim.Game
{
    public enum MatchPhase { NotStarted, Auto, Teleop, Over }

    /// <summary>Moments the field announces with a sound.</summary>
    public enum MatchCue
    {
        MatchStart, // 2:30, AUTO begins (cavalry charge)
        AutoEnd,    // 0:15 elapsed, AUTO ends and TELEOP begins (buzzer)
        Endgame,    // 0:30 left, PILOTS drop the ROPES (steam whistle)
        MatchEnd,   // 0:00 (buzzer)
    }

    /// <summary>
    /// Match timing (2017 manual 3.4): 15 s AUTO then 2:15 TELEOP, the last 30 s being the endgame.
    /// For the video game both periods are driver-controlled. Plain C#, ticked from FixedUpdate,
    /// so it runs at simulation time (stops when paused). Scoring joins it in step 3.
    /// </summary>
    public class MatchClock
    {
        public const float AutoLength = 15f, TeleopLength = 135f, EndgameLength = 30f;
        public const float MatchLength = AutoLength + TeleopLength;

        public MatchPhase Phase { get; private set; } = MatchPhase.NotStarted;
        double elapsed; // double so 18,000 ticks of 1/120 s add up exactly enough
        public float Elapsed => (float)elapsed;
        public bool InEndgame { get; private set; }
        public bool Running => Phase == MatchPhase.Auto || Phase == MatchPhase.Teleop;

        public event Action<MatchCue> Cue;

        /// <summary>Seconds left in the current period (what the field display shows).</summary>
        public float PeriodLeft
        {
            get
            {
                switch (Phase)
                {
                    case MatchPhase.Auto: return (float)Math.Max(0.0, AutoLength - elapsed);
                    case MatchPhase.Teleop: return (float)Math.Max(0.0, MatchLength - elapsed);
                    default: return 0f;
                }
            }
        }

        public void Start()
        {
            Phase = MatchPhase.Auto; elapsed = 0.0; InEndgame = false;
            Cue?.Invoke(MatchCue.MatchStart);
        }

        public void Stop() { Phase = MatchPhase.NotStarted; elapsed = 0.0; InEndgame = false; }

        public void Tick(float dt)
        {
            if (!Running || dt <= 0f) return;
            elapsed += dt;
            const double eps = 1e-6;
            if (Phase == MatchPhase.Auto && elapsed >= AutoLength - eps)
            {
                Phase = MatchPhase.Teleop;
                Cue?.Invoke(MatchCue.AutoEnd);
            }
            if (Phase == MatchPhase.Teleop && !InEndgame && elapsed >= MatchLength - EndgameLength - eps)
            {
                InEndgame = true;
                Cue?.Invoke(MatchCue.Endgame);
            }
            if (Phase == MatchPhase.Teleop && elapsed >= MatchLength - eps)
            {
                elapsed = MatchLength;
                Phase = MatchPhase.Over;
                Cue?.Invoke(MatchCue.MatchEnd);
            }
        }
    }
}
