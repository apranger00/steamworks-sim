using System;
using System.Collections.Generic;
using System.Text;
using SteamworksSim.Field;
using UnityEngine;

namespace SteamworksSim.Game
{
    /// <summary>
    /// The rules tests from prototype/tests/game.mjs, ported to C# so the Unity scoring can be checked against
    /// the prototype's numbers. Run from Steamworks > Run Scoring Self-Test (results in the Console).
    /// </summary>
    public static class MatchRulesSelfTest
    {
        /// <summary>Drives MatchRules the way the prototype's Match did: AUTO 0-15 s, TELEOP 15-150 s.</summary>
        class Harness
        {
            public readonly MatchRules m;
            public double t;
            bool autoEnded, over;
            public Harness(TargetsFile tg, bool reserve = false, bool playoffs = false, bool instant = false)
            {
                m = new MatchRules(tg, reserve) { playoffs = playoffs, instantPilots = instant };
            }
            public void RunTo(double tEnd, double dt = 1 / 60.0, IList<RulesRobot> robots = null)
            {
                while (!over && t < tEnd - 1e-9)
                {
                    double step = Math.Min(dt, tEnd - t);
                    step = Math.Min(step, (autoEnded ? 150.0 : 15.0) - t);
                    if (step > 0) m.Advance(step, autoEnded ? Period.Teleop : Period.Auto, robots);
                    t += step;
                    if (!autoEnded && t >= 15 - 1e-9) { t = 15; autoEnded = true; m.EndAuto(); }
                    else if (autoEnded && t >= 150 - 1e-9) { t = 150; over = true; m.EndMatch(); }
                }
            }
            public void Load(string al, Goal g, int n) { for (int i = 0; i < n; i++) m.FuelLoaded(al, g); }
            public void Gear(string al, int lift) => m.DeliverGear(al, lift, out _);
            public MatchRules.Side R => m.red;
            public MatchRules.Side B => m.blue;
        }

        static RulesRobot Robot(int id, string al, float[] xy, int touchpad = -1, float topZ = 24f)
        {
            var c = new Vector2[4];
            for (int i = 0; i < 4; i++) c[i] = new Vector2(xy[2 * i], xy[2 * i + 1]);
            return new RulesRobot { id = id, name = "R" + id, alliance = al, corners = c, climbTouchpad = touchpad, topZ = topZ };
        }

