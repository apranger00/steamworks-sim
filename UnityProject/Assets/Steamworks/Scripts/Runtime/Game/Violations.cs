using System;
using System.Collections.Generic;
using UnityEngine;

namespace SteamworksSim.Game
{
    /// <summary>
    /// The referees: watches every pair of opposing robots each physics step and calls the game rules the user picked
    /// from the 2017 manual. A FOUL gives the other alliance 5 points, a TECH FOUL 25 (MatchRules.Foul); G07 and G20
    /// can also credit a robot a climb it didn't make (MatchRules.FreeClimb, never twice for one robot). Each call is
    /// reported (Called) for the HUD's VIOLATION card. Contact is physical (RobotMechanisms.Touching); a contact rule
    /// is called once per contact, not every step.
    ///
    ///  G07 Opponent touching their ROPE: don't touch them. Contact with an opposing robot that is on its own ROPE:
    ///      that robot gets a free climb (+50).
    ///  G11 There's a 5-count on pins. Pinning an opponent (in contact while it is stopped against the field or another
    ///      robot, and the pinner is driving) for more than 5 s: FOUL, or TECH FOUL if the pinner is in the opponent's
    ///      KEY, plus one more every 5 s it goes on. The pin lasts until the robots are 6 ft apart, or until the pinned
    ///      robot chases the pinner as it backs off; the pinner then has to wait 3 s before pinning that robot again
    ///      (a pin restarted sooner carries on counting where it left off). Pins are reported for the HUD's pin bubble.
    ///  G13 Don't mess with a ROBOT in their RETRIEVAL ZONE. A robot with any part in its opponent's RETRIEVAL ZONE
    ///      contacting an opposing robot: TECH FOUL.
    ///  G17 Opponent's KEY: a no parking zone. A robot (bumpers breaking the plane of the key line) in its opponent's
    ///      KEY for more than 5 s: FOUL, and one more every 5 s it stays. A robot being pinned is exempt; its count
    ///      starts once the pin is over. Reported for the HUD's count bubble.
    ///  G20 Let 'em climb: don't touch their ROPES. In the last 30 s, contact with an opposing alliance's ROPE: FOUL;
    ///      an opposing robot that then contacts the offender or that ROPE gets a free climb.
    ///  A04 Stay out of your opponent's LAUNCHPAD. In AUTO, bumpers in the opponent's LAUNCHPAD: FOUL; contacting an
    ///      opposing robot in its own LAUNCHPAD meanwhile: TECH FOUL.
    /// </summary>
    public class Violations
    {
        public struct Call
        {
            public string rule, title, offender; // offender: alliance that committed it
            public int points;                   // to the other alliance (or the free climb's 50)
            public bool freeClimb, tech;
        }

        public enum PinState { Counting, Violation, Cleared }

        /// <summary>A pin count, for the HUD bubble.</summary>
        public class Pin
        {
            public RobotMechanisms pinner, pinned;
            public float count;          // s since the pin started (carries on if re-pinned within 3 s)
            public int called;           // FOULS called so far
            public PinState state;
            public float clearedAt = -100f, lastTouch;
            public bool active, backingOff;
            public float lastGap;
            public float PeriodFraction => Mathf.Repeat(count, PinLimit) / PinLimit;
        }

        public const float PinLimit = 5f, PinSeparation = 72f, PinWait = 3f;
        const float StoppedSpeed = 12f;  // in/s: a robot this slow against something is pinned
        const float ChaseSpeed = 10f;    // in/s toward the retreating pinner

        public event Action<Call> Called;
        public readonly List<Pin> pins = new List<Pin>();
        readonly HashSet<string> latched = new HashSet<string>(); // contact rules: called once per contact

        public void Reset() { pins.Clear(); keys.Clear(); latched.Clear(); }

