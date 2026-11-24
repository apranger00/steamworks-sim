#!/usr/bin/env python3
"""
Athena (FRC 1986 Team Titanium, 2017): model.json built from simple shapes (no CAD of this robot is public).

    python3 tools/robots/athena_model.py UnityProject/Assets/StreamingAssets/Robots/athena/model.json

Robot axes, inches: x right, y up, z forward, origin on the carpet under the footprint centre. Frame 29 x 29 x 23.5
tall (robot.json). Every shape is a convex hull of its corner points, written in the same model.json format as
tools/step_to_robot_model.py. From the team's photos (2017 videos):
  front (gear side): a green module across the whole front: the GEAR held upright between two white "fangs" with green
    arcs over them, a grey U track under it, orange belts carrying FUEL up behind it, a black hook bar and green rail
    above, a green funnel leaning out over the top (FUEL from the chute), pneumatics (gauge, regulator) at the right,
    and the orange compliant-wheel floor intake under it all (FUEL goes under the gear mechanism, then up and in);
  middle: plywood deck, a round aluminium tub with a green eight-point star spinner on its floor (the indexer), clear
    walls that lean outward toward the top;
  right side, a little back of centre: the shooter, a double grey flywheel on a curved green arm;
  back: green climber housing with the winch drum, the green camera light ring at the back right;
  under it all: green frame rails with lightening holes and four mecanum wheels.
"""
import sys, os, json, math, collections
import numpy as np
from scipy.spatial import ConvexHull
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
from step_to_robot_model import smooth_mesh

W, L, H = 29.0, 29.0, 23.5
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
    ('tread', [0.58, 0.6, 0.62, 1], 0.0, 0.25),
    ('glow', [0.35, 1.0, 0.55, 1], 0.0, 0.9),
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
    cyl(B, 'black', (x, (y0 + y1) / 2, -hl - 0.01), (0, 0, 1), 0.9, 0.06, n=12)
box(B, 'wood', (0, y1 - 0.25, -1.0), (W - 2, 0.5, L - 4))                                    # plywood deck

# mecanum wheels, 6 in, inside the side rails
for side, sx in (('L', -1), ('R', 1)):
    for k, sz in enumerate((1, -1)):
        name = f'Wheel {side}{k}'
        c = np.array([sx * (hw - 3.4), 3.0, sz * (hl - 4.5)])
        part(name, c)
        cyl(name, 'steel', c, (1, 0, 0), 2.1, 2.0, n=18)
        for j in range(10):                                                                   # 45 deg rollers
            t = 2 * math.pi * j / 10
            radial = np.array([0, math.cos(t), math.sin(t)])
            tang = np.array([0, -math.sin(t), math.cos(t)])
            ax = np.array([1.0, 0, 0]) * math.cos(math.pi / 4) + tang * math.sin(math.pi / 4) * (1 if sx * sz > 0 else -1)
            cyl(name, 'roller grey', c + radial * 2.55, ax, 0.45, 2.4, n=8)

# ------------------------------------------------------------------ hopper: clear walls leaning outward, green posts
zf_mod = 8.0                       # the front module runs from here to the front of the frame
yb, yt, lean = y1, H, 1.6          # walls lean out this far at the top
def wall(p0, p1, p2, p3, mat='polycarbonate', t=0.12):
    P = [np.array(p, float) for p in (p0, p1, p2, p3)]
    n = np.cross(P[1] - P[0], P[3] - P[0]); n /= np.linalg.norm(n)
    add(B, mat, P + [p + n * t for p in P])
for sx in (-1, 1):   # sides, from the back to the front module
    x0, x1 = sx * (hw - 0.6), sx * (hw - 0.6 + lean)
    wall((x0, yb, -hl + 0.6), (x0, yb, zf_mod), (x1, yt, zf_mod), (x1, yt, -hl + 0.6 - lean))
wall((-hw + 0.6, yb, -hl + 0.6), (hw - 0.6, yb, -hl + 0.6), (hw - 0.6 + lean, yt, -hl + 0.6 - lean), (-hw + 0.6 - lean, yt, -hl + 0.6 - lean))
for sx in (-1, 1):
    box(B, 'titanium green', (sx * (hw - 0.6), (yb + yt) / 2, zf_mod), (1.1, yt - yb, 1.1))       # posts at the module
    box(B, 'titanium green', (sx * (hw - 0.4), (yb + yt) / 2 - 2, -hl + 0.5), (1.1, yt - yb - 4, 1.1))
    box(B, 'titanium green', (sx * (hw - 0.6 + lean), yt - 3.5, -2.0), (0.8, 0.8, L - 6))          # side rail, top

# ------------------------------------------------------------------ the indexer: round tub, green star spinner
tc = np.array([-3.5, y1, -1.5]); R_tub, tub_h = 9.6, 7.5
for k in range(28):                                                                            # aluminium band
    a0, a1 = 2 * math.pi * k / 28, 2 * math.pi * (k + 1) / 28
    pts = []
    for a in (a0, a1):
        for r in (R_tub, R_tub + 0.12):
            for y in (0.5, tub_h):
                pts.append(tc + np.array([r * math.cos(a), y, r * math.sin(a)]))
    add(B, 'aluminium', pts)
