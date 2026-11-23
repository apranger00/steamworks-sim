// Exports the field definition from the browser prototype (prototype/web/field.js, the single source of
// truth for geometry) into JSON files Unity can read with JsonUtility.
//
//   node tools/export_unity_data.mjs
//
// JsonUtility limits: no top-level arrays, no arrays of arrays. So every point list is flattened
// ([x0, y0, x1, y1, ...]) and every file has a root object.
//
// Coordinates stay in the spec frame (inches, origin at field centre, +X toward the blue wall,
// +Y toward the scoring table, +Z up). Unity converts with Units.ToUnity(x, y, z) = (x, z, y) * 0.0254.
import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { layout, colliders, targets, ropes, hoppers, loadingStations, loadingLanes, DIM } from '../prototype/web/field.js';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const out = join(root, 'UnityProject/Assets/Steamworks/Data');
mkdirSync(out, { recursive: true });
// targets and ropes are loaded at runtime with Resources.Load, so they live under Resources/Data
const outRes = join(root, 'UnityProject/Assets/Steamworks/Resources/Data');
mkdirSync(outRes, { recursive: true });

const flat = (pts) => pts.flat();
const r4 = (v) => Math.round(v * 1e4) / 1e4;
const clean = (o) => JSON.parse(JSON.stringify(o, (k, v) => (typeof v === 'number' ? r4(v) : v)));

const lay = layout();
const items = lay.items.map((it) => {
  const o = { type: it.type, name: it.name, mat: it.mat || '', group: it.group || 'field' };
  if (it.type === 'box') Object.assign(o, { c: it.c, size: it.size, rotZ: it.rotZ, tilt: it.tilt || 0 });
  if (it.type === 'tape') Object.assign(o, { mat: 'tape_' + it.color, a: it.a, b: it.b, w: it.w });
  if (it.type === 'loft') Object.assign(o, { ring0: flat(it.ring0), z0: it.z0, ring1: flat(it.ring1), z1: it.z1, capBottom: it.caps[0], capTop: it.caps[1] });
  if (it.type === 'cyl') Object.assign(o, { p0: it.p0, p1: it.p1, r0: it.r0, r1: it.r1, seg: it.seg, open: !!it.open });
  if (it.type === 'helicoid') Object.assign(o, { c: it.c, r0: it.r0, r1: it.r1, z0: it.z0, z1: it.z1, turns: it.turns });
  if (it.type === 'dome') Object.assign(o, { c: it.c, r: it.r });
  return o;
});
const field = {
  version: 1,
  units: 'inches, spec frame (see Units.cs)',
  items,
  fuel: flat(lay.fuel),
  gears: lay.gears.map((g) => ({ c: g.c, rotZ: g.rotZ || 0, reserve: g.reserve || '' })),
  fuelRadius: DIM.fuelR,
  gear: DIM.gear,
};
writeFileSync(join(out, 'field.json'), JSON.stringify(clean(field)));

const cols = { version: 1, prisms: colliders(lay).map((c) => ({ name: c.name, pts: flat(c.pts), z0: c.z0, z1: c.z1, ...(c.pts0 ? { pts0: flat(c.pts0), pts1: flat(c.pts1) } : {}) })) };
writeFileSync(join(out, 'colliders.json'), JSON.stringify(clean(cols)));

const tg = targets();
const alliance = (a) => ({
  side: a.side, baseLineX: a.baseLineX,
  highGoal: { c: a.highGoal.c, z: a.highGoal.z, r: a.highGoal.r, capture: a.highGoal.capture },
  lowGoal: { face: a.lowGoal.face, n: a.lowGoal.n, u: a.lowGoal.u, halfW: a.lowGoal.halfW, z0: a.lowGoal.z0, z1: a.lowGoal.z1 },
  airship: a.airship,
  lifts: a.lifts.map((l) => ({ name: l.name, u: l.u, tip: l.tip, z: l.z })),
  touchpads: a.touchpads.map((t) => ({ c: t.c, z: t.z })),
});
writeFileSync(join(outRes, 'targets.json'), JSON.stringify(clean({ version: 1, red: alliance(tg.red), blue: alliance(tg.blue), hoppers: hoppers(), loading: Object.fromEntries(Object.entries(loadingStations()).map(([k, v]) => [k, { ...v, chutes: v.chutes.flat() }])), lanes: Object.fromEntries(Object.entries(loadingLanes()).map(([k, v]) => [k, { ...v, chutes: v.chutes.flat() }])) })));

writeFileSync(join(outRes, 'ropes.json'), JSON.stringify(clean({ version: 1, ropes: ropes(lay).map((r) => ({
  name: r.name, anchor: r.anchor, pin: r.pin, pinR: r.pinR, length: r.length, radius: r.radius, out: r.out })) })));

console.log(`items ${items.length}, colliders ${cols.prisms.length}, fuel ${lay.fuel.length}, gears ${lay.gears.length}, ropes ${ropes(lay).length}`);