        /// <summary>Once per physics step while the match runs (AUTO and TELEOP).</summary>
        public void Step(float dt, IList<RobotMechanisms> robots, MatchRules rules, LoadingStations loading, RopeSystem ropes,
                         bool auto, bool lastThirty)
        {
            if (rules == null || rules.Ended) return;
            current = robots;
            var still = new HashSet<string>();
            for (int i = 0; i < robots.Count; i++)
                for (int j = 0; j < robots.Count; j++)
                {
                    var x = robots[i]; var y = robots[j];
                    if (i == j || x == null || y == null || x.alliance == y.alliance) continue;
                    bool touching = x.Touching(y);

                    // G07: x touches y while y is on one of its own ROPES -> y climbs for free
                    if (touching && OnOwnRope(y, ropes) && Once(still, $"G07 {x.id} {y.id}")
                        && rules.FreeClimb(y.alliance, y.id, "G07"))
                        Raise("G07", "Opponent touching their ROPE", x.alliance, MatchRules.TakeoffPoints, false, true);

                    // G13: x, with any part in its opponent's RETRIEVAL ZONE, contacts y -> TECH FOUL
                    if (touching && loading != null && LoadingStations.InZone(loading.Of(y.alliance), x) && Once(still, $"G13 {x.id} {y.id}"))
                        Raise("G13", "Don't mess with a ROBOT in their RETRIEVAL ZONE", x.alliance, rules.Foul(x.alliance, true, "G13"), true, false);

                    // A04 (AUTO): contact with y in its own LAUNCHPAD while x is in it -> TECH FOUL
                    if (auto && touching && x.InLaunchpad(y.alliance) && y.InLaunchpad(y.alliance) && Once(still, $"A04c {x.id} {y.id}"))
                        Raise("A04", "Contact in the opponent's LAUNCHPAD", x.alliance, rules.Foul(x.alliance, true, "A04"), true, false);

                    // G11: pins (x pinning y)
                    StepPin(dt, x, y, touching, rules);
                }

            foreach (var x in robots)
            {
                if (x == null) continue;
                StepKey(dt, x, rules);
                // A04 (AUTO): bumpers in the opponent's LAUNCHPAD -> FOUL (once per entry)
                string opp = x.alliance == "red" ? "blue" : "red";
                if (auto && x.InLaunchpad(opp) && Once(still, $"A04 {x.id}"))
                    Raise("A04", "Stay out of your opponent's LAUNCHPAD", x.alliance, rules.Foul(x.alliance, false, "A04"), false, false);

                // G20 (last 30 s): contact with an opposing alliance's ROPE -> FOUL; while it lasts, an opposing robot that
                // touches the offender or that ROPE gets a free climb
                if (lastThirty && ropes != null)
                    foreach (var R in ropes.ropes)
                    {
                        if (!R.deployed || R.alliance == null || R.alliance == x.alliance || !R.touchedBy.Contains(x.id)) continue;
                        if (Once(still, $"G20 {x.id} {R.alliance} {R.touchpad}"))
                            Raise("G20", "Let 'em climb: don't touch their ROPES", x.alliance, rules.Foul(x.alliance, false, "G20"), false, false);
                        foreach (var y in robots)
                        {
                            if (y == null || y.alliance != R.alliance) continue;
                            if ((x.Touching(y) || R.touchedBy.Contains(y.id)) && rules.FreeClimb(y.alliance, y.id, "G20"))
                                Raise("G20", "Free climb: opponent on your ROPE", x.alliance, MatchRules.TakeoffPoints, false, true);
                        }
                    }
            }
            latched.IntersectWith(still); // a contact that ended can be called again next time
        }

        static bool OnOwnRope(RobotMechanisms r, RopeSystem ropes)
        {
            if (r.Climb != RobotMechanisms.ClimbPhase.None && r.ClimbRope != null && r.ClimbRope.alliance == r.alliance) return true;
            if (ropes != null)
                foreach (var R in ropes.ropes) if (R.deployed && R.alliance == r.alliance && R.touchedBy.Contains(r.id)) return true;
            return false;
        }

        // true the first step a condition holds (it stays latched while it keeps holding)
        bool Once(HashSet<string> still, string key)
        {
            still.Add(key);
            return latched.Add(key);
        }

        void Raise(string rule, string title, string offender, int points, bool tech, bool freeClimb)
        {
            if (points <= 0 && !freeClimb) return;
            Called?.Invoke(new Call { rule = rule, title = title, offender = offender, points = points, tech = tech, freeClimb = freeClimb });
        }

        // ---------------------------------------------------------------- G17
        /// <summary>A robot's time in its opponent's KEY, for the HUD bubble.</summary>
        public class KeyCount
        {
            public RobotMechanisms robot;
            public float count;
            public int called;
            public bool active;
            public float PeriodFraction => Mathf.Repeat(count, PinLimit) / PinLimit;
        }

        public readonly List<KeyCount> keys = new List<KeyCount>();

        void StepKey(float dt, RobotMechanisms x, MatchRules rules)
        {
            KeyCount k = null;
            foreach (var q in keys) if (q.robot == x) { k = q; break; }
            if (k == null) { k = new KeyCount { robot = x }; keys.Add(k); }
            string opp = x.alliance == "red" ? "blue" : "red";
            bool pinned = false;
            foreach (var p in pins) if (p.active && p.pinned == x) { pinned = true; break; }
            if (!x.InKey(opp) || pinned) { k.active = false; k.count = 0f; k.called = 0; return; } // pinned: starts after the pin
            k.active = true;
            k.count += dt;
            if (k.count >= PinLimit * (k.called + 1))
            {
                k.called++;
                Raise("G17", "Opponent's KEY: a no parking zone", x.alliance, rules.Foul(x.alliance, false, "G17"), false, false);
            }
        }

