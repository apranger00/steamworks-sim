using System.Collections.Generic;
using SteamworksSim.Field;
using UnityEngine;

namespace SteamworksSim.Game
{
    /// <summary>
    /// Where robots get game pieces from the human players (L1 / F: FUEL, R1 / G: GEAR; the chute nearest the robot
    /// drops it: 20 FUEL at a time or whatever is left, one GEAR at a time). Two kinds, by the user's names:
    ///  - LOADING STATION: the return loading station in the far corner (two chutes, RETRIEVAL ZONE 42 in deep).
    ///    Starts with 20 FUEL and 18 GEARS (manual 4.1). GEARS are never restocked; every FUEL a boiler counts comes
    ///    back to the other alliance's station (AddFuel).
    ///  - LOADING LANE: the opening in the alliance's own wall (one chute). Starts with every game piece that
    ///    doesn't start in a robot: 10 FUEL and 1 GEAR per team, less the robots' preloads.
    /// A chute does one thing at a time. A GEAR goes straight into the robot only if it can take one and its gear
    /// slot is lined up close to the chute (GearReach, GearOverlap); otherwise it slides out onto the carpet, each a
    /// little differently (about 15 %), so they don't land on top of each other.
    /// </summary>
    public class LoadingStations : MonoBehaviour
    {
        public const int StationFuel = 20, StationGears = 18, TeamsPerAlliance = 3, PreloadFuel = 10, PreloadGears = 1, FuelPerDrop = 20;
        const float DropVariance = 0.15f; // GEARS sliding out of a chute: speed, direction and spot vary this much
        /// <summary>For every robot: to take a GEAR straight from a chute its front must be within this many inches of
        /// the chute, and its front must cover at least this fraction of the chute's width, left to right.</summary>
        public const float GearReach = 8f, GearOverlap = 0.5f;
        const float FuelPerSecond = 3f, GearCooldown = 1.2f; // a GEAR takes about this long to slide down the chute

        public class Station
        {
            public string alliance;
            public bool lane;                     // LOADING LANE (alliance wall) rather than LOADING STATION
            public string Label => lane ? "Loading lane" : "Loading station";
            public LoadingSpec spec;
            public int Chutes => spec.chutes.Length / 2;
            public GameObject[] gearStack;        // lane GEARS, shown one per GEAR left (top of the last stack goes first)
            public GameObject[] boxes;            // FUEL boxes of 50 in the lane
            public GameObject[][] boxFuel;        // the balls in each box
            public int shownFuel = -1, shownGears = -1;
            public int fuel, gears;
            public readonly int[] pouring = new int[4];   // FUEL still to come out of each chute
            public readonly float[] acc = new float[4];
            public readonly float[] gearWait = new float[4];
        }

        public Station Red { get; private set; }
        public Station Blue { get; private set; }
        public Station RedLane { get; private set; }
        public Station BlueLane { get; private set; }
        /// <summary>The alliance's LOADING STATION (corner).</summary>
        public Station Of(string alliance) => alliance == "blue" ? Blue : Red;
        /// <summary>The alliance's LOADING LANE (its own wall).</summary>
        public Station LaneOf(string alliance) => alliance == "blue" ? BlueLane : RedLane;
        IEnumerable<Station> All() { foreach (var s in new[] { Red, Blue, RedLane, BlueLane }) if (s != null) yield return s; }

        FuelPool fuel;
        GearPool gearPool;
        readonly System.Random rnd = new System.Random(2017);

        public void Init(TargetsFile t, FuelPool f, GearPool g, FieldMaterials mats)
        {
            fuel = f; gearPool = g;
            if (t == null || t.loading == null) return;
            Red = new Station { alliance = "red", spec = t.loading.red };
            Blue = new Station { alliance = "blue", spec = t.loading.blue };
            if (t.lanes != null && t.lanes.red != null && t.lanes.blue != null)
            {
                RedLane = new Station { alliance = "red", lane = true, spec = t.lanes.red };
                BlueLane = new Station { alliance = "blue", lane = true, spec = t.lanes.blue };
            }
            else Debug.LogWarning("[Steamworks] targets.json has no loading lanes: re-export the field data (node tools/export_unity_data.mjs).");
            if (mats != null) foreach (var s in All()) { BuildStock(s, mats); for (int c = 0; c < s.Chutes; c++) BuildChute(s, c, mats); }
        }

