using System;
using System.Collections.Generic;
using SteamworksSim.Field;
using UnityEngine;

namespace SteamworksSim.Game
{
    public enum Goal { High = 0, Low = 1 }
    public enum Period { Auto = 0, Teleop = 1 }

    /// <summary>What the rules need to know about a robot each tick (spec frame, inches).</summary>
    public struct RulesRobot
    {
        public int id;
        public string name, alliance;
        public Vector2[] corners;  // bumper footprint, 4 points
        public int climbTouchpad;  // index of the own-alliance touchpad whose rope it climbs, or -1
        public float topZ;         // top of the robot above the carpet
    }

    /// <summary>
    /// FIRST STEAMWORKS scoring (2017 manual 3.4, 3.6-3.8, 3.11.4, 4.1, 4.3, Table 4-1): a line-by-line port of
    /// prototype/web/game.js minus the clock (MatchClock owns time). Boiler processing at 5 FUEL/s per goal with
    /// pressure in ninths of a kPa, PILOTS (lift, install, crank) and ROTORS, AUTO mobility, TOUCHPADS and
    /// takeoff, ranking points. Engine-agnostic apart from Vector2.
    ///
    /// Call Advance(dt, period, robots) every tick from AUTO start to 0:00. The 3 s pause after AUTO is
    /// advanced as TELEOP with fuelPeriod = Auto: FUEL the boiler counts during the pause still scores at AUTO
    /// values (user rule); FUEL still in it when TELEOP starts counts at TELEOP values. EndAuto() when AUTO
    /// ends, StartTeleop() when TELEOP starts, EndMatch() at 0:00.
    /// </summary>
    public class MatchRules
    {
        // ---------------------------------------------------------------- rules constants
        public const int MobilityPoints = 5, TakeoffPoints = 50, PressureThreshold = 40;
        public const double BoilerRate = 5.0, TouchpadPress = 0.0; // user rule: any touch counts, no hold time
        public static readonly int[] GearsNeeded = { 1, 2, 4, 6 };     // pilot-placed GEARS per ROTOR set
        static readonly int[] Capacity = { 150, 70 };                    // high, low
        static readonly int[,] Ninths = { { 9, 3 }, { 3, 1 } };          // [period, goal]: ninths of a kPa per FUEL
        static readonly int[] RotorPoints = { 60, 40 };                  // [period]
        const int BonusPressure = 20, BonusRotors = 100;                 // playoffs only
        // Pilot timing in seconds (all 0 with instantPilots)
        public const double PilotLift = 1.0, PilotInstall = 1.5, PilotReserve = 1.5, PilotCrank = 2.0;

        public enum PegState { Empty, Loaded, Rising }
        public enum ReserveState { None, Staged, Available, Used }
        internal enum TaskKind { Crank, Gear, Reserve }

        public class Peg { public string name; public PegState state; public double t; }
        public class Touchpad { public bool pressed, credited; public double held; }
        internal class PilotTask { public TaskKind kind; public int rotor, peg; }

        public class Side
        {
            public readonly string name;
            public readonly AllianceTargets tg;
            public int ninths, ninthsAuto;
            public readonly int[,] fuel = new int[2, 2];        // [period, goal] FUEL counted
            public readonly int[] queue = new int[2];           // waiting in the boiler, per goal
            public readonly double[] procAcc = new double[2];
            public int lost;
            public readonly int[] overflow = new int[2];
            public int mobility;
            public readonly HashSet<int> mobilityRobots = new HashSet<int>(), mobilityIneligible = new HashSet<int>(), mobilitySeen = new HashSet<int>();
            public readonly int[] rotorPts = new int[2];        // [period]
            public readonly bool[] rotors = new bool[4];
            public readonly double[] rotorStartedAt = { -1, -1, -1, -1 };
            public readonly int[] sets = new int[4];
            public int spareGears, gearsDelivered;
            public readonly Peg[] pegs;
            internal PilotTask task; internal double taskT, taskDur;
            public ReserveState reserve;
            public readonly Touchpad[] touch;
            public int takeoff, bonusPressure, bonusRotors;
            public bool pressureFlag, rotorsFlag;
            public string result; // win | tie | loss, after the match

            public Side(string name, AllianceTargets tg, bool reserveGear)
            {
                this.name = name; this.tg = tg;
                pegs = new Peg[tg.lifts.Length];
                for (int i = 0; i < pegs.Length; i++) pegs[i] = new Peg { name = tg.lifts[i].name, state = PegState.Empty };
                touch = new Touchpad[tg.touchpads.Length];
                for (int i = 0; i < touch.Length; i++) touch[i] = new Touchpad();
                reserve = reserveGear ? ReserveState.Staged : ReserveState.None;
            }

