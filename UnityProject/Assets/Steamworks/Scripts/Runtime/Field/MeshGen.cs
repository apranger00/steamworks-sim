using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SteamworksSim.Field
{
    /// <summary>
    /// Builds meshes from points given in the spec frame (inches, right-handed, Z up).
    /// Triangles are passed in the prototype's convention (counter-clockwise seen from outside, in the
    /// right-handed spec frame). Converting to Unity's left-handed frame mirrors the geometry, so each
    /// triangle is stored with its last two vertices swapped to keep it facing outward.
    /// Vertices are not shared, which gives flat shading like the prototype.
    /// </summary>
    public class MeshBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<int> tris = new List<int>();

        public int VertexCount => verts.Count;

        public void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            int i = verts.Count;
            verts.Add(Units.ToUnity(a));
            verts.Add(Units.ToUnity(c)); // swapped: see class comment
            verts.Add(Units.ToUnity(b));
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        /// <summary>Adds a triangle oriented so its normal points away from <paramref name="inside"/>.</summary>
        public void TriOutward(Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
        {
            Vector3 n = Vector3.Cross(b - a, c - a); // right-handed normal in spec space
            if (Vector3.Dot(n, (a + b + c) / 3f - inside) < 0f) Tri(a, c, b); else Tri(a, b, c);
        }

        /// <summary>
        /// A smooth sphere (shared vertices, so RecalculateNormals shades it round), centre c and radius r in the
        /// spec frame (inches).
        /// </summary>
        public void SmoothSphere(Vector3 c, float r, int lon, int lat)
        {
            int start = verts.Count;
            var cu = Units.ToUnity(c);
            for (int j = 0; j <= lat; j++)
            {
                float th = Mathf.PI * j / lat;
                for (int i = 0; i <= lon; i++)
                {
                    float ph = Mathf.PI * 2f * i / lon;
                    verts.Add(Units.ToUnity(c + r * new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Sin(th) * Mathf.Sin(ph), Mathf.Cos(th))));
                }
            }
            int row = lon + 1;
            for (int j = 0; j < lat; j++)
                for (int i = 0; i < lon; i++)
                {
                    int a = start + j * row + i, b = a + 1, d = a + row, e = d + 1;
                    if (j > 0) Add(a, b, e, cu);
                    if (j < lat - 1) Add(a, e, d, cu);
                }
        }

        // one triangle, wound so Unity sees it from outside (away from the centre)
        void Add(int a, int b, int c, Vector3 centre)
        {
            Vector3 n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            bool outward = Vector3.Dot(n, (verts[a] + verts[b] + verts[c]) / 3f - centre) > 0f;
            tris.Add(a); tris.Add(outward ? b : c); tris.Add(outward ? c : b);
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (verts.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }

    public static class MeshGen
    {
        // ---- rings (2D, counter-clockwise) ------------------------------------------------------
        public static Vector2[] Ring(float[] flat)
        {
            var r = new Vector2[flat.Length / 2];
            for (int i = 0; i < r.Length; i++) r[i] = new Vector2(flat[2 * i], flat[2 * i + 1]);
            return r;
        }

        /// <summary>Oriented rectangle footprint, counter-clockwise (matches the prototype's obb()).</summary>
        public static Vector2[] Obb(Vector2 c, float sx, float sy, float rotZDeg)
        {
            float a = rotZDeg * Mathf.Deg2Rad, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            var loc = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            var r = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                float u = loc[i].x * sx / 2f, v = loc[i].y * sy / 2f;
                r[i] = new Vector2(c.x + ca * u - sa * v, c.y + sa * u + ca * v);
            }
            return r;
        }

        static Vector2 Centroid(Vector2[] r)
        {
            var s = Vector2.zero;
            foreach (var p in r) s += p;
            return s / r.Length;
        }

        // ---- loft: ring0 at z0 to ring1 at z1 (same vertex count), like the prototype --------------
        public static void Loft(MeshBuilder mb, Vector2[] ring0, float z0, Vector2[] ring1, float z1, bool capBottom, bool capTop)
        {
            int n = ring0.Length;
            for (int k = 0; k < n; k++)
            {
                int k1 = (k + 1) % n;
                var a0 = new Vector3(ring0[k].x, ring0[k].y, z0);
                var b0 = new Vector3(ring0[k1].x, ring0[k1].y, z0);
                var a1 = new Vector3(ring1[k].x, ring1[k].y, z1);
                var b1 = new Vector3(ring1[k1].x, ring1[k1].y, z1);
                mb.Tri(a0, b0, b1);
                mb.Tri(a0, b1, a1);
            }
            if (capBottom)
            {
                var c = Centroid(ring0); var cc = new Vector3(c.x, c.y, z0);
                for (int k = 0; k < n; k++)
                    mb.Tri(cc, new Vector3(ring0[(k + 1) % n].x, ring0[(k + 1) % n].y, z0), new Vector3(ring0[k].x, ring0[k].y, z0));
            }
            if (capTop)
            {
                var c = Centroid(ring1); var cc = new Vector3(c.x, c.y, z1);
                for (int k = 0; k < n; k++)
                    mb.Tri(cc, new Vector3(ring1[k].x, ring1[k].y, z1), new Vector3(ring1[(k + 1) % n].x, ring1[(k + 1) % n].y, z1));
            }
        }

        public static void Box(MeshBuilder mb, Vector3 c, Vector3 size, float rotZDeg)
        {
            var ring = Obb(new Vector2(c.x, c.y), size.x, size.y, rotZDeg);
            Loft(mb, ring, c.z - size.z / 2f, ring, c.z + size.z / 2f, true, true);
        }

        /// <summary>Thin strip along a 2D segment (tape lines).</summary>
        public static void Strip(MeshBuilder mb, Vector2 a, Vector2 b, float width, float z0, float z1)
        {
            var d = b - a;
            float len = d.magnitude, ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            Box(mb, new Vector3((a.x + b.x) / 2f, (a.y + b.y) / 2f, (z0 + z1) / 2f), new Vector3(len, width, z1 - z0), ang);
        }

        /// <summary>A flat convex polygon (3D corners, spec frame) thickened by t along its normal.</summary>
        public static void Slab(MeshBuilder mb, Vector3[] p, float t)
        {
            int n = p.Length;
            if (n < 3) return;
            var nrm = Vector3.Cross(p[1] - p[0], p[2] - p[0]).normalized * t;
            var q = new Vector3[n];
            for (int k = 0; k < n; k++) q[k] = p[k] + nrm;
            var mid = Vector3.zero;
            for (int k = 0; k < n; k++) mid += p[k] + q[k];
            mid /= 2 * n;
            for (int k = 1; k + 1 < n; k++) { mb.TriOutward(p[0], p[k], p[k + 1], mid); mb.TriOutward(q[0], q[k], q[k + 1], mid); }
            for (int k = 0; k < n; k++)
            {
                int j = (k + 1) % n;
                mb.TriOutward(p[k], p[j], q[j], mid);
                mb.TriOutward(p[k], q[j], q[k], mid);
            }
        }

        /// <summary>
        /// A 2017 GEAR (the real part): 10 pocketed teeth on an outer ring, five spokes to a hub with a centre hole,
        /// and open slices between the spokes. Centre c (spec frame, inches), lying flat (axis along spec Z), turned
        /// rotDeg; OD 11 in, 2 in thick.
        /// </summary>
        public static void DetailedGear(MeshBuilder mb, Vector3 c, float rotDeg = 0f)
        {
            float rot = rotDeg * Mathf.Deg2Rad, h = 1f;
            Vector2 P(float r, float a) => new Vector2(c.x + r * Mathf.Cos(a + rot), c.y + r * Mathf.Sin(a + rot));
            void Quad(Vector2 a, Vector2 b, Vector2 d, Vector2 e, float z0, float z1)
            {
                var ring = new[] { a, b, d, e };
                float area = 0f;
                for (int k = 0; k < 4; k++) { var p = ring[k]; var q = ring[(k + 1) % 4]; area += p.x * q.y - q.x * p.y; }
                if (area < 0f) System.Array.Reverse(ring);
                Loft(mb, ring, c.z + z0, ring, c.z + z1, true, true);
            }
            void Annulus(float r0, float r1, int seg, float z0, float z1)
            {
                for (int k = 0; k < seg; k++)
                {
                    float a0 = k * Mathf.PI * 2f / seg, a1 = (k + 1) * Mathf.PI * 2f / seg;
                    Quad(P(r0, a0), P(r1, a0), P(r1, a1), P(r0, a1), z0, z1);
                }
            }
            const float ringIn = 3.55f, ringOut = 4.35f, tipR = 5.5f;
            Annulus(ringIn, ringOut, 40, -h, h);     // outer ring
            Annulus(0.45f, 1.0f, 16, -h, h);         // hub, with the centre hole
            for (int k = 0; k < 5; k++)              // five spokes (a little thinner than the ring)
            {
                float a = k * Mathf.PI * 2f / 5f + Mathf.PI / 10f, w = 0.24f;
                var dir = new Vector2(Mathf.Cos(a + rot), Mathf.Sin(a + rot));
                var nrm = new Vector2(-dir.y, dir.x) * w;
                Vector2 p0 = new Vector2(c.x, c.y) + dir * 0.9f, p1 = new Vector2(c.x, c.y) + dir * (ringIn + 0.1f);
                Quad(p0 - nrm, p1 - nrm, p1 + nrm, p0 + nrm, -0.75f, 0.75f);
            }
            for (int k = 0; k < 10; k++)             // pocketed teeth: a rim of four walls round a thin centre web
            {
                float a = k * Mathf.PI * 2f / 10f;
                float hb = 0.95f / 4.25f, ht = 0.72f / tipR; // half-widths (rad) at the base and at the tip
                Vector2 b0 = P(4.2f, a - hb), b1 = P(4.2f, a + hb), t1 = P(tipR, a + ht), t0 = P(tipR, a - ht);
                Vector2 m = (b0 + b1 + t1 + t0) / 4f;
                Vector2 In(Vector2 v) => v + (m - v).normalized * 0.32f;
                Vector2 i0 = In(b0), i1 = In(b1), j1 = In(t1), j0 = In(t0);
                Quad(b0, b1, i1, i0, -h, h);
                Quad(b1, t1, j1, i1, -h, h);
                Quad(t1, t0, j0, j1, -h, h);
                Quad(t0, b0, i0, j0, -h, h);
                Quad(i0, i1, j1, j0, -0.25f, 0.25f); // web in the middle of the pocket
            }
        }

        // ---- cylinder / cone between two 3D points (r1 at p1) ------------------------------------
        public static void Cylinder(MeshBuilder mb, Vector3 p0, Vector3 p1, float r0, float r1, int seg, bool open)
        {
            seg = Mathf.Max(3, seg);
            Vector3 d = p1 - p0;
            float len = d.magnitude;
            if (len < 1e-5f) return;
            d /= len;
            // u, v perpendicular to the axis with u x v = d (right-handed in spec space)
            Vector3 helper = Mathf.Abs(d.z) < 0.9f ? Vector3.forward : Vector3.right; // spec Z or X
            Vector3 u = Vector3.Cross(helper, d).normalized;
            Vector3 v = Vector3.Cross(d, u);
            var bot = new Vector3[seg];
            var top = new Vector3[seg];
            for (int k = 0; k < seg; k++)
            {
                float a = k * Mathf.PI * 2f / seg;
                Vector3 dir = Mathf.Cos(a) * u + Mathf.Sin(a) * v;
                bot[k] = p0 + dir * r0;
                top[k] = p1 + dir * r1;
            }
            for (int k = 0; k < seg; k++)
            {
                int k1 = (k + 1) % seg;
                mb.Tri(bot[k], bot[k1], top[k1]);
                mb.Tri(bot[k], top[k1], top[k]);
            }
            if (!open)
            {
                for (int k = 0; k < seg; k++)
                {
                    int k1 = (k + 1) % seg;
                    mb.Tri(p0, bot[k1], bot[k]);
                    mb.Tri(p1, top[k], top[k1]);
                }
            }
        }

        // ---- helicoid (central rotor), double-sided material ---------------------------------------
        public static void Helicoid(MeshBuilder mb, Vector2 c, float r0, float r1, float z0, float z1, float turns)
        {
            const int nu = 6, nv = 72;
            for (int j = 0; j < nv; j++)
            {
                for (int i = 0; i < nu; i++)
                {
                    Vector3 P(int jj, int ii)
                    {
                        float t = (float)jj / nv, a = t * turns * Mathf.PI * 2f, z = z0 + (z1 - z0) * t;
                        float r = r0 + (r1 - r0) * ((float)ii / nu);
                        return new Vector3(c.x + r * Mathf.Cos(a), c.y + r * Mathf.Sin(a), z);
                    }
                    var a0 = P(j, i); var b0 = P(j, i + 1); var a1 = P(j + 1, i); var b1 = P(j + 1, i + 1);
                    mb.Tri(a0, b0, b1);
                    mb.Tri(a0, b1, a1);
                }
            }
        }

        // ---- sphere / hemisphere, oriented outward from the centre ---------------------------------
        public static void Sphere(MeshBuilder mb, Vector3 c, float r, int lon = 14, int lat = 10, bool upperHalfOnly = false)
        {
            float thetaMax = upperHalfOnly ? Mathf.PI / 2f : Mathf.PI;
            for (int j = 0; j < lat; j++)
            {
                float t0 = thetaMax * j / lat, t1 = thetaMax * (j + 1) / lat;
                for (int i = 0; i < lon; i++)
                {
                    float p0 = Mathf.PI * 2f * i / lon, p1 = Mathf.PI * 2f * (i + 1) / lon;
                    Vector3 S(float th, float ph) => c + r * new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Sin(th) * Mathf.Sin(ph), Mathf.Cos(th));
                    Vector3 a = S(t0, p0), b = S(t0, p1), d = S(t1, p0), e = S(t1, p1);
                    if (j > 0) mb.TriOutward(a, b, e, c);
                    mb.TriOutward(a, e, d, c);
                }
            }
        }

        // ---- GEAR: 10-tooth outline extruded 2" (lying flat) ----------------------------------------
        public static Vector2[] GearOutline(Vector2 c, float od = 11f, int teeth = 10, float rotDeg = 0f)
        {
            int steps = teeth * 4;
            float ro = od / 2f, rr = od / 2f - 1.1f, rot = rotDeg * Mathf.Deg2Rad;
            var ring = new Vector2[steps];
            for (int k = 0; k < steps; k++)
            {
                float a = rot + k * Mathf.PI * 2f / steps;
                float r = (k % 4 == 1 || k % 4 == 2) ? ro : rr;
                ring[k] = new Vector2(c.x + r * Mathf.Cos(a), c.y + r * Mathf.Sin(a));
            }
            return ring;
        }
    }
}
