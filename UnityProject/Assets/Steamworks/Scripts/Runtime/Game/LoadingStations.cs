using System.Collections.Generic;
using SteamworksSim.Field;
using UnityEngine;

namespace SteamworksSim.Game
{
    /// <summary>
    /// Return LOADING STATIONS (manual 3.11.3): two chutes each, fed by the alliance's human players. A robot with
    /// any part of its bumpers in its RETRIEVAL ZONE asks for a game piece (L1 / F: FUEL, R1 / G: GEAR) and the
    /// chute nearest the robot drops it: 20 FUEL at a time (or whatever is left), one GEAR at a time. A GEAR goes
    /// straight into the robot only if it can take one and is lined up close to the chute (GearReach,
    /// GearOverlap); otherwise it slides out onto the carpet.
    ///
    /// Stock (manual 4.1): each LOADING LANE starts with 20 FUEL and 18 GEARS, plus every preload its three teams
    /// don't put in a robot (10 FUEL and 1 GEAR per team). GEARS are never restocked; FUEL is: every FUEL a boiler
    /// counts goes to the other alliance's station (AddFuel).
    /// </summary>
    public class LoadingStations : MonoBehaviour
    {
        public const int LaneFuel = 20, LaneGears = 18, TeamsPerAlliance = 3, PreloadFuel = 10, PreloadGears = 1, FuelPerDrop = 20;
        /// <summary>For every robot: to take a GEAR straight from a chute its front must be within this many inches of
        /// the chute, and its front must cover at least this fraction of the chute's width, left to right.</summary>
        public const float GearReach = 8f, GearOverlap = 0.5f;
        const float FuelPerSecond = 12f, GearCooldown = 0.6f;

        public class Station
        {
            public string alliance;
            public LoadingSpec spec;
            public GameObject[] gearStack;        // lane GEARS, shown one per GEAR left (top of the last stack goes first)
            public GameObject[] boxes;            // FUEL boxes of 50 in the lane
            public GameObject[][] boxFuel;        // the balls in each box
            public int shownFuel = -1, shownGears = -1;
            public int fuel, gears;
            public readonly int[] pouring = new int[2];   // FUEL still to come out of each chute
            public readonly float[] acc = new float[2];
            public readonly float[] gearWait = new float[2];
        }

        public Station Red { get; private set; }
        public Station Blue { get; private set; }
        public Station Of(string alliance) => alliance == "blue" ? Blue : Red;

        FuelPool fuel;
        GearPool gearPool;
        readonly System.Random rnd = new System.Random(2017);

        public void Init(TargetsFile t, FuelPool f, GearPool g, FieldMaterials mats)
        {
            fuel = f; gearPool = g;
            if (t == null || t.loading == null) return;
            Red = new Station { alliance = "red", spec = t.loading.red };
            Blue = new Station { alliance = "blue", spec = t.loading.blue };
            if (mats != null) { BuildLane(Red, mats); BuildLane(Blue, mats); }
        }

        // ---------------------------------------------------------------- loading lane visuals
        public const int FuelPerBox = 50;
        const int GearStacks = 3, GearsPerStack = 7, MaxBoxes = 6;

