#!/usr/bin/env python3
"""
Athena (FRC 1986 Team Titanium, 2017): model.json built from simple shapes (no CAD of this robot is public).

    python3 tools/robots/athena_model.py UnityProject/Assets/StreamingAssets/Robots/athena/model.json

Robot axes, inches: x right, y up, z forward, origin on the carpet under the footprint centre. Frame 32 wide x 27 long
x 23.5 tall (robot.json). Every shape is a convex hull of its corner points, written in the same model.json format as
tools/step_to_robot_model.py. From the team's photos: green frame with lightening holes, plywood belly, four mecanum
wheels, a clear polycarbonate hopper over the whole robot, the "angry eyes" gear slot on the front with the orange
compliant-wheel FUEL intake under it, an orange belt carrying FUEL up behind the front panel, a large indexer wheel on
the left, the turret in the back left corner and the climber winch across the back.
"""
import sys, os, json, math, collections
import numpy as np
from scipy.spatial import ConvexHull
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
from step_to_robot_model import smooth_mesh

W, L, H = 32.0, 27.0, 23.5
hw, hl = W / 2, L / 2

MATS = [
    ('titanium green', [0.10, 0.46, 0.22, 1], 0.45, 0.5),
    ('polycarbonate', [0.82, 0.9, 1.0, 0.28], 0.0, 0.85),
    ('aluminium', [0.72, 0.74, 0.77, 1], 0.8, 0.55),
    ('steel', [0.55, 0.57, 0.6, 1], 0.85, 0.6),
    ('black', [0.06, 0.06, 0.07, 1], 0.1, 0.35),
    ('rubber', [0.05, 0.05, 0.05, 1], 0.0, 0.12),
    ('orange', [0.95, 0.47, 0.08, 1], 0.0, 0.4),
    ('white', [0.93, 0.94, 0.95, 1], 0.0, 0.45),
    ('wood', [0.78, 0.62, 0.40, 1], 0.0, 0.3),
    ('iris', [0.16, 0.52, 0.56, 1], 0.1, 0.6),
    ('brow', [0.35, 0.37, 0.4, 1], 0.2, 0.4),
    ('roller grey', [0.62, 0.64, 0.66, 1], 0.3, 0.4),
]
MI = {m[0]: k for k, m in enumerate(MATS)}
parts = collections.OrderedDict()   # name -> {'pivot': np3, 'items': [(mat, points)]}

def part(name, pivot=(0, 0, 0)):
    if name not in parts: parts[name] = {'pivot': np.array(pivot, float), 'items': []}
    return parts[name]

def add(pname, mat, pts):
    part(pname)['items'].append((MI[mat], np.array(pts, float)))

def box(pname, mat, c, s):
    c = np.array(c, float); h = np.array(s, float) / 2
    add(pname, mat, [c + h * np.array([i, j, k]) for i in (-1, 1) for j in (-1, 1) for k in (-1, 1)])

def frame_axis(axis):
    a = np.array(axis, float); a /= np.linalg.norm(a)
    u = np.cross(a, [0, 1, 0] if abs(a[1]) < 0.9 else [1, 0, 0]); u /= np.linalg.norm(u)
    return a, u, np.cross(a, u)

def cyl(pname, mat, c, axis, r, length, n=16, r2=None):
    a, u, v = frame_axis(axis); c = np.array(c, float); r2 = r if r2 is None else r2
    pts = []
    for k in range(n):
        t = 2 * math.pi * k / n
        d = math.cos(t) * u + math.sin(t) * v
        pts += [c - a * length / 2 + d * r, c + a * length / 2 + d * r2]
    add(pname, mat, pts)

def flat(pname, mat, poly2d, origin, ux, uy, t=0.08):
    """A thin plate: convex 2D outline (in ux/uy units) at origin, t thick along ux x uy."""
    ux, uy, o = np.array(ux, float), np.array(uy, float), np.array(origin, float)
    n = np.cross(ux, uy); n /= np.linalg.norm(n)
    ring = [o + ux * p[0] + uy * p[1] for p in poly2d]
    add(pname, mat, [q for q in ring] + [q + n * t for q in ring])

