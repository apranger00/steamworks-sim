import { colliders, ropes, layout } from '../web/field.js';
import { Sim } from '../web/physics.js';
const lay = layout(); const sim = new Sim(colliders(lay));
const specs = ropes(lay); specs.forEach((s) => sim.addRope(s));
const run = (secs, fn) => { for (let k = 0; k < Math.round(secs * 60); k++) { fn && fn(k / 60); sim.step(1 / 60); } };
let pass = 0, fail = 0; const check = (n, ok, info) => { ok ? pass++ : fail++; console.log((ok ? 'PASS ' : 'FAIL ') + n + '  — ' + info); };
const f = (v) => v.toFixed(1);
console.log('ropes', specs.length, specs.map((s) => s.name + ' @(' + f(s.anchor[0]) + ',' + f(s.anchor[1]) + ')').join('; '));
run(3);
const R = sim.ropes.find((r) => r.name.startsWith('RED') && r.anchor[0] < -230); // the one facing the red wall
const dev = (rp) => { let m = 0; for (let i = 0; i < rp.n; i++) if (rp.z[i] > 1) m = Math.max(m, Math.hypot(rp.x[i] - rp.anchor[0], rp.y[i] - rp.anchor[1])); return m; };
const len = (rp) => { let L = 0; for (let i = 0; i < rp.n - 1; i++) L += Math.hypot(rp.x[i + 1] - rp.x[i], rp.y[i + 1] - rp.y[i], rp.z[i + 1] - rp.z[i]); return L; };
check('ropes hang still and plumb after settling', sim.ropes.every((r) => dev(r) < 1.0), `max off-plumb ${f(Math.max(...sim.ropes.map(dev)))} in; bottom Z ${f(R.z[R.n - 1])}; length ${f(len(R))}/88`);
// Drive the red robot straight through the rope facing the red wall, at full speed
const ry = R.anchor[1] + 8; // off-centre hit so the rope gets swept sideways
const r = sim.addRobot({ x: R.anchor[0] - 80, y: ry, th: 0 });
let vMin = 1e9, maxDev = 0, inside = 0, nan = false, maxLen = 0;
run(1.6, (t) => {
  // steer away from the airship after passing the rope
  r.input.throttle = 1; r.input.turn = t > 0.55 ? -0.8 : 0;
  if (t > 0.3 && t < 0.7) vMin = Math.min(vMin, Math.hypot(r.vx, r.vy));
  maxDev = Math.max(maxDev, dev(R)); maxLen = Math.max(maxLen, len(R));
  for (let i = 1; i < R.n; i++) { if (!Number.isFinite(R.x[i])) nan = true; }
});
check('rope does not slow the robot', vMin > 100, `min robot speed while passing ${f(vMin)} in/s`);
check('rope swings when hit', maxDev > 6, `max swing ${f(maxDev)} in`);
check('rope never stretches', maxLen < 88 * 1.04 && !nan, `max length ${f(maxLen)} in`);
// pinned through touchpad?
let pinOk = true; run(0.1, () => { r.input.throttle = 0; const k = R.pinIndex; if (Math.hypot(R.x[k] - R.pin[0], R.y[k] - R.pin[1]) > R.pinR + 0.3) pinOk = false; });
check('rope stays threaded through the touchpad', pinOk, `pin index ${R.pinIndex}, z ${f(R.z[R.pinIndex])}`);
// park the robot well away and let it settle back
Object.assign(r, { x: 0, y: 0, vx: 0, vy: 0, w: 0 }); r.updateShape();
run(6, () => { r.input.throttle = 0; r.input.turn = 0; });
check('rope settles back after the hit', dev(R) < 1.5, `off-plumb after 6 s ${f(dev(R))} in`);
// Robot parked against the rope: rope bends around, robot not inside rope
const r2 = sim.addRobot({ x: R.anchor[0] - 21, y: R.anchor[1], th: 0 }); run(1.5, () => { r2.input.throttle = 0.3; });
let inR = 0; for (let i = 1; i < R.n; i++) { const P = r2.asPrism(); const dx = R.x[i] - r2.x, dy = R.y[i] - r2.y; const c = Math.cos(-r2.th), s = Math.sin(-r2.th); const lx = c * dx - s * dy, ly = s * dx + c * dy; if (Math.abs(lx) < 19 && Math.abs(ly) < 17 && R.z[i] < 23.5) inR++; }
check('rope wraps around a robot pressed against it (no points inside)', inR === 0, `points inside ${inR}, robot at X ${f(r2.x)} (rope at ${f(R.anchor[0])})`);
// Fuel thrown at a rope
const bi = sim.addBall(R.anchor[0] - 30, R.anchor[1], 30, 150, 0, 40); let bx = 0;
run(0.5, () => { bx = sim.b.x[0]; });
console.log('   ball after hitting rope: x', f(sim.b.x[sim.nBalls - 1]));
const t0 = performance.now(); run(2); const ms = (performance.now() - t0) / 120;
check('6 ropes cost little', ms < 1.5, `${ms.toFixed(2)} ms per frame total sim`);
console.log(`${pass} passed, ${fail} failed`);