        public static string Run(out int passed, out int failed)
        {
            var tg = TargetsFile.Load();
            var sb = new StringBuilder();
            int pass = 0, fail = 0;
            void Check(string name, bool ok, string info = "")
            {
                if (ok) pass++; else fail++;
                sb.AppendLine((ok ? "PASS " : "FAIL ") + name + (info.Length > 0 ? "  - " + info : ""));
            }
            if (tg == null) { passed = 0; failed = 1; return "targets.json missing"; }

            // --- Manual worked examples (section 4.3)
            {
                var h = new Harness(tg); h.RunTo(15.01);
                h.Load("red", Goal.Low, 12); h.Load("red", Goal.High, 5); h.RunTo(20);
                Check("TELEOP 12 low + 5 high = 3 kPa, 3 pts", h.R.KPa == 3 && MatchRules.TotalPressure(h.R) - MatchRules.AutoPressure(h.R) == 3, $"kPa {h.R.KPa:0.000}");
                var h2 = new Harness(tg); h2.RunTo(15.01);
                h2.Load("red", Goal.Low, 14); h2.Load("red", Goal.High, 5); h2.RunTo(20);
                Check("TELEOP 14 low + 5 high = 29/9 kPa, 3 pts", Math.Abs(h2.R.KPa - 29 / 9.0) < 1e-12 && MatchRules.TotalPressure(h2.R) == 3, $"kPa {h2.R.KPa:0.0000}");
                var h3 = new Harness(tg);
                h3.Load("red", Goal.Low, 17); h3.RunTo(10);
                Check("AUTO 17 low = 5 2/3 kPa, 5 pts", Math.Abs(h3.R.KPa - 17 / 3.0) < 1e-12 && MatchRules.AutoPressure(h3.R) == 5, $"kPa {h3.R.KPa:0.0000}");
                h3.RunTo(16); h3.Load("red", Goal.Low, 3); h3.RunTo(20);
                int a = MatchRules.AutoPressure(h3.R), tl = MatchRules.TotalPressure(h3.R) - a;
                Check("...then 3 low in TELEOP: 6 kPa, the 6th point in TELEOP", h3.R.KPa == 6 && a == 5 && tl == 1, $"auto {a} tele {tl}");
            }
            // --- Processing rate and period attribution
            {
                var h = new Harness(tg); h.RunTo(14.0);
                h.Load("red", Goal.High, 10); h.RunTo(30);
                Check("10 high loaded at 14.0 s: 5 count in AUTO (5/s), 5 in TELEOP", h.R.fuel[0, 0] == 5 && h.R.fuel[1, 0] == 5, $"auto {h.R.fuel[0, 0]}, teleop {h.R.fuel[1, 0]}");
                Check("...= 5 AUTO pts + 1 TELEOP pt", MatchRules.AutoPressure(h.R) == 5 && MatchRules.TotalPressure(h.R) - 5 == 1);
                var h2 = new Harness(tg); h2.RunTo(149.5);
                h2.Load("blue", Goal.Low, 5); h2.RunTo(151);
                Check("5 low loaded at T-0.5 s: 2 counted, 3 lost at T=0", h2.B.fuel[1, 1] == 2 && h2.B.lost == 3, $"counted {h2.B.fuel[1, 1]}, lost {h2.B.lost}");
                var h3 = new Harness(tg); h3.Load("red", Goal.High, 200); h3.RunTo(1);
                Check("High goal capacity 150: extra FUEL falls back", h3.R.overflow[0] == 50 && h3.R.queue[0] == 145, $"overflow {h3.R.overflow[0]}, queued {h3.R.queue[0]}");
            }
            // --- Rotors, pilots, reserve (official reserve GEAR rule on)
            {
                var h = new Harness(tg, reserve: true);
                h.RunTo(5); h.Gear("red", 0);
                h.RunTo(7.9); h.Gear("red", 0); h.Gear("red", 1);
                h.RunTo(30);
                Check("ROTOR 1 in AUTO (+60) and ROTOR 2 cranked by 14.9 s (+60)", h.R.rotorPts[0] == 120 && h.R.rotors[0] && h.R.rotors[1], $"R2 at {h.R.rotorStartedAt[1]:0.00}");
                Check("reserve GEAR used after AUTO (into set 3)", h.R.reserve == MatchRules.ReserveState.Used && h.R.sets[2] >= 1, $"sets {string.Join("/", h.R.sets)}");
                var h2 = new Harness(tg);
                h2.RunTo(5.2); h2.Gear("red", 0); h2.RunTo(8.1); h2.Gear("red", 0); h2.Gear("red", 1);
                h2.RunTo(30);
                Check("ROTOR 2 finishing at 15.1 s scores TELEOP 40, not 60", h2.R.rotorPts[0] == 60 && h2.R.rotorPts[1] == 40, $"auto {h2.R.rotorPts[0]} tele {h2.R.rotorPts[1]}");
                var h3 = new Harness(tg, reserve: true); h3.RunTo(30);
                Check("official rule, no GEARS delivered: reserve starts ROTOR 1 in TELEOP (+40)", h3.R.rotorPts[1] == 40 && h3.R.rotorPts[0] == 0);
                bool first = h3.m.DeliverGear("red", 2, out _), second = h3.m.DeliverGear("red", 2, out var msg);
                Check("a lift holds one GEAR until the PILOT takes it", first && !second, msg);
            }
            // --- All rotors, 40 kPa, quals vs playoffs, takeoff
            foreach (bool playoffs in new[] { false, true })
            {
                var h = new Harness(tg, reserve: true, playoffs: playoffs, instant: true);
                h.RunTo(16);
                for (int k = 0; k < 12; k++) { h.Gear("red", k % 3); h.RunTo(h.t + 0.001); }
                h.Load("red", Goal.High, 120);
                var climber = new List<RulesRobot> { Robot(1, "red", new float[8], 0, 59f) };
                h.RunTo(148.9);
                h.RunTo(151, 1 / 60.0, climber);
                int total = MatchRules.Total(h.R), want = playoffs ? 160 + 40 + 50 + 120 : 160 + 40 + 50;
                int? rp = h.m.RankingPoints(h.R), wantRp = playoffs ? (int?)null : 4;
                Check($"{(playoffs ? "playoffs" : "quals")}: 4 rotors + 40 kPa + takeoff = {want}", total == want && rp == wantRp, $"total {total}, RP {rp}");
            }
            // --- Mobility, touchpads, RP
            {
                var h = new Harness(tg);
                var robots = new List<RulesRobot> { Robot(1, "red", new float[] { -300, 0, -262, 0, -262, 34, -300, 34 }, 1, 59f) };
                h.RunTo(1, 1 / 60.0, robots);
                int before = h.R.mobility;
                robots[0] = Robot(1, "red", new float[] { -240, 0, -230, 0, -240, 5, -245, 5 }, 1, 59f);
                h.RunTo(3, 1 / 60.0, robots);
                Check("AUTO mobility +5 once, after driving across", before == 0 && h.R.mobility == 5, $"mobility {h.R.mobility}");

                int Takeoff(double from, double until = 151)
                {
                    var hh = new Harness(tg); hh.RunTo(from);
                    var rs = new List<RulesRobot> { Robot(1, "red", new float[8], 1, 59f) };
                    hh.RunTo(until, 1 / 60.0, rs); hh.RunTo(151);
                    return hh.R.takeoff;
                }
                Check("TOUCHPAD held 1.5 s at T=0: 50", Takeoff(148.5) == 50);
                Check("TOUCHPAD held 0.8 s at T=0: 0 (needs 1 s)", Takeoff(149.2) == 0);
                Check("TOUCHPAD released before T=0: 0", Takeoff(140, 149) == 0);

                var mob = new Harness(tg); mob.RunTo(16); mob.RunTo(20, 1 / 60.0, robots);
                Check("crossing the BASE LINE in TELEOP gives no mobility", mob.R.mobility == 0);
                var parked = new List<RulesRobot> { Robot(9, "blue", new float[] { -60, 0, -40, 0, -40, 20, -60, 20 }) };
                var pk = new Harness(tg); pk.RunTo(15.5, 1 / 60.0, parked);
                Check("robot that starts past its BASE LINE earns no mobility", pk.B.mobility == 0);

                var w = new Harness(tg); w.Load("red", Goal.High, 3); w.RunTo(151);
                Check("quals: win = 2 RP, loser 0", w.m.RankingPoints(w.R) == 2 && w.m.RankingPoints(w.B) == 0, $"{MatchRules.Total(w.R)}-{MatchRules.Total(w.B)}");
                var tie = new Harness(tg); tie.RunTo(151);
                Check("quals: 0-0 tie = 1 RP each", tie.m.RankingPoints(tie.R) == 1 && tie.m.RankingPoints(tie.B) == 1);
            }
            // --- Default: no reserve GEAR (harder game)
            {
                var h = new Harness(tg); h.RunTo(30);
                Check("default: no reserve GEAR, so no free ROTOR 1", h.R.rotorPts[1] == 0 && !h.R.rotors[0] && h.R.reserve == MatchRules.ReserveState.None);
                int Rotors(int n)
                {
                    var hh = new Harness(tg, instant: true); hh.RunTo(16);
                    for (int k = 0; k < n; k++) { hh.Gear("red", k % 3); hh.RunTo(hh.t + 0.001); }
                    hh.RunTo(20);
                    return hh.R.RotorsTurning;
                }
                int r12 = Rotors(12), r13 = Rotors(13);
                Check("default: 12 GEARS start 3 ROTORS, 13 start all 4", r12 == 3 && r13 == 4, $"{r12} / {r13}");
            }
            // --- Score sheet
            {
                var h = new Harness(tg); h.Load("red", Goal.High, 12); h.RunTo(151);
                var sheet = new MatchScore(); h.m.Fill(sheet);
                Check("score sheet: 12 high in AUTO = 12 kPa, 12 AUTO points, red wins", sheet.red.autoFuel == 12 && sheet.red.Total == 12 && sheet.Winner == "red", $"{sheet.red.Total}-{sheet.blue.Total}");
            }

            passed = pass; failed = fail;
            return sb.ToString();
        }
    }
}