def ellipse(cx, cy, rx, ry, n=14, rot=0.0):
    c, s = math.cos(rot), math.sin(rot)
    return [(cx + rx * math.cos(2 * math.pi * k / n) * c - ry * math.sin(2 * math.pi * k / n) * s,
             cy + rx * math.cos(2 * math.pi * k / n) * s + ry * math.sin(2 * math.pi * k / n) * c) for k in range(n)]

# ------------------------------------------------------------------ drive base
B = 'Body'
y0, y1 = 1.5, 6.0                                   # frame rail bottom / top
for sx in (-1, 1):
    box(B, 'titanium green', (sx * (hw - 1), (y0 + y1) / 2, 0), (2, y1 - y0, L))            # side rails
    for z in np.arange(-hl + 2.5, hl - 2, 3.0):                                              # lightening holes
        cyl(B, 'black', (sx * (hw + 0.01), (y0 + y1) / 2, z), (1, 0, 0), 0.9, 0.06, n=12)
for sz in (-1, 1):
    box(B, 'titanium green', (0, (y0 + y1) / 2, sz * (hl - 1)), (W - 4, y1 - y0, 2))          # front / back rails
    for x in np.arange(-hw + 4, hw - 3, 3.0):
        cyl(B, 'black', (x, (y0 + y1) / 2, sz * (hl + 0.01)), (0, 0, 1), 0.9, 0.06, n=12)
box(B, 'wood', (0, y1 - 0.25, 0), (W - 4, 0.5, L - 4))                                        # plywood belly pan

# mecanum wheels, 6 in, inside the side rails
for side, sx in (('L', -1), ('R', 1)):
    for k, sz in enumerate((1, -1)):
        name = f'Wheel {side}{k}'
        c = np.array([sx * (hw - 3.4), 3.0, sz * (hl - 4.5)])
        p = part(name, c)
        cyl(name, 'steel', c, (1, 0, 0), 2.1, 2.0, n=18)                                     # hub plates
        for j in range(10):                                                                   # 45 deg rollers
            t = 2 * math.pi * j / 10
            radial = np.array([0, math.cos(t), math.sin(t)])
            tang = np.array([0, -math.sin(t), math.cos(t)])
            ax = np.array([1.0, 0, 0]) * math.cos(math.pi / 4) + tang * math.sin(math.pi / 4) * (1 if sx * sz > 0 else -1)
            cyl(name, 'roller grey', c + radial * 2.55, ax, 0.45, 2.4, n=8)

# ------------------------------------------------------------------ hopper (clear walls, green posts)
yb, yt = y1, H
for sx in (-1, 1):
    box(B, 'polycarbonate', (sx * (hw - 0.6), (yb + yt) / 2, 0), (0.12, yt - yb, L - 1.4))
for sz in (-1, 1):
    box(B, 'polycarbonate', (0, (yb + yt) / 2, sz * (hl - 0.6)), (W - 1.4, yt - yb, 0.12))
for sx in (-1, 1):
    for sz in (-1, 1):
        box(B, 'titanium green', (sx * (hw - 1), (yb + yt) / 2, sz * (hl - 1)), (1.2, yt - yb, 1.2))
    box(B, 'titanium green', (sx * (hw - 1), (yb + yt) / 2, 0), (1.0, yt - yb, 1.0))
    box(B, 'titanium green', (sx * (hw - 1), yt - 0.5, 0), (1.2, 1.0, L))                     # top rails
for sz in (-1, 1):
    box(B, 'titanium green', (0, yt - 0.5, sz * (hl - 1)), (W, 1.0, 1.2))
for x in (-6.5, 6.5):                                                                         # front posts by the eyes
    box(B, 'titanium green', (x, (yb + yt) / 2, hl - 1), (1.0, yt - yb, 1.0))

# ------------------------------------------------------------------ front: the angry eyes and the gear slot
zf = hl + 0.05
# U-shaped gear guide: two straights and a half circle of short plates
for x in (-5.6, 5.6):
    box(B, 'aluminium', (x, 15.5, zf + 0.1), (0.9, 9.0, 0.25))
