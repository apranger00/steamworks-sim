using System;
using UnityEngine;

namespace SteamworksSim.Field
{
    /// <summary>
    /// Scoring targets per alliance (goals, lifts, touchpads, base line) and rope specs, exported from the
    /// prototype (tools/export_unity_data.mjs) to Resources/Data. Spec frame: inches, Z up.
    /// </summary>
    [Serializable]
    public class TargetsFile
    {
        public int version;
        public AllianceTargets red, blue;
        public HopperSpec[] hoppers;
        public LoadingSet loading;
        public LoadingSet lanes;   // LOADING LANES in the alliance walls (unused preloads)
        public AllianceTargets Of(string alliance) => alliance == "blue" ? blue : red;

        /// <summary>Index of the hopper pair whose containers hold this (staged FUEL) position, or -1.</summary>
        public int HopperAt(float x, float y)
        {
            if (hoppers == null) return -1;
            for (int k = 0; k < hoppers.Length; k++)
            {
                var h = hoppers[k];
                if (Mathf.Sign(y) != h.side || Mathf.Abs(y) < Mathf.Abs(h.yIn) - 1f || Mathf.Abs(y) > Mathf.Abs(h.yOut) + 1f) continue;
                var c = h.containers;
                if ((x >= c[0] - 1f && x <= c[1] + 1f) || (x >= c[2] - 1f && x <= c[3] + 1f)) return k;
            }
            return -1;
        }

        static TargetsFile cached;
        public static TargetsFile Load()
        {
            if (cached != null) return cached;
            var ta = Resources.Load<TextAsset>("Data/targets");
            if (ta == null) { Debug.LogError("[Steamworks] Resources/Data/targets.json is missing."); return null; }
            return cached = JsonUtility.FromJson<TargetsFile>(ta.text);
        }
    }

    [Serializable]
    public class AllianceTargets
    {
        public int side;            // -1 red (-X), +1 blue (+X)
        public float baseLineX;
        public float launchpadX;    // signed X of the launchpad's field-centre edge (red -139, blue +139)
        public float[] key;         // KEY tape a.x, a.y, b.x, b.y; the boiler's side of it is the KEY
        public HighGoalSpec highGoal;
        public LowGoalSpec lowGoal;
        public float[] airship;     // centre x, y
        public LiftSpec[] lifts;
        public TouchpadSpec[] touchpads;
    }

    [Serializable] public class HighGoalSpec { public float[] c; public float z, r, capture; }

    /// <summary>Low goal opening: face centre, outward normal n, along-face u, half width, z range.</summary>
    [Serializable] public class LowGoalSpec { public float[] face, n, u; public float halfW, z0, z1; }

    /// <summary>Lift peg: tip position and u = unit vector pointing out of the airship along the peg.</summary>
    [Serializable] public class LiftSpec { public string name; public float[] u, tip; public float z; }

    [Serializable] public class TouchpadSpec { public float[] c; public float z; }

    [Serializable] public class LoadingSet { public LoadingSpec red, blue; }

    /// <summary>
    /// A return LOADING STATION, by the alliance that uses it: face ends a and b, inward = unit normal into the
    /// field, chutes = the two slot centres (x0, y0, x1, y1), slot half width and height range, and how far the
    /// RETRIEVAL ZONE reaches from the face.
    /// </summary>
    [Serializable]
    public class LoadingSpec
    {
        public float[] a, b, inward, chutes;
        public float chuteHalfW, chuteZ0, chuteZ1, zoneDepth;
        public float zoneHalfW;   // > 0: the zone also ends this far along the face from the first chute
    }

    /// <summary>A pair of HOPPER containers outside the guardrail and the push panel between them.</summary>
    [Serializable]
    public class HopperSpec
    {
        public float[] panel;       // panel centre x, y (its field-side face is about 0.25 in further in)
        public float panelHalfW;
        public int side;            // +1 scoring-table side, -1 far side
        public float[] containers;  // x0, x1 of the first container, x0, x1 of the second
        public float yIn, yOut, floorZ, fieldHalfY, railH;
    }

    [Serializable]
    public class RopesFile
    {
        public int version;
        public RopeSpec[] ropes;

        static RopesFile cached;
        public static RopesFile Load()
        {
            if (cached != null) return cached;
            var ta = Resources.Load<TextAsset>("Data/ropes");
            if (ta == null) { Debug.LogError("[Steamworks] Resources/Data/ropes.json is missing."); return null; }
            return cached = JsonUtility.FromJson<RopesFile>(ta.text);
        }
    }

    /// <summary>A rope hanging from a davit: anchor (top), the touchpad cut-out it passes through (pin, pinR).</summary>
    [Serializable]
    public class RopeSpec
    {
        public string name;
        public float[] anchor, pin;
        public float pinR, length, radius;
        public float[] @out; // direction the excess lies on the carpet
    }
}
