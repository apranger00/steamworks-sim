using System;

namespace SteamworksSim.Game
{
    public enum MatchPhase { NotStarted, Countdown, Auto, Teleop, Over }

    /// <summary>Moments the field announces with a sound.</summary>
    public enum MatchCue
    {
        MatchStart, // 2:30, after the 3-2-1 countdown: AUTO begins (cavalry charge)
        AutoEnd,    // 0:15 elapsed, AUTO ends and TELEOP begins (buzzer)
        Endgame,    // 0:30 left, PILOTS drop the ROPES (steam whistle)
        MatchEnd,   // 0:00 (buzzer)
    }

    /// <summary>
    /// Match timing (2017 manual 3.4): a 3-2-1 countdown (video game only; robots disabled), then 15 s AUTO
    /// and 2:15 TELEOP, the last 30 s being the endgame.
    /// For the video game both periods are driver-controlled. Plain C#, ticked from FixedUpdate,
    /// so it runs at simulation time (stops when paused). Scoring joins it in step 3.
    /// </summary>
    public class MatchClock
    {
        public const float CountdownLength = 3f, AutoLength = 15f, TeleopLength = 135f, EndgameLength = 30f;
        public const float MatchLength = AutoLength + TeleopLength;

        public MatchPhase Phase { get; private set; } = MatchPhase.NotStarted;
        double elapsed; // double so 18,000 ticks of 1/120 s add up exactly enough
        public float Elapsed => (float)elapsed;
        public bool InEndgame { get; private set; }
        public bool Running => Phase == MatchPhase.Auto || Phase == MatchPhase.Teleop;
        /// <summary>Seconds left in the 3-2-1 countdown (0 once the match is under way).</summary>
        public float CountdownLeft => Phase == MatchPhase.Countdown ? (float)Math.Max(0.0, countdown) : 0f;
        double countdown;

        public event Action<MatchCue> Cue;

        /// <summary>Seconds left in the current period (what the field display shows).</summary>
        public float PeriodLeft
        {
            get
            {
                switch (Phase)
                {
                    case MatchPhase.Countdown: return AutoLength;
                    case MatchPhase.Auto: return (float)Math.Max(0.0, AutoLength - elapsed);
                    case MatchPhase.Teleop: return (float)Math.Max(0.0, MatchLength - elapsed);
                    default: return 0f;
                }
            }
        }

        /// <summary>Starts the 3-2-1 countdown; the match itself begins when it reaches zero.</summary>
        public void Start()
        {
            Phase = MatchPhase.Countdown; countdown = CountdownLength; elapsed = 0.0; InEndgame = false;
        }

        public void Stop() { Phase = MatchPhase.NotStarted; elapsed = 0.0; countdown = 0.0; InEndgame = false; }

        public void Tick(float dt)
        {
            const double eps = 1e-6;
            if (dt <= 0f) return;
            if (Phase == MatchPhase.Countdown)
            {
                countdown -= dt;
                if (countdown > eps) return;
                countdown = 0.0;
                Phase = MatchPhase.Auto; elapsed = 0.0;
                Cue?.Invoke(MatchCue.MatchStart);
                return;
            }
            if (!Running) return;
            elapsed += dt;
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