for k in range(12):
    a0, a1 = math.pi + math.pi * k / 12, math.pi + math.pi * (k + 1) / 12
    pts = []
    for a in (a0, a1):
        for r in (4.7, 6.05):
            pts.append((r * math.cos(a), 11.0 + r * math.sin(a)))
    flat(B, 'aluminium', pts, (0, 0, zf), (1, 0, 0), (0, 1, 0), t=0.25)
# eye plates (white), brows (grey), irises
for sx in (-1, 1):
    plate = [(sx * 6.4, 7.5), (sx * 11.6, 7.5), (sx * 12.2, 18.6), (sx * 10.6, 21.2), (sx * 6.9, 20.0)]
    flat(B, 'white', plate, (0, 0, zf + 0.3), (1, 0, 0), (0, 1, 0), t=0.12)
    brow = [(sx * 6.7, 19.3), (sx * 12.0, 17.4), (sx * 12.1, 18.7), (sx * 7.0, 20.6)]
    flat(B, 'brow', brow, (0, 0, zf + 0.45), (1, 0, 0), (0, 1, 0), t=0.1)
    flat(B, 'white', ellipse(sx * 9.2, 16.2, 2.1, 1.05, rot=sx * -0.35), (0, 0, zf + 0.45), (1, 0, 0), (0, 1, 0), t=0.06)
    flat(B, 'iris', ellipse(sx * 8.7, 16.25, 1.05, 0.85), (0, 0, zf + 0.53), (1, 0, 0), (0, 1, 0), t=0.05)
    flat(B, 'black', ellipse(sx * 8.6, 16.3, 0.45, 0.45, n=10), (0, 0, zf + 0.6), (1, 0, 0), (0, 1, 0), t=0.04)
# two black rods across the top front, the pneumatics on the front right
for y in (20.0, 22.4):
    cyl(B, 'black', (0, y, hl + 0.9), (1, 0, 0), 0.45, W - 1, n=10)
cyl(B, 'white', (12.0, 10.0, hl - 3.5), (0, 0, 1), 1.4, 6.0, n=14)                            # air tank
for x in (10.5, 13.0):
    cyl(B, 'steel', (x, 13.2, hl - 1.2), (0, 0, 1), 0.75, 0.6, n=12)                          # gauges
# orange belt carrying FUEL up behind the front panel
for k, x in enumerate(np.arange(-4.5, 4.6, 1.5)):
    box(B, 'orange', (x, 14.0, hl - 2.6), (0.9, 14.0, 0.25))
box(B, 'black', (0, 14.0, hl - 3.0), (9.6, 14.6, 0.3))

# floor intake: white shaft with orange compliant wheels, in the bumper opening under the gear slot
IR = 'Intake roller'
ic = np.array([0, 3.2, hl + 2.2]); part(IR, ic)
cyl(IR, 'white', ic, (1, 0, 0), 0.55, 20.0, n=10)
for x in list(np.arange(-9.0, -2.4, 1.1)) + list(np.arange(2.4, 9.1, 1.1)):
    cyl(IR, 'orange', ic + np.array([x, 0, 0]), (1, 0, 0), 1.55, 0.7, n=10)
CR = 'Conveyor roller 0'
cc = np.array([0, 7.5, hl - 2.8]); part(CR, cc)
cyl(CR, 'black', cc, (1, 0, 0), 0.8, 10.0, n=12)

# ------------------------------------------------------------------ indexer wheel on the left
ic2 = np.array([-hw + 2.2, 13.5, 1.0])
cyl(B, 'titanium green', ic2, (1, 0, 0), 7.5, 0.4, n=28)
for k in range(8):
    a = 2 * math.pi * k / 8
    d = np.array([0, math.cos(a), math.sin(a)])
    cyl(B, 'black', ic2 + d * 4.6 + np.array([0.21, 0, 0]), (1, 0, 0), 1.4, 0.05, n=12)
cyl(B, 'steel', ic2 + np.array([0.3, 0, 0]), (1, 0, 0), 1.0, 0.5, n=12)