            public double KPa => ninths / 9.0;
            public int RotorsTurning { get { int n = 0; foreach (var r in rotors) if (r) n++; return n; } }
            public bool PilotBusy => task != null;
        }

        // ---------------------------------------------------------------- state
        public readonly TargetsFile targets;
        public bool playoffs, instantPilots, reserveGear;
        public Side red { get; private set; }
        public Side blue { get; private set; }
        public double Time { get; private set; }    // seconds since AUTO started
        public bool Ended { get; private set; }

        /// <summary>(alliance, rotor index 0-3) when a ROTOR starts turning.</summary>
        public event Action<string, int> RotorStarted;
        /// <summary>(alliance) for every FUEL its boiler counts: the ball goes on to the other alliance's loading station.</summary>
        public event Action<string> FuelCounted;
        /// <summary>(alliance) when an alliance's pressure reaches 40 kPa.</summary>
        public event Action<string> PressureReached;
        /// <summary>(alliance or null, text): what happened, for logs.</summary>
        public event Action<string, string> Log;

        public MatchRules(TargetsFile targets, bool reserveGear = false)
        {
            this.targets = targets;
            this.reserveGear = reserveGear;
            Reset();
        }

        public void Reset()
        {
            red = new Side("red", targets.red, reserveGear);
            blue = new Side("blue", targets.blue, reserveGear);
            Time = 0; Ended = false;
        }

        public Side Of(string alliance) => alliance == "blue" ? blue : red;
        Side[] Both => new[] { red, blue };
        void Say(string alliance, string text) => Log?.Invoke(alliance, text);

        // ---------------------------------------------------------------- inputs from the simulation
        /// <summary>A FUEL went into a goal. False when the goal is full (the ball should stay on the field).</summary>
        public bool FuelLoaded(string alliance, Goal goal)
        {
            if (Ended) return false;
            var A = Of(alliance); int g = (int)goal;
            if (A.queue[g] >= Capacity[g]) { A.overflow[g]++; return false; }
            A.queue[g]++;
            return true;
        }

        /// <summary>A robot put a GEAR on a lift peg.</summary>
        public bool DeliverGear(string alliance, int liftIndex, out string message)
        {
            var A = Of(alliance);
            if (Ended) { message = "Match over"; return false; }
            var peg = A.pegs[liftIndex];
            if (peg.state != PegState.Empty) { message = $"{peg.name} lift is busy"; return false; }
            peg.state = PegState.Loaded; peg.t = 0;
            A.gearsDelivered++;
            Say(alliance, $"GEAR delivered to {peg.name} lift");
            message = $"GEAR on {peg.name} lift";
            return true;
        }

        // ---------------------------------------------------------------- time
        /// <param name="fuelPeriod">The values FUEL counted this tick scores at, if not the period's own
        /// (Auto during the pause after AUTO).</param>
        public void Advance(double dt, Period period, IList<RulesRobot> robots, Period? fuelPeriod = null)
        {
            if (Ended || dt <= 0) return;
            Time += dt;
            var fp = fuelPeriod ?? period;
            foreach (var A in Both)
            {
                Boiler(A, Goal.High, dt, fp);
                Boiler(A, Goal.Low, dt, fp);
                Pilot(A, dt, period);
            }
            // AUTO mobility: a BUMPER breaks the BASE LINE plane by the end of AUTO
            if (period == Period.Auto && robots != null)
            {
                foreach (var r in robots)
                {
                    var A = Of(r.alliance);
                    if (A.mobilityRobots.Contains(r.id) || A.mobilityIneligible.Contains(r.id)) continue;
                    int s = A.tg.side; float line = A.tg.baseLineX;
                    bool past = false;
                    foreach (var c in r.corners) if (s < 0 ? c.x > line : c.x < line) { past = true; break; }
                    // a robot must start behind its BASE LINE: the first time it is seen decides eligibility
                    if (!A.mobilitySeen.Contains(r.id))
                    {
                        A.mobilitySeen.Add(r.id);
                        if (past) { A.mobilityIneligible.Add(r.id); Say(r.alliance, $"{r.name} started past the BASE LINE: not eligible for AUTO mobility"); continue; }
                    }
                    if (past)
                    {
                        A.mobilityRobots.Add(r.id); A.mobility += MobilityPoints;
                        Say(r.alliance, $"AUTO mobility: {r.name} broke the BASE LINE (+{MobilityPoints})");
                    }
                }
            }
            // TOUCHPADS
            foreach (var A in Both)
            {
                for (int i = 0; i < A.touch.Length; i++)
                {
                    var tp = A.touch[i];
                    double tz = A.tg.touchpads[i].z + TouchpadPress;
                    bool pressed = false;
                    if (robots != null)
                        foreach (var r in robots)
                            if (r.alliance == A.name && r.climbTouchpad == i && r.topZ >= tz - 1e-6) { pressed = true; break; }
                    if (pressed && !tp.pressed) Say(A.name, $"TOUCHPAD {i + 1} touched (+{TakeoffPoints})");
                    else if (!pressed && tp.pressed) Say(A.name, $"TOUCHPAD {i + 1} released (-{TakeoffPoints})");
                    tp.pressed = pressed;
                    tp.held = pressed ? tp.held + dt : 0;
                    tp.credited = pressed; // user rule: scores while touched, unscores when let go; frozen at T=0
                }
                int n = 0;
                foreach (var tp in A.touch) if (tp.credited) n++;
                A.takeoff = n * TakeoffPoints;
            }
        }

