using System.Collections.Generic;
using SteamworksSim.Field;
using UnityEngine;

namespace SteamworksSim.Game
{
    /// <summary>
    /// Field moving parts driven by the match: ROTORS spin once turning, GEARS ride the lift carriages up to
    /// the PILOTS, TOUCHPADS press down under a climbing robot and light up in the alliance colour once they
    /// have been held for the required second (prototype index.html syncMatchVisuals).
    /// </summary>
    public class FieldAnimator : MonoBehaviour
    {
        class Pad { public Transform plate, dome; public Vector3 plate0, dome0; public Renderer[] renderers; public bool lit; }

        readonly Dictionary<string, Transform[]> rotors = new Dictionary<string, Transform[]>();
        readonly Dictionary<string, Pad[]> pads = new Dictionary<string, Pad[]>();
        readonly Dictionary<string, Transform[]> pegGears = new Dictionary<string, Transform[]>();
        TargetsFile targets;
        /// <summary>GEARS installed on the AIRSHIP, per alliance and ROTOR set: set 1 is the one GEAR standing on top of
        /// the STEAM TANK facing the driver stations (doesn't turn); sets 2-4 lie on the rail trays next to their ROTOR and turn with it, each the
        /// opposite way to its neighbour as a gear train would.</summary>
        readonly Dictionary<string, Transform[][]> setGears = new Dictionary<string, Transform[][]>();
        // AIRSHIP geometry (prototype field.js DIM.airship): rail at 79 in on a 101 in hexagon, STEAM TANK top 108.5 in,
        // small ROTORS 2, 3, 4 at 90, 210 and 330 deg from the AIRSHIP centre (FieldBuilder.SmallRotorAngles)
        const float RailZ = 79f, RailFlat = 101f, TankTop = 108.5f, TrayInset = 3.5f, GearPitch = 11.2f, FirstGear = 11f;
        static readonly int[] SmallRotorAngles = { 90, 210, 330 };
        /// <summary>GEARS already on each set at the start (manual figure 3-11 "Pre-Populated"), drawn in front of the
        /// ones the PILOTS add; they turn with the set.</summary>
        static readonly int[] PrePopulated = { 0, 0, 1, 2 };
        const float SetGearSpin = 3f; // rad/s
        static readonly Color Red = new Color(1f, 0.08f, 0.1f), Blue = new Color(0.08f, 0.32f, 1f);
        const float MaxPress = 3.5f; // in: a fully climbed robot pushes the plate (and dome) up this far
        readonly Dictionary<Material, Color> baseColours = new Dictionary<Material, Color>();