        // ---------------------------------------------------------------- loading lane visuals
        public const int FuelPerBox = 50;
        const int GearStacks = 3, GearsPerStack = 7, MaxBoxes = 6;

        /// <summary>
        /// The stock behind the wall, as the human players have it: GEAR stacks against the wall and FUEL boxes
        /// (RETURN BINS, 50 FUEL each) behind them, as many as the stock needs. Station (corner): three stacks of 7 and
        /// up to six boxes. Lane (alliance wall, beside the opening): one stack of 3 and one box.
        /// </summary>
        void BuildStock(Station s, FieldMaterials mats)
        {
            float sx = Mathf.Sign(s.spec.a[0]);
            int stacks = s.lane ? 1 : GearStacks, perStack = s.lane ? TeamsPerAlliance * PreloadGears : GearsPerStack, maxBoxes = s.lane ? 1 : MaxBoxes;
            float[] stackY = s.lane ? new[] { s.spec.chutes[1] - 22f } : new[] { 135f, 150f, 165f };
            float boxY = s.lane ? s.spec.chutes[1] + 34f : 150f;
            float stackX = s.lane ? 332f : 344f, boxX = s.lane ? 347f : 364f; // clear of the chutes
            var root = new GameObject($"{s.alliance.ToUpper()} {s.Label.ToLower()} stock").transform;
            root.SetParent(transform, false);

            var gearMesh = FieldAnimator.GearMesh();
            var gearMat = mats.Get("gear");
            s.gearStack = new GameObject[stacks * perStack];
            for (int k = 0; k < stacks; k++)
                for (int h = 0; h < perStack; h++)
                {
                    var g = new GameObject("Lane GEAR");
                    g.transform.SetParent(root, false);
                    g.transform.SetPositionAndRotation(Units.ToUnity(sx * stackX, stackY[k], 1f + 2f * h), Quaternion.Euler(0f, 18f * h, 0f));
                    g.AddComponent<MeshFilter>().sharedMesh = gearMesh;
                    g.AddComponent<MeshRenderer>().sharedMaterial = gearMat;
                    s.gearStack[k * perStack + h] = g;
                }

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var cubeMesh = cube.GetComponent<MeshFilter>().sharedMesh;
            Destroy(cube);
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var sphereMesh = sphere.GetComponent<MeshFilter>().sharedMesh;
            Destroy(sphere);
            var binMat = mats.Get("bin");
            var fuelMat = mats.Get("fuel");
            const float bl = 18f, bw = 33.5f, bh = 13f, wall = 0.4f; // RETURN BIN, manual 3.11.5
            s.boxes = new GameObject[maxBoxes];
            s.boxFuel = new GameObject[maxBoxes][];
            for (int b = 0; b < maxBoxes; b++)
            {
                var box = new GameObject($"FUEL box {b + 1}").transform;
                box.SetParent(root, false);
                box.position = Units.ToUnity(sx * (boxX + 20f * b), boxY, 0f);
                void Slab(Vector3 c, Vector3 size)
                {
                    var w = new GameObject("Bin");
                    w.transform.SetParent(box, false);
                    w.transform.localPosition = Units.ToUnity(c);
                    w.transform.localScale = new Vector3(size.x, size.z, size.y) * Units.Inch;
                    w.AddComponent<MeshFilter>().sharedMesh = cubeMesh;
                    w.AddComponent<MeshRenderer>().sharedMaterial = binMat;
                }
                Slab(new Vector3(0f, 0f, wall / 2f), new Vector3(bl, bw, wall));
                Slab(new Vector3(bl / 2f, 0f, bh / 2f), new Vector3(wall, bw, bh));
                Slab(new Vector3(-bl / 2f, 0f, bh / 2f), new Vector3(wall, bw, bh));
                Slab(new Vector3(0f, bw / 2f, bh / 2f), new Vector3(bl, wall, bh));
                Slab(new Vector3(0f, -bw / 2f, bh / 2f), new Vector3(bl, wall, bh));
                // 3 x 6 per layer, three layers (54 places for 50 FUEL)
                var balls = new GameObject[FuelPerBox];
                float r = FuelPool.RadiusIn;
                for (int n = 0; n < FuelPerBox; n++)
                {
                    int layer = n / 18, i = n % 18 / 6, j = n % 6;
                    var ball = new GameObject("FUEL");
                    ball.transform.SetParent(box, false);
                    ball.transform.localPosition = Units.ToUnity(-5f + 5f * i, -12.5f + 5f * j, wall + r + layer * 4.4f);
                    ball.transform.localScale = Vector3.one * (2f * r * Units.Inch);
                    ball.AddComponent<MeshFilter>().sharedMesh = sphereMesh;
                    ball.AddComponent<MeshRenderer>().sharedMaterial = fuelMat;
                    balls[n] = ball;
                }
                s.boxFuel[b] = balls;
                s.boxes[b] = box.gameObject;
            }
            ShowLane(s);
        }