IX = 'Indexer'
part(IX, tc)
tips, valleys = 8, []
for k in range(tips):
    a = 2 * math.pi * k / tips
    tip = np.array([7.6 * math.cos(a), 1.1, 7.6 * math.sin(a)])
    v0 = np.array([4.3 * math.cos(a - math.pi / tips), 1.4, 4.3 * math.sin(a - math.pi / tips)])
    v1 = np.array([4.3 * math.cos(a + math.pi / tips), 1.4, 4.3 * math.sin(a + math.pi / tips)])
    centre = np.array([0, 2.6, 0])
    add(IX, 'titanium green', [tc + p for p in (centre, v0, tip, v1)] + [tc + p - np.array([0, 0.25, 0]) for p in (v0, tip, v1)])
    d = np.array([math.cos(a), 0, math.sin(a)])                                                # arm out to the wall
    box_c = tc + d * 6.0 + np.array([0, 1.4, 0])
    add(IX, 'titanium green', [box_c + d * s1 * 3.4 + np.array([-d[2], 0, d[0]]) * s2 * 0.2 + np.array([0, s3 * 0.35, 0])
                               for s1 in (-1, 1) for s2 in (-1, 1) for s3 in (-1, 1)])

# ------------------------------------------------------------------ front module (gear side)
zf = hl + 0.05
for sx in (-1, 1):
    box(B, 'titanium green', (sx * (hw - 0.8), 14.0, (zf_mod + hl) / 2), (0.25, 16.0, hl - zf_mod))  # side plates
    for y in (8.0, 19.5):
        box(B, 'titanium green', (sx * (hw - 0.8), y, hl - 0.6), (1.0, 1.0, 1.0))
box(B, 'titanium green', (0, 21.6, hl - 0.6), (W - 1, 1.0, 1.0))                                # rail across the top
cyl(B, 'black', (0, 20.2, hl + 0.2), (1, 0, 0), 0.4, W - 2, n=10)                               # hook bar
for x in np.arange(-12, 12.1, 2.0):
    box(B, 'black', (x, 19.75, hl + 0.35), (0.25, 0.6, 0.25))
for x in np.arange(-6.5, 6.6, 1.3):                                                             # orange belts
    box(B, 'orange', (x, 13.5, hl - 3.0), (0.8, 13.5, 0.25))
box(B, 'black', (0, 13.5, hl - 3.4), (9.0, 14.0, 0.3))
box(B, 'aluminium', (0, 11.0, hl - 1.2), (W - 3, 0.6, 0.6))                                    # cross bar behind the fangs
# grey U track under the GEAR, white fangs either side, green arcs over them
for k in range(12):
    a0, a1 = math.pi + math.pi * k / 12, math.pi + math.pi * (k + 1) / 12
    pts = [(r * math.cos(a), 12.5 + r * math.sin(a)) for a in (a0, a1) for r in (5.8, 6.8)]
    flat(B, 'aluminium', pts, (0, 0, zf + 0.6), (1, 0, 0), (0, 1, 0), t=0.6)
for sx in (-1, 1):
    fang = [(sx * 9.2, 19.8), (sx * 5.5, 19.8), (sx * 5.7, 9.0), (sx * 6.9, 6.8), (sx * 8.8, 9.6)]
    flat(B, 'white', fang, (0, 0, zf + 1.3), (1, 0, 0), (0, 1, 0), t=0.18)
    for k in range(6):                                                                         # eyebrow arc
        a0 = math.radians(100 + 10 * k) if sx < 0 else math.radians(80 - 10 * k)
        a1 = a0 + (math.radians(10) if sx < 0 else -math.radians(10))
        pts = [(sx * 3.0 + r * math.cos(a), 13.2 + r * math.sin(a)) for a in (a0, a1) for r in (6.6, 7.5)]
        flat(B, 'titanium green', pts, (0, 0, zf + 1.5), (1, 0, 0), (0, 1, 0), t=0.2)
# green funnel leaning out over the top (FUEL from the chute drops in here)
for x0, x1 in ((-13.5, -1.5), (1.5, 13.5)):
    add(B, 'titanium green', [np.array(p, float) for p in
        [(x0, 18.8, zf_mod + 1.0), (x1, 18.8, zf_mod + 1.0), (x0, H - 0.2, hl + 0.8), (x1, H - 0.2, hl + 0.8),
         (x0, 18.6, zf_mod + 1.2), (x1, 18.6, zf_mod + 1.2), (x0, H - 0.4, hl + 1.0), (x1, H - 0.4, hl + 1.0)]])
# pneumatics on the front right
cyl(B, 'white', (11.0, 11.0, hl + 0.5), (0, 0, 1), 1.0, 0.5, n=16)                              # gauge
cyl(B, 'black', (11.0, 11.0, hl + 0.8), (0, 0, 1), 0.85, 0.08, n=16)
cyl(B, 'black', (11.0, 14.5, hl - 0.2), (0, 1, 0), 0.6, 1.6, n=12)                              # regulator
cyl(B, 'steel', (11.0, 8.2, hl - 0.4), (1, 0, 0), 0.4, 4.0, n=10)
cyl(B, 'white', (9.0, 7.6, hl - 4.0), (1, 0, 0), 1.3, 7.0, n=14)                                # air tank

