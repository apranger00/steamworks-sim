#!/usr/bin/env python3
"""
STEP assembly -> Steamworks Sim robot model (model.json), with numpy and scipy only (no CAD kernel needed).

Every solid in the assembly becomes its convex hull, sampled from the solid's edges (lines, circular arcs, spline
control points). Plates, tubes, wheels, rollers and gussets come out close to the real part; concave parts (L
brackets, channels) are filled in. Hulls are placed by the assembly transforms, moved into robot axes, coloured by
part-name rules, smoothed (normals shared across edges under 35 deg) and grouped: moving parts (wheels, rollers, a gear
slider...) get their own pivot so the game can animate them.

    python3 tools/step_to_robot_model.py robot.stp tools/robots/<robot>.json out/model.json

The config (JSON) says how the CAD sits in the robot: which CAD axis is up and which is forward, the floor height,
the footprint centre, which parts to leave out (e.g. the CAD bumpers: the game draws alliance bumpers), the colour
rules and the groups. See tools/robots/sir-belvidere.json.

model.json (read by RobotModel.cs):
  { "format": "steamworks-robot-model", "version": 1, "units": "in",
    "axes": "x right, y up, z forward; origin on the carpet under the footprint centre",
    "materials": [ { "name", "color": [r,g,b,a], "metal", "smooth" } ],
    "parts": [ { "name", "pivot": [x,y,z], "meshes": [ { "material": i, "v": [x,y,z,...], "n": [...], "t": [...] } ] } ] }
  Vertex positions are relative to the part's pivot. Triangles wind clockwise seen from outside (Unity).
"""
import re, sys, json, math, collections, fnmatch
import numpy as np
from scipy.spatial import ConvexHull

# ------------------------------------------------------------------ STEP parsing
def load_step(path):
    ents = {}; buf = []
    pat = re.compile(r'#(\d+)\s*=\s*(.*)$', re.S)
    with open(path, 'r', errors='replace') as f:
        data = False
        for line in f:
            if not data:
                if line.startswith('DATA;'): data = True
                continue
            buf.append(line.rstrip('\n'))
            if line.rstrip().endswith(';'):
                s = ''.join(buf); buf = []
                m = pat.match(s)
                if m: ents[int(m.group(1))] = m.group(2).rstrip().rstrip(';')
    return ents

NUM = re.compile(r'[-+]?(?:\d+\.?\d*|\.\d+)(?:[Ee][-+]?\d+)?')
def typ(b):
    m = re.match(r'([A-Z_0-9]+)\(', b); return m.group(1) if m else 'COMPLEX'
def R(s): return [int(x) for x in re.findall(r'#(\d+)', s)]