        void Boiler(Side A, Goal goal, double dt, Period period)
        {
            int g = (int)goal, p = (int)period;
            if (A.queue[g] <= 0) { A.procAcc[g] = 0; return; }
            A.procAcc[g] += dt;
            const double per = 1.0 / BoilerRate;
            while (A.queue[g] > 0 && A.procAcc[g] >= per - 1e-9)
            {
                A.procAcc[g] -= per;
                A.queue[g]--;
                A.fuel[p, g]++;
                FuelCounted?.Invoke(A.name);
                A.ninths += Ninths[p, g];
                if (period == Period.Auto) A.ninthsAuto += Ninths[p, g];
                if (!A.pressureFlag && A.ninths >= PressureThreshold * 9)
                {
                    A.pressureFlag = true;
                    PressureReached?.Invoke(A.name);
                    if (playoffs) { A.bonusPressure = BonusPressure; Say(A.name, $"40 kPa reached: +{BonusPressure} (playoff bonus)"); }
                    else Say(A.name, "40 kPa reached: +1 Ranking Point");
                }
            }
        }

        // PILOT model: one task at a time. Priority: crank a completed set, take a GEAR off a lift, then
        // (after AUTO) use the reserve GEAR. GEARS fill the lowest unfinished set; ROTORS start in order.
        void Pilot(Side A, double dt, Period period)
        {
            double tLift = instantPilots ? 0 : PilotLift, tInstall = instantPilots ? 0 : PilotInstall;
            double tReserve = instantPilots ? 0 : PilotReserve, tCrank = instantPilots ? 0 : PilotCrank;
            // carriages that were raised come back down
            foreach (var peg in A.pegs)
                if (peg.state == PegState.Rising) { peg.t += dt; if (peg.t >= tLift) { peg.state = PegState.Empty; peg.t = 0; } }
            double budget = dt;
            for (int guard = 0; guard < 50; guard++)
            {
                if (A.task == null)
                {
                    A.task = NextTask(A); A.taskT = 0;
                    if (A.task == null) return;
                    A.taskDur = A.task.kind == TaskKind.Crank ? tCrank : A.task.kind == TaskKind.Gear ? tLift + tInstall : tReserve;
                    if (A.task.kind == TaskKind.Gear) { var peg = A.pegs[A.task.peg]; peg.state = PegState.Rising; peg.t = 0; }
                    if (A.task.kind == TaskKind.Reserve) A.reserve = ReserveState.Used;
                    if (A.task.kind == TaskKind.Crank) Say(A.name, $"PILOT cranking ROTOR {A.task.rotor + 1}");
                }
                double need = A.taskDur - A.taskT;
                if (budget < need - 1e-9) { A.taskT += budget; return; }
                budget -= Math.Max(0, need);
                FinishTask(A, A.task, period);
                A.task = null;
            }
        }

        PilotTask NextTask(Side A)
        {
            int k = Array.IndexOf(A.rotors, false);
            if (k > 0 && A.rotors[k - 1] && A.sets[k] >= GearsNeeded[k]) return new PilotTask { kind = TaskKind.Crank, rotor = k };
            for (int i = 0; i < A.pegs.Length; i++) if (A.pegs[i].state == PegState.Loaded) return new PilotTask { kind = TaskKind.Gear, peg = i };
            if (A.reserve == ReserveState.Available)
                for (int i = 0; i < 4; i++) if (A.sets[i] < GearsNeeded[i]) return new PilotTask { kind = TaskKind.Reserve };
            return null;
        }

        void FinishTask(Side A, PilotTask task, Period period)
        {
            if (task.kind == TaskKind.Crank) { StartRotor(A, task.rotor, period); return; }
            int k = -1;
            for (int i = 0; i < 4; i++) if (A.sets[i] < GearsNeeded[i]) { k = i; break; }
            string what = task.kind == TaskKind.Reserve ? "Reserve GEAR" : "GEAR";
            if (k < 0) { A.spareGears++; Say(A.name, $"{what} not needed: all GEAR sets complete"); return; }
            A.sets[k]++;
            Say(A.name, $"{what} installed in set {k + 1} ({A.sets[k]}/{GearsNeeded[k]})");
            if (k == 0 && A.sets[0] >= GearsNeeded[0]) StartRotor(A, 0, period); // ROTOR 1 starts when placed
        }