        // ---------------------------------------------------------------- G11
        Pin PinOf(RobotMechanisms p, RobotMechanisms t)
        {
            foreach (var q in pins) if (q.pinner == p && q.pinned == t) return q;
            return null;
        }

        void StepPin(float dt, RobotMechanisms p, RobotMechanisms t, bool touching, MatchRules rules)
        {
            var pin = PinOf(p, t);
            float now = Time.fixedTime;
            bool trapped = touching && p.Driving && t.Velocity.magnitude < StoppedSpeed && t.Climb == RobotMechanisms.ClimbPhase.None
                           && (t.TouchingField || OtherContact(t, p));
            if (pin == null || !pin.active)
            {
                if (!trapped) return;
                if (pin == null) { pin = new Pin { pinner = p, pinned = t }; pins.Add(pin); }
                else if (now - pin.clearedAt > PinWait) { pin.count = 0f; pin.called = 0; } // a new pin; sooner, it carries on
                pin.active = true; pin.state = pin.called > 0 ? PinState.Violation : PinState.Counting;
                pin.backingOff = false; pin.lastTouch = now; pin.lastGap = 0f;
                return;
            }

            pin.count += dt;
            if (touching) { pin.lastTouch = now; pin.backingOff = false; }
            float gap = Gap(p.Corners(), t.Corners());
            // separated by 6 ft: the pin is over
            if (gap >= PinSeparation) { Clear(pin, now); return; }
            // the pinner backs off and the pinned robot chases it: the pin is over, no penalty
            if (!touching)
            {
                if (gap > pin.lastGap + 0.01f) pin.backingOff = true;
                var toP = (Centre(p) - Centre(t)).normalized;
                if (pin.backingOff && Vector2.Dot(t.Velocity, toP) > ChaseSpeed && gap < pin.lastGap) { Clear(pin, now); return; }
            }
            pin.lastGap = gap;
            // every 5 s it isn't corrected: FOUL (TECH FOUL with the pinner in the pinned robot's KEY)
            if (pin.count >= PinLimit * (pin.called + 1))
            {
                pin.called++;
                pin.state = PinState.Violation;
                bool tech = p.InKey(t.alliance);
                Raise("G11", "There's a 5-count on pins", p.alliance, rules.Foul(p.alliance, tech, "G11"), tech, false);
            }
        }

        static void Clear(Pin pin, float now) { pin.active = false; pin.state = PinState.Cleared; pin.clearedAt = now; }

        IList<RobotMechanisms> current; // the robots of this step

        // the pinned robot is also against another robot (pinning is transitive through robots)
        bool OtherContact(RobotMechanisms t, RobotMechanisms except)
        {
            if (current == null) return false;
            foreach (var o in current)
                if (o != null && o != t && o != except && t.Touching(o)) return true;
            return false;
        }

        static Vector2 Centre(RobotMechanisms r) { var c = r.Corners(); return (c[0] + c[1] + c[2] + c[3]) / 4f; }

        /// <summary>Distance (in) between two convex bumper footprints; 0 when they touch or overlap.</summary>
        public static float Gap(Vector2[] a, Vector2[] b)
        {
            if (Overlap(a, b)) return 0f;
            float best = float.MaxValue;
            for (int i = 0; i < a.Length; i++)
                for (int j = 0; j < b.Length; j++)
                {
                    best = Mathf.Min(best, PointSeg(a[i], b[j], b[(j + 1) % b.Length]));
                    best = Mathf.Min(best, PointSeg(b[j], a[i], a[(i + 1) % a.Length]));
                }
            return best;
        }

        static float PointSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            var d = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, d) / Mathf.Max(1e-6f, d.sqrMagnitude));
            return (p - (a + d * t)).magnitude;
        }

        static bool Overlap(Vector2[] a, Vector2[] b)
        {
            foreach (var poly in new[] { a, b })
                for (int i = 0; i < poly.Length; i++)
                {
                    var e = poly[(i + 1) % poly.Length] - poly[i];
                    var n = new Vector2(-e.y, e.x);
                    float amin = float.MaxValue, amax = float.MinValue, bmin = float.MaxValue, bmax = float.MinValue;
                    foreach (var v in a) { float d = Vector2.Dot(v, n); amin = Mathf.Min(amin, d); amax = Mathf.Max(amax, d); }
                    foreach (var v in b) { float d = Vector2.Dot(v, n); bmin = Mathf.Min(bmin, d); bmax = Mathf.Max(bmax, d); }
                    if (amax < bmin || bmax < amin) return false;
                }
            return true;
        }
    }
}