class Step:
    def __init__(self, ents):
        self.e = ents
        self.bytype = collections.defaultdict(list)
        for i, b in ents.items(): self.bytype[typ(b)].append(i)
        self.vp = {}; self.units = {}; self.hulls = {}; self.max_verts = 90

    def args(self, i): b = self.e[i]; return b[b.index('(') + 1:-1]
    def coords(self, i):
        b = self.e[i]; q = b.index("'"); q = b.index("'", q + 1)
        return np.array([float(x) for x in NUM.findall(b[b.index('(', q) + 1:])[:3]])
    def direction(self, i): v = self.coords(i); return v / np.linalg.norm(v)
    def axis(self, i):
        r = R(self.args(i)); loc = self.coords(r[0])
        z = self.direction(r[1]) if len(r) > 1 else np.array([0, 0, 1.])
        x = self.direction(r[2]) if len(r) > 2 else (np.array([1., 0, 0]) if abs(z[0]) < 0.9 else np.array([0, 1., 0]))
        x = x - z * np.dot(x, z); x /= np.linalg.norm(x)
        return loc, x, np.cross(z, x), z
    def axismat(self, i, scale):
        loc, x, y, z = self.axis(i); M = np.eye(4)
        M[:3, 0] = x; M[:3, 1] = y; M[:3, 2] = z; M[:3, 3] = loc * scale
        return M
    def ctx_scale(self, ctx):
        if ctx in self.units: return self.units[ctx]
        s = 1.0
        m = re.search(r'GLOBAL_UNIT_ASSIGNED_CONTEXT\(\(([^)]*)\)\)', self.e[ctx])
        if m:
            for u in R(m.group(1)):
                ub = self.e[u]
                if 'LENGTH_UNIT' not in ub: continue
                if "CONVERSION_BASED_UNIT('INCH'" in ub: s = 1.0
                elif 'SI_UNIT(.MILLI.,.METRE.)' in ub: s = 1 / 25.4
                elif 'SI_UNIT(.CENTI.,.METRE.)' in ub: s = 1 / 2.54
                elif 'SI_UNIT($,.METRE.)' in ub: s = 1 / 0.0254
        self.units[ctx] = s; return s
    def rep_scale(self, rep): return self.ctx_scale(R(self.args(rep))[-1])

    # ---- assembly tree
    def tree(self):
        e = self.e
        self.pd_name = {}
        for i in self.bytype['PRODUCT_DEFINITION']:
            form = R(self.args(i))[0]; prod = R(self.args(form))[0]
            self.pd_name[i] = re.match(r"PRODUCT\('([^']*)'", e[prod]).group(1)
        self.pds_of = {i: R(self.args(i))[-1] for i in self.bytype['PRODUCT_DEFINITION_SHAPE']}
        self.rep_of_pd = {}
        for i in self.bytype['SHAPE_DEFINITION_REPRESENTATION']:
            a, b = R(self.args(i))[:2]; d = self.pds_of.get(a)
            if d in self.pd_name: self.rep_of_pd[d] = b
        self.srr = collections.defaultdict(list)
        for i in self.bytype['SHAPE_REPRESENTATION_RELATIONSHIP']:
            a, b = R(self.args(i))[-2:]; self.srr[a].append(b); self.srr[b].append(a)
        nauo = {i: tuple(R(self.args(i))[:2]) for i in self.bytype['NEXT_ASSEMBLY_USAGE_OCCURRENCE']}
        self.children = collections.defaultdict(list)
        for i in self.bytype['CONTEXT_DEPENDENT_SHAPE_REPRESENTATION']:
            rel, pds = R(self.args(i))[:2]
            parent, child = nauo[self.pds_of[pds]]
            b = e[rel]
            m = re.search(r"REPRESENTATION_RELATIONSHIP\('[^']*','[^']*',#(\d+),#(\d+)\)", b)
            r1, r2 = int(m.group(1)), int(m.group(2))
            idt = int(re.search(r'REPRESENTATION_RELATIONSHIP_WITH_TRANSFORMATION\(#(\d+)\)', b).group(1))
            a1, a2 = R(self.args(idt))[:2]
            if r1 == self.rep_of_pd.get(parent) or r2 == self.rep_of_pd.get(child): r1, r2, a1, a2 = r2, r1, a2, a1
            M = self.axismat(a2, self.rep_scale(r2)) @ np.linalg.inv(self.axismat(a1, self.rep_scale(r1)))
            self.children[parent].append((child, M))
        kids = {c for v in self.children.values() for c, _ in v}
        return [p for p in self.pd_name if p not in kids and p in self.children] or \
               [p for p in self.pd_name if p not in kids]

    def solids_of_rep(self, rep):
        out = []; seen = set(); stack = [rep]
        while stack:
            r = stack.pop()
            if r in seen: continue
            seen.add(r)
            if typ(self.e[r]) in ('ADVANCED_BREP_SHAPE_REPRESENTATION', 'MANIFOLD_SURFACE_SHAPE_REPRESENTATION', 'SHAPE_REPRESENTATION'):
                for it in R(self.args(r).split(')')[0]):
                    if typ(self.e[it]) in ('MANIFOLD_SOLID_BREP', 'BREP_WITH_VOIDS', 'SHELL_BASED_SURFACE_MODEL'):
                        out.append((it, self.rep_scale(r)))
            stack.extend(self.srr.get(r, []))
        return out

    def instances(self):
        roots = self.tree(); out = []
        def walk(pd, M, path):
            rep = self.rep_of_pd.get(pd)
            if rep is not None:
                for s, sc in self.solids_of_rep(rep): out.append((path + [self.pd_name[pd]], M, s, sc))
            for c, Mc in self.children.get(pd, []): walk(c, M @ Mc, path + [self.pd_name[pd]])
        for r in roots: walk(r, np.eye(4), [])
        return out

    # ---- solid -> hull
    def vertex(self, v):
        if v not in self.vp: self.vp[v] = self.coords(R(self.args(v))[0])
        return self.vp[v]
    def circle(self, c, p1, p2, full, n=12):
        a = self.args(c); r = R(a); rad = float(NUM.findall(a.split(',')[-1])[0])
        loc, x, y, z = self.axis(r[0])
        ang = lambda p: math.atan2(np.dot(p - loc, y), np.dot(p - loc, x))
        if full: a0, a1 = 0.0, 2 * math.pi
        else:
            a0, a1 = ang(p1), ang(p2)
            while a1 <= a0 + 1e-9: a1 += 2 * math.pi
        k = max(2, int(math.ceil(n * (a1 - a0) / (2 * math.pi))) + 1)
        return [loc + rad * (math.cos(t) * x + math.sin(t) * y) for t in np.linspace(a0, a1, k)]
    def edge(self, i):
        a = self.args(i); r = R(a); v1, v2, cur = r[0], r[1], r[2]
        sense = '.T.' in a.split(',')[-1]
        p1, p2 = self.vertex(v1), self.vertex(v2)
        t = typ(self.e[cur])
        if t in ('SURFACE_CURVE', 'SEAM_CURVE'): cur = R(self.args(cur))[0]; t = typ(self.e[cur])
        if t == 'CIRCLE': return self.circle(cur, *((p1, p2) if sense else (p2, p1)), v1 == v2)
        if t == 'B_SPLINE_CURVE_WITH_KNOTS':
            return [p1, p2] + [self.coords(c) for c in R(re.search(r'\(([^()]*)\)', self.args(cur)).group(1))]
        if t == 'COMPLEX':
            m = re.search(r'B_SPLINE_CURVE\(\d+,\(([^()]*)\)', self.e[cur])
            if m: return [p1, p2] + [self.coords(c) for c in R(m.group(1))]
        return [p1, p2]
    WALK = {'MANIFOLD_SOLID_BREP', 'BREP_WITH_VOIDS', 'CLOSED_SHELL', 'OPEN_SHELL', 'SHELL_BASED_SURFACE_MODEL', 'ADVANCED_FACE',
            'FACE_OUTER_BOUND', 'FACE_BOUND', 'EDGE_LOOP', 'ORIENTED_EDGE', 'ORIENTED_CLOSED_SHELL'}
    def points(self, s):
        pts = []; seen = set(); stack = [s]
        while stack:
            i = stack.pop()
            if i in seen: continue
            seen.add(i); t = typ(self.e[i])
            if t == 'EDGE_CURVE': pts.extend(self.edge(i))
            elif t == 'VERTEX_POINT': pts.append(self.vertex(i))
            elif t in self.WALK:
                a = self.args(i)
                if t == 'ADVANCED_FACE': a = a[:a.rindex('#')]  # not the surface
                stack.extend(R(a))
        return np.array(pts)
    def hull(self, s, scale):
        key = (s, scale)
        if key in self.hulls: return self.hulls[key]
        P = self.points(s) * scale; res = None
        if len(P) >= 4:
            P = np.unique(np.round(P, 4), axis=0)
            g = np.linalg.norm(P.max(0) - P.min(0)) / 28.0
            for attempt in range(8):
                Q = thin(P, g) if len(P) > 60 else P
                for opt in (None, 'QJ'):
                    try:
                        h = ConvexHull(Q, qhull_options=opt) if opt else ConvexHull(Q)
                        idx = np.unique(h.simplices); remap = np.full(len(Q), -1); remap[idx] = np.arange(len(idx))
                        res = (h.points[idx], remap[h.simplices]); break
                    except Exception: res = None
                if res is None or len(res[0]) <= self.max_verts: break
                g *= 1.5
        self.hulls[key] = res; return res