        // ---------------------------------------------------------------- chutes (manual figure 3-32)
        /// <summary>Chute length (in) and slope (deg from horizontal: 72 deg off the vertical wall).</summary>
        const float ChuteLen = 36f, ChuteSlope = 18f;
        PhysicsMaterial chuteSurface;

        /// <summary>Spec-frame directions of chute c: down the chute toward the field (`down`), and the plate's normal.</summary>
        static void ChuteFrame(Station s, int c, out Vector3 bottom, out Vector3 up, out Vector3 normal, out Vector3 along)
        {
            var L = s.spec;
            var inward = new Vector2(L.inward[0], L.inward[1]);
            float a = ChuteSlope * Mathf.Deg2Rad;
            bottom = new Vector3(L.chutes[2 * c], L.chutes[2 * c + 1], L.chuteZ0);            // plate top at the opening
            up = new Vector3(-inward.x * Mathf.Cos(a), -inward.y * Mathf.Cos(a), Mathf.Sin(a)); // up the chute, away from the field
            normal = new Vector3(inward.x * Mathf.Sin(a), inward.y * Mathf.Sin(a), Mathf.Cos(a));
            along = new Vector3(-inward.y, inward.x, 0f);
        }

        /// <summary>
        /// A chute behind the opening (manual figure 3-32): a 3 ft trough sloping up away from the field at 18 deg with
        /// an alliance-coloured bottom plate, aluminium sides as tall as the opening, a cover over its upper half, an end
        /// cap and two legs. The opening itself is open, so FUEL and GEARS are seen sliding down and out. The plate,
        /// sides and cover collide (low friction, so GEARS slide).
        /// </summary>
        void BuildChute(Station s, int c, FieldMaterials mats)
        {
            ChuteFrame(s, c, out var b, out var up, out var n, out var al);
            var L = s.spec;
            float hw = L.chuteHalfW, h = L.chuteZ1 - L.chuteZ0;
            var root = new GameObject($"{s.alliance.ToUpper()} {s.Label.ToLower()} chute {c + 1}").transform;
            root.SetParent(transform, false);
            if (chuteSurface == null)
                chuteSurface = new PhysicsMaterial("Chute") { dynamicFriction = 0.06f, staticFriction = 0.08f, bounciness = 0.1f, frictionCombine = PhysicsMaterialCombine.Minimum };
            var colour = mats.Get(s.alliance == "blue" ? "blue" : "red");
            var alu = mats.Get("alu");
            var rot = Quaternion.LookRotation(Units.ToUnity(up).normalized, Units.ToUnity(n).normalized);
            void Plank(string name, Vector3 centre, Vector3 size, Material m, bool solid)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(Units.ToUnity(centre), rot);
                go.transform.localScale = size * Units.Inch; // (across, thickness, along the chute)
                go.GetComponent<MeshRenderer>().sharedMaterial = m;
                var col = go.GetComponent<BoxCollider>();
                if (solid) col.sharedMaterial = chuteSurface; else Destroy(col);
            }
            var mid = b + up * (ChuteLen / 2f);
            Plank("Bottom plate", mid - n * 0.25f, new Vector3(2f * hw + 0.5f, 0.5f, ChuteLen), colour, true);
            for (int k = -1; k <= 1; k += 2)
                Plank("Side", mid + n * (h / 2f) + al * (k * (hw + 0.15f)), new Vector3(0.25f, h + 0.5f, ChuteLen), alu, true);
            Plank("Cover", b + up * (ChuteLen * 0.75f) + n * (h + 0.15f), new Vector3(2f * hw + 0.5f, 0.25f, ChuteLen * 0.5f), alu, true);
            Plank("End cap", b + up * (ChuteLen + 0.15f) + n * (h / 2f), new Vector3(2f * hw + 0.5f, h + 0.5f, 0.25f), alu, true);
            // legs under the high end, down to the carpet
            var top = b + up * (ChuteLen - 1f);
            for (int k = -1; k <= 1; k += 2)
            {
                var leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                leg.name = "Leg";
                leg.transform.SetParent(root, false);
                var foot = top + al * (k * (hw + 0.6f));
                leg.transform.position = Units.ToUnity(foot.x, foot.y, foot.z / 2f);
                leg.transform.localScale = new Vector3(1.25f, foot.z, 1.25f) * Units.Inch;
                leg.GetComponent<MeshRenderer>().sharedMaterial = alu;
                Destroy(leg.GetComponent<BoxCollider>());
            }
        }

