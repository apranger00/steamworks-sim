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
        public AllianceTargets Of(string alliance) => alliance == "blue" ? blue : red;

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