# ------------------------------------------------------------------ geometry helpers
def thin(P, g):
    """One point per g-sized grid cell (the one farthest from the centre, so the outline keeps its extent)."""
    c = (P.max(0) + P.min(0)) / 2
    cell = np.floor((P - P.min(0)) / max(g, 1e-6)).astype(np.int64)
    d = np.linalg.norm(P - c, axis=1)
    order = np.argsort(-d)
    _, first = np.unique(cell[order], axis=0, return_index=True)
    return P[order[first]]

def smooth_mesh(V, F, crease=35.0):
    """Flat hull triangles -> vertices with normals shared across edges under `crease` degrees."""
    a, b, c = V[F[:, 0]], V[F[:, 1]], V[F[:, 2]]
    fn = np.cross(b - a, c - a); area = np.linalg.norm(fn, axis=1); keep = area > 1e-9
    F, fn, area = F[keep], fn[keep], area[keep]
    fn = fn / area[:, None]
    cos = math.cos(math.radians(crease))
    vf = collections.defaultdict(list)
    for k, f in enumerate(F):
        for v in f: vf[v].append(k)
    outV, outN, outT, key = [], [], [], {}
    for k, f in enumerate(F):
        tri = []
        for v in f:
            ns = [fn[j] * area[j] for j in vf[v] if np.dot(fn[j], fn[k]) >= cos]
            n = np.sum(ns, axis=0); n /= np.linalg.norm(n)
            kk = (v, tuple(np.round(n, 2)))
            if kk not in key:
                key[kk] = len(outV); outV.append(V[v]); outN.append(n)
            tri.append(key[kk])
        outT.append(tri)
    return np.array(outV), np.array(outN), np.array(outT, dtype=int)