        /// <summary>
        /// The lane behind the station's alliance wall (red's at the +X end): three GEAR stacks against the wall
        /// and a row of FUEL boxes (RETURN BINS, 50 FUEL each) behind them, as many as the stock needs.
        /// </summary>
        void BuildLane(Station s, FieldMaterials mats)
        {
            float sx = Mathf.Sign(s.spec.a[0]);
            var root = new GameObject($"{s.alliance.ToUpper()} loading lane").transform;
            root.SetParent(transform, false);

            var gearMesh = FieldAnimator.GearMesh();
            var gearMat = mats.Get("gear");
            s.gearStack = new GameObject[GearStacks * GearsPerStack];
            float[] stackY = { 135f, 150f, 165f };
            for (int k = 0; k < GearStacks; k++)
                for (int h = 0; h < GearsPerStack; h++)
                {
                    var g = new GameObject("Lane GEAR");
                    g.transform.SetParent(root, false);
                    g.transform.SetPositionAndRotation(Units.ToUnity(sx * 332f, stackY[k], 1f + 2f * h), Quaternion.Euler(0f, 18f * h, 0f));
                    g.AddComponent<MeshFilter>().sharedMesh = gearMesh;
                    g.AddComponent<MeshRenderer>().sharedMaterial = gearMat;
                    s.gearStack[k * GearsPerStack + h] = g;
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
            s.boxes = new GameObject[MaxBoxes];
            s.boxFuel = new GameObject[MaxBoxes][];
            for (int b = 0; b < MaxBoxes; b++)
            {
                var box = new GameObject($"FUEL box {b + 1}").transform;
                box.SetParent(root, false);
                box.position = Units.ToUnity(sx * (347f + 20f * b), 150f, 0f);
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
            if (Red != null) ShowLane(Red);
            if (Blue != null) ShowLane(Blue);
        }

        /// <summary>Start of a match: lane stock plus the preloads the alliance's robots didn't take.</summary>
        public void Reset(IEnumerable<RobotMechanisms> robots)
        {
            foreach (var s in new[] { Red, Blue })
            {
                if (s == null) continue;
                int fuelIn = 0, gearsIn = 0;
                foreach (var r in robots) if (r.alliance == s.alliance && r.participates) { fuelIn += r.PreloadedFuel; gearsIn += r.PreloadedGears; }
                s.fuel = LaneFuel + TeamsPerAlliance * PreloadFuel - fuelIn;
                s.gears = LaneGears + TeamsPerAlliance * PreloadGears - gearsIn;
                for (int c = 0; c < 2; c++) { s.pouring[c] = 0; s.acc[c] = 0f; s.gearWait[c] = 0f; }
            }
        }

        /// <summary>FUEL processed by the other alliance's boiler comes back here.</summary>
        public void AddFuel(string alliance, int n) { var s = Of(alliance); if (s != null) s.fuel += n; }

        /// <summary>Any bumper corner within the RETRIEVAL ZONE (the far edge of its tape is 42 in from the station face).</summary>
        public bool InZone(RobotMechanisms r)
        {
            var s = Of(r.alliance);
            if (s == null) return false;
            foreach (var c in r.Corners())
                if (Depth(s.spec, c) <= s.spec.zoneDepth) return true;
            return false;
        }

        static float Depth(LoadingSpec L, Vector2 p) => (p.x - L.a[0]) * L.inward[0] + (p.y - L.a[1]) * L.inward[1];

        int NearestChute(Station s, Vector2 p)
        {
            float d0 = (p - new Vector2(s.spec.chutes[0], s.spec.chutes[1])).sqrMagnitude;
            float d1 = (p - new Vector2(s.spec.chutes[2], s.spec.chutes[3])).sqrMagnitude;
            return d0 <= d1 ? 0 : 1;
        }

        /// <summary>L1 / F: 20 FUEL (or what's left) out of the nearest chute. False if not allowed or empty.</summary>
        public bool RequestFuel(RobotMechanisms r)
        {
            var s = Of(r.alliance);
            if (s == null || !InZone(r)) return false;
            int c = NearestChute(s, r.Center);
            if (s.pouring[c] > 0 || s.fuel < 1) return false;
            int n = Mathf.Min(FuelPerDrop, s.fuel);
            s.fuel -= n; s.pouring[c] = n; s.acc[c] = 1f; // first ball right away
            return true;
        }

        /// <summary>R1 / G: one GEAR out of the nearest chute. False if not allowed or none left.</summary>
        public bool RequestGear(RobotMechanisms r)
        {
            var s = Of(r.alliance);
            if (s == null || !InZone(r) || gearPool == null) return false;
            int c = NearestChute(s, r.Center);
            if (s.gearWait[c] > 0f || s.gears < 1) return false;
            s.gears--; s.gearWait[c] = GearCooldown;
            var L = s.spec;
            var inward = new Vector2(L.inward[0], L.inward[1]);
            var chute = new Vector2(L.chutes[2 * c], L.chutes[2 * c + 1]);
            if (r.TakeGearFromChute(L, chute, GearReach, GearOverlap)) return true; // straight into the gear slot
            // GEARS fit through the 7.5 in slot lying flat: it slides out of the opening onto the carpet
            float yaw = Mathf.Atan2(inward.x, inward.y) * Mathf.Rad2Deg;
            gearPool.Spawn(new Vector3(chute.x, chute.y, L.chuteZ0 + 1.5f), new Vector3(inward.x, inward.y, 0f) * 35f, yaw);
            return true;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            foreach (var s in new[] { Red, Blue })
            {
                if (s == null) continue;
                var L = s.spec;
                var inward = new Vector2(L.inward[0], L.inward[1]);
                var along = new Vector2(-inward.y, inward.x);
                for (int c = 0; c < 2; c++)
                {
                    if (s.gearWait[c] > 0f) s.gearWait[c] -= dt;
                    if (s.pouring[c] <= 0 || fuel == null) continue;
                    s.acc[c] += dt * FuelPerSecond;
                    while (s.acc[c] >= 1f && s.pouring[c] > 0)
                    {
                        s.acc[c] -= 1f; s.pouring[c]--;
                        var mouth = new Vector2(L.chutes[2 * c], L.chutes[2 * c + 1]) + inward * (FuelPool.RadiusIn + 1f)
                                  + along * (((float)rnd.NextDouble() - 0.5f) * 2f * (L.chuteHalfW - FuelPool.RadiusIn));
                        float z = Mathf.Lerp(L.chuteZ0 + FuelPool.RadiusIn, L.chuteZ1 - FuelPool.RadiusIn, (float)rnd.NextDouble());
                        var v = inward * (25f + 20f * (float)rnd.NextDouble()) + along * (((float)rnd.NextDouble() - 0.5f) * 12f);
                        fuel.Spawn(new Vector3(mouth.x, mouth.y, z), new Vector3(v.x, v.y, 0f));
                    }
                }
            }
        }
    }
}