        /// <summary>A point on chute c's plate `fromMouth` in up the chute, `lateral` in across, `lift` in above the plate.</summary>
        static Vector3 OnChute(Station s, int c, float fromMouth, float lateral, float lift)
        {
            ChuteFrame(s, c, out var b, out var up, out var n, out var al);
            return b + up * fromMouth + al * lateral + n * lift;
        }

        static void ShowLane(Station s)
        {
            if (s.gearStack != null && s.shownGears != s.gears)
            {
                s.shownGears = s.gears;
                for (int i = 0; i < s.gearStack.Length; i++) s.gearStack[i].SetActive(i < s.gears);
            }
            if (s.boxes != null && s.shownFuel != s.fuel)
            {
                s.shownFuel = s.fuel;
                for (int b = 0; b < s.boxes.Length; b++)
                {
                    int inBox = Mathf.Clamp(s.fuel - b * FuelPerBox, 0, FuelPerBox);
                    s.boxes[b].SetActive(inBox > 0);  // a box is there only while it has FUEL in it
                    for (int n = 0; n < FuelPerBox; n++) s.boxFuel[b][n].SetActive(n < inBox);
                }
            }
        }

        void Update()
        {
            foreach (var s in All()) ShowLane(s);
        }

        /// <summary>Start of a match: station stock, and the lanes get the preloads the alliance's robots didn't take.</summary>
        public void Reset(IEnumerable<RobotMechanisms> robots)
        {
            foreach (var s in All())
            {
                int fuelIn = 0, gearsIn = 0;
                foreach (var r in robots) if (r.alliance == s.alliance && r.participates) { fuelIn += r.PreloadedFuel; gearsIn += r.PreloadedGears; }
                if (s.lane)
                {
                    s.fuel = Mathf.Max(0, TeamsPerAlliance * PreloadFuel - fuelIn);
                    s.gears = Mathf.Max(0, TeamsPerAlliance * PreloadGears - gearsIn);
                }
                else { s.fuel = StationFuel; s.gears = StationGears; }
                for (int c = 0; c < s.pouring.Length; c++) { s.pouring[c] = 0; s.acc[c] = 0f; s.gearWait[c] = 0f; }
            }
        }

        /// <summary>FUEL processed by the other alliance's boiler comes back here.</summary>
        public void AddFuel(string alliance, int n) { var s = Of(alliance); if (s != null) s.fuel += n; }

        /// <summary>Any bumper corner within the station's RETRIEVAL ZONE (its tape's far edge is 42 in from the face).</summary>
        public bool InZone(RobotMechanisms r) => InZone(Of(r.alliance), r);

        /// <summary>Any bumper corner within the zone of this station or lane (a lane's zone also ends along the wall).</summary>
        public static bool InZone(Station s, RobotMechanisms r)
        {
            if (s == null) return false;
            var L = s.spec;
            var along = new Vector2(-L.inward[1], L.inward[0]);
            var c0 = new Vector2(L.chutes[0], L.chutes[1]);
            foreach (var c in r.Corners())
            {
                float d = Depth(L, c);
                if (d < -2f || d > L.zoneDepth) continue;
                if (L.zoneHalfW > 0f && Mathf.Abs(Vector2.Dot(c - c0, along)) > L.zoneHalfW) continue;
                return true;
            }
            return false;
        }

        /// <summary>The station or lane this robot is in the zone of (its own alliance's), or null.</summary>
        public Station ZoneOf(RobotMechanisms r)
        {
            var lane = LaneOf(r.alliance);
            if (lane != null && InZone(lane, r)) return lane;
            var st = Of(r.alliance);
            return st != null && InZone(st, r) ? st : null;
        }

        static float Depth(LoadingSpec L, Vector2 p) => (p.x - L.a[0]) * L.inward[0] + (p.y - L.a[1]) * L.inward[1];