        public void Init(FieldBuilder field, TargetsFile t, FieldMaterials mats)
        {
            targets = t;
            Transform anim = field != null && field.GeneratedRoot != null ? field.GeneratedRoot.Find(FieldBuilder.AnimatedName) : null;
            var gearMesh = GearMesh();
            foreach (var al in new[] { "red", "blue" })
            {
                string AL = al.ToUpper();
                var r = new Transform[4];
                for (int k = 0; k < 4; k++) r[k] = anim != null ? anim.Find($"{AL} ROTOR {k + 1}") : null;
                rotors[al] = r;

                var tps = t.Of(al).touchpads;
                var list = new Pad[tps.Length];
                for (int i = 0; i < tps.Length; i++)
                {
                    var pad = new Pad();
                    if (anim != null)
                        foreach (Transform c in anim)
                        {
                            var spec = Units.ToSpec(c.localPosition);
                            if (new Vector2(spec.x - tps[i].c[0], spec.y - tps[i].c[1]).magnitude > 4f) continue;
                            if (c.name.StartsWith($"{AL} TOUCHPAD dome")) pad.dome = c;
                            else if (c.name.StartsWith($"{AL} TOUCHPAD")) pad.plate = c;
                        }
                    if (pad.plate != null) pad.plate0 = pad.plate.localPosition;
                    if (pad.dome != null) pad.dome0 = pad.dome.localPosition;
                    var rs = new List<Renderer>();
                    foreach (var tr in new[] { pad.plate, pad.dome })
                        if (tr != null) foreach (var rend in tr.GetComponentsInChildren<Renderer>()) { rend.material = new Material(rend.sharedMaterial); rs.Add(rend); }
                    pad.renderers = rs.ToArray();
                    list[i] = pad;
                }
                pads[al] = list;

                var lifts = t.Of(al).lifts;
                var gears = new Transform[lifts.Length];
                for (int i = 0; i < lifts.Length; i++)
                {
                    var go = new GameObject($"{AL} lift GEAR {lifts[i].name}");
                    go.transform.SetParent(transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = gearMesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = mats.Get("gear");
                    // the GEAR's axis lies along the peg
                    go.transform.rotation = Quaternion.FromToRotation(Vector3.up, Units.ToUnity(lifts[i].u[0], lifts[i].u[1], 0f).normalized);
                    go.SetActive(false);
                    gears[i] = go.transform;
                }
                pegGears[al] = gears;
                setGears[al] = BuildSetGears(AL, t.Of(al).airship, gearMesh, mats.Get("gear"));
            }
        }

        /// <summary>Places (hidden) every GEAR slot of the four sets on one AIRSHIP.</summary>
        Transform[][] BuildSetGears(string AL, float[] centre, Mesh mesh, Material mat)
        {
            var c = new Vector2(centre[0], centre[1]);
            var sets = new Transform[4][];
            Transform Make(string name, Vector3 spec, float yaw)
            {
                var go = new GameObject(name);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                go.transform.SetPositionAndRotation(Units.ToUnity(spec), Quaternion.Euler(0f, yaw, 0f));
                go.SetActive(false);
                return go.transform;
            }
            // set 1: standing upright on top of the STEAM TANK, near its edge, face toward the alliance's driver
            // stations (manual figure 3-7); the red wall is at -X
            float w = AL == "RED" ? -1f : 1f;
            var g1 = Make($"{AL} ROTOR 1 GEAR", new Vector3(c.x + w * 7f, c.y, TankTop + 5.5f), 0f);
            g1.rotation = Quaternion.FromToRotation(Vector3.up, Units.ToUnity(w, 0f, 0f).normalized);
            sets[0] = new[] { g1 };
            float rr = RailFlat / Mathf.Sqrt(3f); // hexagon corner radius (corners at 30 + 60k deg)
            for (int k = 1; k < 4; k++)
            {
                int n = MatchRules.GearsNeeded[k] + PrePopulated[k];
                float a = SmallRotorAngles[k - 1] * Mathf.Deg2Rad;
                var corner = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                var list = new Transform[n];
                for (int j = 0; j < n; j++)
                {
                    // alternate between the two rail sides that meet at the ROTOR's corner, working away from it
                    int side = j % 2 == 0 ? 1 : -1, along = j / 2;
                    float b = a + side * 60f * Mathf.Deg2Rad; // the neighbouring corner along that rail side
                    var nextCorner = c + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * rr;
                    var dir = (nextCorner - corner).normalized;
                    var mid = (corner + nextCorner) / 2f;
                    var inward = (c - mid).normalized;
                    var p = corner + dir * (FirstGear + along * GearPitch) + inward * TrayInset;
                    list[j] = Make($"{AL} ROTOR {k + 1} GEAR {j + 1}", new Vector3(p.x, p.y, RailZ + 1f), along * 18f);
                }
                sets[k] = list;
            }
            return sets;
        }

        /// <summary>An 11 in, 10-tooth GEAR, 2 in thick, centred on the origin, axis up.</summary>
        static Mesh gearMesh, gearHull;

        /// <summary>The GEAR as drawn: the real part's pocketed teeth, ring, five spokes and open slices (shared).</summary>
        public static Mesh GearMesh()
        {
            if (gearMesh != null) return gearMesh;
            var mb = new MeshBuilder();
            MeshGen.DetailedGear(mb, Vector3.zero);
            return gearMesh = mb.ToMesh("GEAR");
        }

        /// <summary>A simple solid gear outline for physics (a convex collider is a solid disc either way).</summary>
        public static Mesh GearColliderMesh()
        {
            if (gearHull != null) return gearHull;
            var mb = new MeshBuilder();
            var ring = MeshGen.GearOutline(Vector2.zero, 11f, 10, 0f);
            MeshGen.Loft(mb, ring, -1f, ring, 1f, true, true);
            return gearHull = mb.ToMesh("GEAR collider");
        }

        /// <summary>climbTop: per alliance and touchpad, how high the robot under it reaches (or -1).</summary>
        public void Sync(MatchRules rules, float dt, System.Func<string, int, float> climbTop)
        {
            if (rules == null) return;
            foreach (var al in new[] { "red", "blue" })
            {
                var A = rules.Of(al);
                var r = rotors[al];
                for (int k = 0; k < 4; k++)
                    if (A.rotors[k] && r[k] != null) r[k].Rotate(0f, -(k == 0 ? 4f : 12f) * Mathf.Rad2Deg * dt, 0f, Space.Self);

                // GEARS installed on the AIRSHIP: pre-populated ones first, then each one the PILOTS add
                var sg = setGears[al];
                for (int k = 0; k < 4; k++)
                {
                    var set = sg[k];
                    int shown = Mathf.Min(set.Length, A.sets[k] + PrePopulated[k]);
                    for (int j = 0; j < set.Length; j++)
                    {
                        bool on = j < shown;
                        if (set[j].gameObject.activeSelf != on) set[j].gameObject.SetActive(on);
                        if (on && k > 0 && A.rotors[k])
                            set[j].Rotate(0f, ((j / 2) % 2 == 0 ? 1f : -1f) * SetGearSpin * Mathf.Rad2Deg * dt, 0f, Space.Self);
                    }
                }

                // GEARS on the lift carriages: shown while loaded or rising, riding up to the PILOT
                var lifts = targets.Of(al).lifts;
                var gears = pegGears[al];
                for (int i = 0; i < gears.Length; i++)
                {
                    var peg = A.pegs[i];
                    bool show = peg.state != MatchRules.PegState.Empty;
                    if (gears[i].gameObject.activeSelf != show) gears[i].gameObject.SetActive(show);
                    if (!show) continue;
                    float lift = rules.instantPilots ? 1e-3f : (float)MatchRules.PilotLift;
                    float rise = peg.state == MatchRules.PegState.Rising ? Mathf.Min(1f, (float)peg.t / lift) : 0f;
                    float rr = 44f - rise * 6f;
                    var L = lifts[i];
                    gears[i].position = Units.ToUnity(L.tip[0] - L.u[0] * (49.4f - rr), L.tip[1] - L.u[1] * (49.4f - rr), 13f + rise * 32f);
                }

                // TOUCHPADS: pressed by the robot under them (pushed up as it climbs); lit while touched
                var list = pads[al];
                for (int i = 0; i < list.Length; i++)
                {
                    var pad = list[i];
                    float top = climbTop != null ? climbTop(al, i) : -1f;
                    float press = top < 0f ? 0f : Mathf.Clamp(top - targets.Of(al).touchpads[i].z, 0f, MaxPress);
                    if (pad.plate != null) pad.plate.localPosition = pad.plate0 + Vector3.up * (press * Units.Inch);
                    if (pad.dome != null) pad.dome.localPosition = pad.dome0 + Vector3.up * (press * Units.Inch);
                    var tp = A.touch[i];
                    bool lit = tp.credited || tp.pressed; // lights at the first touch
                    if (lit == pad.lit) continue;
                    pad.lit = lit;
                    var glow = al == "red" ? Red : Blue;
                    foreach (var rend in pad.renderers)
                    {
                        var m = rend.material;
                        if (!baseColours.ContainsKey(m)) baseColours[m] = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
                        var b0 = baseColours[m];
                        var tint = lit ? new Color(glow.r, glow.g, glow.b, Mathf.Max(b0.a, 0.85f)) : b0;
                        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint); else m.color = tint;
                        if (lit) FieldMaterials.SetEmission(m, glow, 1.1f);
                        else { m.SetColor("_EmissionColor", Color.black); m.DisableKeyword("_EMISSION"); }
                    }
                }
            }
        }
    }
}
