using System;

namespace SteamworksSim.Field
{
    // Shapes of the JSON files in Assets/Steamworks/Data, written by tools/export_unity_data.mjs.
    // All coordinates are in the spec frame (inches, Z up); point lists are flattened [x0, y0, x1, y1, ...].

    [Serializable]
    public class FieldFile
    {
        public int version;
        public FieldItem[] items;
        public float[] fuel;      // x, y, z per ball
        public GearSpot[] gears;
        public float fuelRadius = 2.5f;
    }

    [Serializable]
    public class FieldItem
    {
        public string type;   // box | tape | loft | cyl | helicoid | dome
        public string name;
        public string mat;    // material key, see FieldMaterials
        public string group;  // field | tape | pieces
        // box
        public float[] c;     // centre (x, y, z); helicoid/dome use it too
        public float[] size;  // (sx, sy, sz) in the box's own frame
        public float rotZ;    // degrees, counter-clockwise from +X
        public float tilt;
        // tape
        public float[] a, b;  // 2D end points of the tape centre line
        public float w;
        // loft (convex ring extruded/tapered from z0 to z1)
        public float[] ring0, ring1;
        public float z0, z1;
        public bool capBottom, capTop;
        // cyl (between two 3D points; r1 at p1)
        public float[] p0, p1;
        public float r0, r1;
        public int seg;
        public bool open;
        // helicoid / dome
        public float r, turns;
    }

    [Serializable]
    public class GearSpot
    {
        public float[] c;
        public float rotZ;
        public string reserve; // "red" / "blue" for the official reserve gear, empty otherwise
    }

    [Serializable]
    public class ColliderFile
    {
        public int version;
        public PrismCollider[] prisms;
    }

    [Serializable]
    public class PrismCollider
    {
        public string name;
        public float[] pts;  // convex polygon, counter-clockwise, flattened
        public float z0, z1;
        public float[] pts0, pts1; // frustums only: exact bottom (z0) and top (z1) rings
    }
}