        static int NearestChute(Station s, Vector2 p)
        {
            int best = 0; float bd = float.MaxValue;
            for (int c = 0; c < s.Chutes; c++)
            {
                float d = (p - new Vector2(s.spec.chutes[2 * c], s.spec.chutes[2 * c + 1])).sqrMagnitude;
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        /// <summary>
        /// A chute does one thing at a time: no new request until the GEAR it is passing has come out, or the last
        /// FUEL of the drop has.
        /// </summary>
        static bool Busy(Station s, int c) => s.pouring[c] > 0 || s.gearWait[c] > 0f;

        /// <summary>L1 / F: 20 FUEL (or what's left) out of the nearest chute of the lane or station the robot is at.</summary>
        public bool RequestFuel(RobotMechanisms r)
        {
            var s = ZoneOf(r);
            if (s == null) return false;
            int c = NearestChute(s, r.Center);
            if (Busy(s, c) || s.fuel < 1) return false;
            int n = Mathf.Min(FuelPerDrop, s.fuel);
            s.fuel -= n; s.pouring[c] = n; s.acc[c] = 1f; // first ball right away
            return true;
        }

        /// <summary>R1 / G: one GEAR out of the nearest chute of the lane or station the robot is at.</summary>
        public bool RequestGear(RobotMechanisms r)
        {
            var s = ZoneOf(r);
            if (s == null || gearPool == null) return false;
            int c = NearestChute(s, r.Center);
            if (Busy(s, c) || s.gears < 1) return false;
            s.gears--; s.gearWait[c] = GearCooldown;
            var L = s.spec;
            var inward = new Vector2(L.inward[0], L.inward[1]);
            var chute = new Vector2(L.chutes[2 * c], L.chutes[2 * c + 1]);
            if (r.TakeGearFromChute(L, chute, GearReach, GearOverlap)) return true; // straight into the gear slot
            // GEARS fit through the 7.5 in slot lying flat: it slides out of the opening onto the carpet, a little
            // differently each time (speed, direction and spot within about 15 %) so they don't stack up
            // the human player sets it in the chute; it slides down and out of the opening, a little differently each
            // time (speed, direction and spot within about 15 %) so they don't land on top of each other
            float V() => 1f + ((float)rnd.NextDouble() * 2f - 1f) * DropVariance;
            float side = ((float)rnd.NextDouble() * 2f - 1f) * DropVariance;
            float lateral = ((float)rnd.NextDouble() * 2f - 1f) * DropVariance * 2f * Mathf.Max(0f, L.chuteHalfW - 5.5f);
            ChuteFrame(s, c, out _, out var upC, out _, out var alC);
            var at = OnChute(s, c, 14f * V(), lateral, 2.2f);
            var vel = (-upC + alC * side).normalized * (30f * V());
            float yaw = Mathf.Atan2(inward.x, inward.y) * Mathf.Rad2Deg + (float)rnd.NextDouble() * 36f;
            gearPool.Spawn(at, vel, yaw);
            return true;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            foreach (var s in All())
            {
                var L = s.spec;
                var inward = new Vector2(L.inward[0], L.inward[1]);
                var along = new Vector2(-inward.y, inward.x);
                for (int c = 0; c < s.Chutes; c++)
                {
                    if (s.gearWait[c] > 0f) s.gearWait[c] -= dt;
                    if (s.pouring[c] <= 0 || fuel == null) continue;
                    s.acc[c] = Mathf.Min(s.acc[c] + dt * FuelPerSecond, 1f);
                    if (s.acc[c] < 1f) continue;
                    // the human player sets one ball at a time in the chute, half way up, and lets it roll down and
                    // out of the opening; only where it won't land on the last one (each drop is exactly its count)
                    float lat = ((float)rnd.NextDouble() - 0.5f) * 2f * (L.chuteHalfW - FuelPool.RadiusIn - 0.6f);
                    var at = OnChute(s, c, 18f, lat, FuelPool.RadiusIn + 0.1f);
                    if (Physics.CheckSphere(Units.ToUnity(at), FuelPool.RadiusIn * 0.98f * Units.Inch, ~0, QueryTriggerInteraction.Ignore)) continue;
                    s.acc[c] -= 1f; s.pouring[c]--;
                    ChuteFrame(s, c, out _, out var upC, out _, out _);
                    fuel.Spawn(at, -upC * 6f);
                }
            }
        }
    }
}