# floor intake: white shaft with orange compliant wheels, under the gear mechanism in the bumper opening
IR = 'Intake roller'
ic = np.array([0, 3.2, hl + 2.2]); part(IR, ic)
cyl(IR, 'white', ic, (1, 0, 0), 0.55, 25.0, n=10)          # full width (robot.json intake.floorWidth 25)
for x in list(np.arange(-12.0, -1.0, 1.0)) + list(np.arange(1.5, 12.1, 1.0)):
    cyl(IR, 'orange' if int(round(x * 10)) % 20 < 10 else 'black', ic + np.array([x, 0, 0]), (1, 0, 0), 1.5, 0.6, n=10)
CR = 'Conveyor roller 0'
cc = np.array([0, 7.0, hl - 2.4]); part(CR, cc)
cyl(CR, 'orange', cc, (1, 0, 0), 0.9, 13.0, n=12)

# ------------------------------------------------------------------ shooter: double flywheel on a curved green arm
# robot.json: shooter.exitOffset [-4.5, -12] (forward, left) and exitHeight 19.5 (the flywheel axle: the top of the
# 8 in wheel is flush with the top of the hopper, 23.5 in); the turret node
# sits 5 in under the axle and the shot leaves along its +z
TY = 'Turret yaw'
sc = np.array([12.0, 19.5 - 5.0, -4.5]); part(TY, sc)
cyl(TY, 'titanium green', sc, (0, 1, 0), 3.2, 0.5, n=20)                                        # bearing plate
arm = [(-5.5, 0.0), (4.5, 0.0), (5.5, 3.0), (4.0, 6.6), (1.0, 8.6), (-3.0, 8.8), (-6.0, 6.2)]
flat(TY, 'titanium green', arm, sc + np.array([2.2, 0.2, 0]), (0, 0, 1), (0, 1, 0), t=0.3)        # outboard arm
flat(TY, 'titanium green', arm, sc + np.array([-1.9, 0.2, 0]), (0, 0, 1), (0, 1, 0), t=0.25)      # inboard plate
for k in range(3):                                                                              # clear hood round the back
    a0 = math.radians(-20 + 20 * k); a1 = a0 + math.radians(20)
    pts = [(-r * math.cos(a) - 0.5, 5.0 + r * math.sin(a)) for a in (a0, a1) for r in (6.3, 6.5)]
    flat(TY, 'polycarbonate', pts, sc + np.array([1.9, 0, 0]), (0, 0, 1), (0, 1, 0), t=3.8)
box(TY, 'black', sc + np.array([2.7, 3.0, 3.5]), (0.6, 1.2, 3.0))                                 # handle-like bracket
cyl(TY, 'steel', sc + np.array([-2.6, 5.0, -0.5]), (1, 0, 0), 1.2, 2.5, n=12)                    # motor
FW = 'Turret yaw/Flywheel'
fc = sc + np.array([0, 5.0, -0.5]); part(FW, fc)
for dx in (-0.9, 0.9):
    cyl(FW, 'tread', fc + np.array([dx, 0, 0]), (1, 0, 0), 4.0, 1.6, n=24)
    cyl(FW, 'black', fc + np.array([dx * 1.1, 0, 0]), (1, 0, 0), 2.6, 1.7, n=16)

# ------------------------------------------------------------------ back: climber housing, winch drum, camera light
for x in (-13.5, -4.5):
    flat(B, 'titanium green', [(-2.5, 0), (1.0, 0), (1.0, 15.5), (-0.5, 17.0), (-2.5, 16.0)],
         (x, y1, -hl + 0.5), (0, 0, 1), (0, 1, 0), t=0.25)
box(B, 'titanium green', (-9.0, y1 + 16.3, -hl + 0.4), (9.5, 0.3, 3.4))
cyl(B, 'black', (-9.0, y1 + 13.5, -hl - 0.3), (1, 0, 0), 1.4, 8.6, n=16)                          # winch drum
cyl(B, 'steel', (-2.0, y1 + 13.5, -hl + 0.2), (1, 0, 0), 1.25, 4.0, n=14)                         # motor
for k in range(16):                                                                              # camera light ring
    a0, a1 = 2 * math.pi * k / 16, 2 * math.pi * (k + 1) / 16
    pts = [(r * math.cos(a), r * math.sin(a)) for a in (a0, a1) for r in (1.0, 1.5)]
    flat(B, 'glow', pts, (6.0, 21.5, -hl - 0.2), (1, 0, 0), (0, 1, 0), t=0.3)
box(B, 'black', (6.0, 21.5, -hl + 0.3), (3.6, 3.6, 0.8))
box(B, 'white', (11.5, 21.0, 6.0), (4.0, 2.5, 1.0))                                              # radio

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