def match(name, patterns):
    if isinstance(patterns, str): patterns = [patterns]
    return any(fnmatch.fnmatchcase(name.upper(), p.upper()) for p in patterns)

# ------------------------------------------------------------------ main
def main(step_path, cfg_path, out_path):
    cfg = json.load(open(cfg_path))
    st = Step(load_step(step_path))
    inst = st.instances()
    print(f'{len(inst)} solids in the assembly')
    # CAD -> robot axes (x right, y up, z forward, inches); a reflection is fine: CAD is right-handed, Unity left
    ax = {'+x': [1, 0, 0], '-x': [-1, 0, 0], '+y': [0, 1, 0], '-y': [0, -1, 0], '+z': [0, 0, 1], '-z': [0, 0, -1]}
    right, up, fwd = (np.array(ax[cfg['axes'][k]], float) for k in ('right', 'up', 'forward'))
    C = np.array(cfg['origin'], float)  # CAD point that becomes the robot origin (footprint centre, on the carpet)
    T = np.vstack([right, up, fwd])
    def to_robot(P): return (P - C) @ T.T
    mats = cfg['materials']; mat_index = {m['name']: k for k, m in enumerate(mats)}
    groups = collections.OrderedDict()  # name -> {pivot, axis?, items:[(matIdx, V, F)]}
    skipped = collections.Counter(); tris = 0
    for path, M, s, sc in inst:
        name = path[-1]; full = '/'.join(path)
        if match(full, cfg.get('exclude', [])) or match(name, cfg.get('exclude', [])): skipped['excluded'] += 1; continue
        h = st.hull(s, sc)
        if h is None: skipped['no hull'] += 1; continue
        V, F = h
        W = (M[:3, :3] @ V.T).T + M[:3, 3]
        W = to_robot(W)
        ext = W.max(0) - W.min(0)
        if np.linalg.norm(ext) < cfg.get('minSize', 0.5): skipped['tiny'] += 1; continue
        mat = cfg['defaultMaterial']
        for rule in cfg['colors']:
            if match(full, rule['match']) or match(name, rule['match']): mat = rule['material']; break
        grp = 'Body'; ctr = (W.max(0) + W.min(0)) / 2
        for g in cfg.get('groups', []):
            if not match(full, g['match']): continue
            if g.get('roundOnly') and not (abs(ext[1] - ext[2]) < 0.35 * max(ext[1], ext[2]) and max(ext[1], ext[2]) < g.get('maxDiameter', 99)): continue
            if 'region' in g and not (np.all(ctr >= np.array(g['region'][0])) and np.all(ctr <= np.array(g['region'][1]))): continue
            grp = g['name']; break
        groups.setdefault(grp, []).append((mat_index[mat], W, F))
    print('skipped', dict(skipped))
    parts = []
    for gname, items in groups.items():
        g = next((g for g in cfg.get('groups', []) if g['name'] == gname), {})
        if 'bakeYaw' in g:
            # turn the group about a vertical axis through bakeAbout (robot axes), e.g. so a turret faces forward at rest
            a = math.radians(g['bakeYaw']); about = np.array(g['bakeAbout'], float)
            Ry = np.array([[math.cos(a), 0, math.sin(a)], [0, 1, 0], [-math.sin(a), 0, math.cos(a)]])
            items = [(mi, (W - about) @ Ry.T + about, F) for mi, W, F in items]
        allW = np.vstack([w for _, w, _ in items])
        if g.get('pivot') == 'axisX':   # spins about the robot's right axis through the group's centre
            lo, hi = allW.min(0), allW.max(0); pivot = (lo + hi) / 2
        elif 'pivot' in g and isinstance(g['pivot'], list): pivot = np.array(g['pivot'], float)
        else: pivot = np.zeros(3)
        bymat = collections.defaultdict(lambda: ([], [], []))
        for mi, W, F in items:
            # outward winding for Unity (clockwise seen from outside == cross(b-a, c-a) points out)
            c = W.mean(0); F = F.copy()
            for k, f in enumerate(F):
                a, b, cc = W[f]
                if np.dot(np.cross(b - a, cc - a), a - c) < 0: F[k] = f[[0, 2, 1]]
            v, n, t = smooth_mesh(W, F)
            vs, ns, ts = bymat[mi]
            base = sum(len(x) for x in vs)
            vs.append(v - pivot); ns.append(n); ts.append(t + base)
            tris += len(t)
        meshes = []
        for mi, (vs, ns, ts) in sorted(bymat.items()):
            meshes.append({'material': mi,
                           'v': [round(float(x), 3) for x in np.vstack(vs).ravel()],
                           'n': [round(float(x), 3) for x in np.vstack(ns).ravel()],
                           't': [int(x) for x in np.vstack(ts).ravel()]})
        parts.append({'name': gname, 'pivot': [round(float(x), 3) for x in pivot], 'meshes': meshes})
        print(f'  {gname:22s} {len(items):4d} solids  pivot {np.round(pivot, 2)}')
    for extra in cfg.get('decals', []):  # flat coloured shapes (team logo shapes on plates)
        mi = mat_index[extra['material']]
        P = np.array(extra['points'], float); o = np.array(extra['origin'], float)
        u = np.array(extra['u'], float); vv = np.array(extra['v'], float); nrm = np.cross(u, vv); nrm /= np.linalg.norm(nrm)
        ring = [o + u * p[0] + vv * p[1] for p in P]
        from_center = np.mean(ring, axis=0)
        V = [from_center] + ring; T_ = []
        for k in range(len(ring)): T_.append([0, 1 + k, 1 + (k + 1) % len(ring)])
        V = np.array(V); T_ = np.array(T_)
        if np.dot(np.cross(V[T_[0][1]] - V[T_[0][0]], V[T_[0][2]] - V[T_[0][0]]), nrm) < 0: T_ = T_[:, [0, 2, 1]]
        parts.append({'name': extra['name'], 'pivot': [0, 0, 0], 'meshes': [{'material': mi, 'v': [round(float(x), 3) for x in V.ravel()],
                      'n': [round(float(x), 3) for x in np.tile(nrm, len(V))], 't': [int(x) for x in T_.ravel()]}]})
    out = {'format': 'steamworks-robot-model', 'version': 1, 'units': 'in',
           'axes': 'x right, y up, z forward; origin on the carpet under the footprint centre',
           'source': step_path.split('/')[-1], 'materials': mats, 'parts': parts}
    with open(out_path, 'w') as f: json.dump(out, f, separators=(',', ':'))
    print(f'{tris} triangles -> {out_path}')

if __name__ == '__main__':
    if len(sys.argv) != 4: print(__doc__); sys.exit(1)
    main(*sys.argv[1:])