        void StartRotor(Side A, int k, Period period)
        {
            if (A.rotors[k] || Ended) return;
            A.rotors[k] = true; A.rotorStartedAt[k] = Time;
            // Points are for each ROTOR turning by the period's end and not scored before. ROTORS never
            // stop, so crediting at start time with the current period's value is equivalent.
            A.rotorPts[(int)period] += RotorPoints[(int)period];
            Say(A.name, $"ROTOR {k + 1} turning (+{RotorPoints[(int)period]}, {period.ToString().ToUpper()})");
            RotorStarted?.Invoke(A.name, k);
            if (A.RotorsTurning == 4 && !A.rotorsFlag)
            {
                A.rotorsFlag = true;
                if (playoffs) { A.bonusRotors = BonusRotors; Say(A.name, $"All four ROTORS: +{BonusRotors} (playoff bonus)"); }
                else Say(A.name, "All four ROTORS: +1 Ranking Point");
            }
        }

        public void EndAuto()
        {
            foreach (var A in Both)
            {
                int q = A.queue[0] + A.queue[1];
                if (q > 0) Say(A.name, $"{q} FUEL still in the BOILER at end of AUTO: AUTO values until TELEOP starts");
                if (A.reserve == ReserveState.Staged) A.reserve = ReserveState.Available; // A05 only applies during AUTO
            }
        }

        /// <summary>TELEOP starts (end of the 3 s pause): FUEL counted from now on scores at TELEOP values.</summary>
        public void StartTeleop()
        {
            foreach (var A in Both)
            {
                int q = A.queue[0] + A.queue[1];
                if (q > 0) Say(A.name, $"{q} FUEL still in the BOILER at TELEOP start will count at TELEOP values");
            }
        }

        public void EndMatch()
        {
            if (Ended) return;
            foreach (var A in Both)
            {
                int q = A.queue[0] + A.queue[1];
                if (q > 0) { A.lost += q; A.queue[0] = A.queue[1] = 0; Say(A.name, $"{q} FUEL not counted before T=0 (does not score)"); }
                // TOUCHPADS touched at T=0 keep their points (credited live in Advance)
                for (int i = 0; i < A.touch.Length; i++)
                    if (A.touch[i].credited) Say(A.name, $"Ready for takeoff: TOUCHPAD {i + 1} touched at T=0 (+{TakeoffPoints})");
            }
            Ended = true;
            int r = Total(red), b = Total(blue);
            red.result = r > b ? "win" : r == b ? "tie" : "loss";
            blue.result = b > r ? "win" : r == b ? "tie" : "loss";
            Say(null, $"Final: Red {r} - Blue {b}");
        }

        // ---------------------------------------------------------------- score
        public static int AutoPressure(Side A) => A.ninthsAuto / 9;
        public static int TotalPressure(Side A) => A.ninths / 9;
        public static int AutoPoints(Side A) => A.mobility + AutoPressure(A) + A.rotorPts[0];
        public static int TeleopPoints(Side A) => (TotalPressure(A) - AutoPressure(A)) + A.rotorPts[1] + A.takeoff + A.bonusPressure + A.bonusRotors;
        public static int Total(Side A) => AutoPoints(A) + TeleopPoints(A);

        /// <summary>Qualification ranking points (null in playoffs).</summary>
        public int? RankingPoints(Side A)
        {
            if (playoffs) return null;
            int rp = (A.pressureFlag ? 1 : 0) + (A.rotorsFlag ? 1 : 0);
            if (A.result == "win") rp += 2;
            if (A.result == "tie") rp += 1;
            return rp;
        }

        /// <summary>Copies the breakdown into the score sheet shown by the HUD and the final score screen.</summary>
        public void Fill(MatchScore score)
        {
            foreach (var A in Both)
            {
                var s = score.Of(A.name);
                s.mobility = A.mobility;
                s.autoFuel = AutoPressure(A);
                s.autoRotors = A.rotorPts[0];
                s.teleopFuel = TotalPressure(A) - AutoPressure(A);
                s.teleopRotors = A.rotorPts[1];
                s.takeoff = A.takeoff;
                s.bonusPressure = A.bonusPressure;
                s.bonusRotors = A.bonusRotors;
                s.kPa = (float)A.KPa;
                s.ninths = A.ninths;
                s.rotorsTurning = A.RotorsTurning;
            }
        }
    }
}
