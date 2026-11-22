using System;

namespace SteamworksSim.Game
{
    public enum MatchPhase { NotStarted, Countdown, Auto, AutoPause, Teleop, Over }

    /// <summary>Moments the field announces with a sound.</summary>
    public enum MatchCue
    {
        MatchStart,  // after the 3-2-1 countdown: AUTO begins (cavalry charge)
        AutoEnd,     // AUTO ends (buzzer); robots wait 3 s
        TeleopStart, // TELEOP begins after the pause (resume sound)
        Endgame,     // 0:30 left, PILOTS drop the ROPES (steam whistle)
        MatchEnd,    // 0:00 (buzzer)
    }

    /// <summary>
    /// Match timing: a 3-2-1 countdown, 15 s AUTO, a 3 s pause, then 2:15 TELEOP whose last 30 s are the
    /// endgame (2017 manual 3.4; the countdown and pause are video-game additions, robots are disabled
    /// in both). Both periods are driver-controlled. Plain C#, ticked from FixedUpdate at simulation time.
    /// </summary>
    public class MatchClock
    {
        public const float CountdownLength = 3f, AutoLength = 15f, PauseLength = 3f, TeleopLength = 135f, EndgameLength = 30f;

        public MatchPhase Phase { get; private set; } = MatchPhase.NotStarted;
        public bool InEndgame { get; private set; }
        /// <summary>Robots may drive (AUTO or TELEOP).</summary>
        public bool Running => Phase == MatchPhase.Auto || Phase == MatchPhase.Teleop;

        double left; // seconds left in the current phase (double so 1/120 s ticks land exactly)

        public event Action<MatchCue> Cue;

        /// <summary>What the timer shows: seconds left in the current phase (AUTO shows 0:15 during the countdown).</summary>
        public float PeriodLeft
        {
            get
            {
                switch (Phase)
                {
                    case MatchPhase.Countdown: return AutoLength;
                    case MatchPhase.Auto:
                    case MatchPhase.AutoPause:
                    case MatchPhase.Teleop: return (float)Math.Max(0.0, left);
                    default: return 0f;
                }
            }
        }

        /// <summary>Seconds left in the 3-2-1 countdown (0 once the match is under way).</summary>
        public float CountdownLeft => Phase == MatchPhase.Countdown ? (float)Math.Max(0.0, left) : 0f;

        /// <summary>Starts the 3-2-1 countdown; the match itself begins when it reaches zero.</summary>
        public void Start() { Enter(MatchPhase.Countdown, CountdownLength); InEndgame = false; }

        public void Stop() { Phase = MatchPhase.NotStarted; left = 0.0; InEndgame = false; }

        void Enter(MatchPhase p, double length) { Phase = p; left = length; }

        public void Tick(float dt)
        {
            const double eps = 1e-6;
            if (dt <= 0f || Phase == MatchPhase.NotStarted || Phase == MatchPhase.Over) return;
            left -= dt;
            if (Phase == MatchPhase.Teleop && !InEndgame && left <= EndgameLength + eps)
            {
                InEndgame = true;
                Cue?.Invoke(MatchCue.Endgame);
            }
            if (left > eps) return;
            switch (Phase)
            {
                case MatchPhase.Countdown: Enter(MatchPhase.Auto, AutoLength); Cue?.Invoke(MatchCue.MatchStart); break;
                case MatchPhase.Auto: Enter(MatchPhase.AutoPause, PauseLength); Cue?.Invoke(MatchCue.AutoEnd); break;
                case MatchPhase.AutoPause: Enter(MatchPhase.Teleop, TeleopLength); Cue?.Invoke(MatchCue.TeleopStart); break;
                case MatchPhase.Teleop: Enter(MatchPhase.Over, 0.0); Cue?.Invoke(MatchCue.MatchEnd); break;
            }
        }
    }
}