# ------------------------------------------------------------------ turret, back left (exitOffset [-6.5, 9.5], exitHeight 17)
TY = 'Turret yaw'
tc = np.array([-9.5, 17.0 - 5.0, -6.5]); part(TY, tc)
cyl(TY, 'titanium green', tc, (0, 1, 0), 5.0, 0.6, n=24)                                       # turret ring
for sx in (-1, 1):
    flat(TY, 'titanium green', [(-4.0, 0), (4.0, 0), (4.0, 7.5), (1.0, 9.6), (-3.5, 8.6)],
         tc + np.array([sx * 2.6, 0.3, 0]), (0, 0, 1), (0, 1, 0), t=0.25)                     # side plates
cyl(TY, 'black', tc + np.array([0, 5.0, -0.5]), (1, 0, 0), 2.0, 4.6, n=16)                     # flywheel
for k in range(7):                                                                             # hood arc round the back
    a0 = math.radians(-20 + 15 * k); a1 = a0 + math.radians(15)
    pts = []
    for a in (a0, a1):
        for r in (6.2, 6.7):
            pts.append((-r * math.cos(a), r * math.sin(a)))
    flat(TY, 'aluminium', [(p[0] - 0.5, p[1] + 5.0) for p in pts], tc + np.array([2.3, 0, 0]), (0, 0, 1), (0, 1, 0), t=4.6)
cyl(TY, 'motor' if 'motor' in MI else 'steel', tc + np.array([0, 2.0, -3.2]), (0, 1, 0), 1.25, 3.5, n=12)

# ------------------------------------------------------------------ climber across the back
cyl(B, 'black', (0, 21.0, -hl - 0.8), (1, 0, 0), 1.3, 10.0, n=16)                              # velcro drum
cyl(B, 'steel', (7.2, 21.0, -hl - 0.8), (1, 0, 0), 1.25, 4.0, n=14)                            # motor
for x in (-5.6, 5.6):
    box(B, 'titanium green', (x, 19.5, -hl - 0.5), (0.4, 4.5, 2.2))

# ------------------------------------------------------------------ write
def mesh_of(points):
    h = ConvexHull(points, qhull_options='QJ')
    idx = np.unique(h.simplices); remap = np.full(len(points), -1); remap[idx] = np.arange(len(idx))
    V = points[idx]; F = remap[h.simplices]
    c = V.mean(0)
    for k, f in enumerate(F):
        a, b, cc = V[f]
        if np.dot(np.cross(b - a, cc - a), a - c) < 0: F[k] = f[[0, 2, 1]]
    return smooth_mesh(V, F)

out_parts, tris = [], 0
for name, p in parts.items():
    bymat = collections.defaultdict(lambda: ([], [], []))
    for mi, pts in p['items']:
        v, n, t = mesh_of(pts)
        vs, ns, ts = bymat[mi]
        base = sum(len(x) for x in vs)
        vs.append(v - p['pivot']); ns.append(n); ts.append(t + base); tris += len(t)
    meshes = [{'material': mi, 'v': [round(float(x), 3) for x in np.vstack(vs).ravel()],
               'n': [round(float(x), 3) for x in np.vstack(ns).ravel()], 't': [int(x) for x in np.vstack(ts).ravel()]}
              for mi, (vs, ns, ts) in sorted(bymat.items())]
    out_parts.append({'name': name, 'pivot': [round(float(x), 3) for x in p['pivot']], 'meshes': meshes})
model = {'format': 'steamworks-robot-model', 'version': 1, 'units': 'in',
         'axes': 'x right, y up, z forward; origin on the carpet under the footprint centre',
         'source': 'tools/robots/athena_model.py (from photos)',
         'materials': [{'name': m[0], 'color': m[1], 'metal': m[2], 'smooth': m[3]} for m in MATS], 'parts': out_parts}
with open(sys.argv[1], 'w') as f: json.dump(model, f, separators=(',', ':'))
print(f'{tris} triangles, {len(out_parts)} parts -> {sys.argv[1]}')
