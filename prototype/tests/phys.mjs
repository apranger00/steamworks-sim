import { colliders, FIELD_OUTLINE } from '../web/field.js';
import { Sim } from '../web/physics.js';
const C = colliders();
const run = (sim, secs, fn) => { const N = Math.round(secs * 60); for (let k = 0; k < N; k++) { fn && fn(k / 60); sim.step(1 / 60); } };
const f1 = (v) => v.toFixed(1);
let pass = 0, fail = 0;
const check = (name, ok, info) => { (ok ? pass++ : fail++); console.log((ok ? 'PASS ' : 'FAIL ') + name + (info ? '  — ' + info : '')); };

// 1. Drive straight from the red wall into the center lift peg
{
  const sim = new Sim(C); const r = sim.addRobot({ x: -326 + 19.25 + 0.5, y: 0, th: 0 });
  let vmax = 0; run(sim, 4, () => { r.input.throttle = 1; vmax = Math.max(vmax, Math.hypot(r.vx, r.vy)); });
  const front = r.x + 19.25;
  check('stops at center peg tip (≈ -226.1)', Math.abs(front - -226.1) < 1.0, `front X ${f1(front)}, top speed ${f1(vmax)} in/s (${f1(vmax / 12)} ft/s), heading ${f1(r.th * 57.3)}°`);
  // time to cross base line (93.3" from wall)
  const sim2 = new Sim(C); const r2 = sim2.addRobot({ x: -326 + 19.25 + 0.5, y: -60, th: 0 });
  let t = null; run(sim2, 3, (tt) => { r2.input.throttle = 1; if (t === null && r2.x - 19.25 > -232.6) t = tt; });
  check('crosses base line with whole robot in < 1.5 s', t !== null && t < 1.5, `t = ${t?.toFixed(2)} s`);
}
// 2. Drive at 30 degrees into the scoring-side rail and along it at full speed
{
  const sim = new Sim(C); const r = sim.addRobot({ x: -100, y: 100, th: 30 / 57.3 });
  let maxY = -1e9; run(sim, 5, () => { r.input.throttle = 1; maxY = Math.max(maxY, ...r.corners.map((c) => c[1])); });
  check('never passes the guardrail (162.4)', maxY <= 162.4 + 0.6, `max corner Y ${f1(maxY)}, final pos (${f1(r.x)}, ${f1(r.y)}) heading ${f1(r.th * 57.3)}°`);
  // inside field outline check for all corners at end
}
// 3. Spin in a corner and ram the boiler / loading station
{
  const sim = new Sim(C); const r = sim.addRobot({ x: -270, y: -120, th: -135 / 57.3 });
  let worst = 0;
  run(sim, 4, (t) => { r.input.throttle = 1; r.input.turn = Math.sin(t * 3); for (const c of r.corners) worst = Math.max(worst, outside(c)); });
  check('stays inside field near red boiler corner', worst < 0.8, `max penetration ${f1(worst)} in`);
  const sim2 = new Sim(C); const r2 = sim2.addRobot({ x: -250, y: 110, th: 150 / 57.3 }); worst = 0;
  run(sim2, 4, (t) => { r2.input.throttle = 1; r2.input.turn = 0.3; for (const c of r2.corners) worst = Math.max(worst, outside(c)); });
  check('stays inside field near red loading station', worst < 0.8, `max penetration ${f1(worst)} in`);
}
function outside(p) { // distance outside the convex outline (0 if inside)
  let m = 0; const n = FIELD_OUTLINE.length;
  for (let i = 0; i < n; i++) { const a = FIELD_OUTLINE[i], b = FIELD_OUTLINE[(i + 1) % n]; const dx = b[0] - a[0], dy = b[1] - a[1], L = Math.hypot(dx, dy); const s = ((p[0] - a[0]) * dy - (p[1] - a[1]) * dx) / L; m = Math.max(m, s); }
  return m;
}
// 4. Robot pushes a ball
{
  const sim = new Sim(C); const r = sim.addRobot({ x: -40, y: 0, th: 0 }); sim.addBall(0, 0, 2.5);
  run(sim, 1.2, () => { r.input.throttle = 1; });
  const b = sim.b;
  check('ball is pushed ahead of robot', b.x[0] > r.x + 19.25 + 1, `ball X ${f1(b.x[0])} vx ${f1(b.vx[0])}, robot front ${f1(r.x + 19.25)} v ${f1(r.vx)}`);
}
// 5. Drop a pile of 200 balls and let it settle
{
  const sim = new Sim(C);
  for (let k = 0; k < 200; k++) sim.addBall(-20 + (k % 10) * 5.2, -25 + Math.floor(k / 10) % 10 * 5.2, 10 + Math.floor(k / 100) * 6 + (k % 7) * 0.3);
  run(sim, 6);
  const b = sim.b; let minD = 1e9, maxZ = 0, asleep = 0, maxSp = 0;
  for (let i = 0; i < sim.nBalls; i++) { asleep += b.sleep[i]; maxZ = Math.max(maxZ, b.z[i]); maxSp = Math.max(maxSp, Math.hypot(b.vx[i], b.vy[i], b.vz[i]));
    for (let j = i + 1; j < sim.nBalls; j++) minD = Math.min(minD, Math.hypot(b.x[i] - b.x[j], b.y[i] - b.y[j], b.z[i] - b.z[j])); }
  check('pile settles without overlap', minD > 4.6 && sim.nBalls === 200, `min centre distance ${minD.toFixed(2)} (diam 5), asleep ${asleep}/200, max Z ${f1(maxZ)}, max speed ${f1(maxSp)}`);
  // drive through the pile
  const r = sim.addRobot({ x: -120, y: 0, th: 0 }); let vmin = 1e9; const trace = [];
  run(sim, 3, (t) => { r.input.throttle = 1; if (t > 0.8 && t < 2.4) vmin = Math.min(vmin, r.vx); if (Math.round(t * 60) % 9 === 0) trace.push(`${t.toFixed(2)}s x${r.x.toFixed(0)} v${r.vx.toFixed(0)}`); });
  console.log('   plow trace:', trace.join(' | '));
  let inside = 0; for (let i = 0; i < sim.nBalls; i++) { const T = {}; if (Math.abs(b.x[i] - r.x) < 19.25 && Math.abs(b.y[i] - r.y) < 17.25 && b.z[i] < 24) inside++; }
  check('robot plows through pile; no balls inside robot', inside === 0, `balls inside robot ${inside}, robot at X ${f1(r.x)}, min speed while plowing ${f1(vmin)} in/s, balls remaining ${sim.nBalls}`);
}
// 6. Gear push and idle-defender push
{
  const sim = new Sim(C); const r = sim.addRobot({ x: -60, y: 0, th: 0 }); const g = sim.addGear(-30, 3);
  run(sim, 1.5, () => { r.input.throttle = 0.6; });
  check('gear is pushed', g.x > -30 + 20, `gear X ${f1(g.x)}, robot front ${f1(r.x + 19.25)}`);
  for (const [lab, th, off] of [['T-bone', Math.PI / 2, 34.5 / 2], ['head-on', 0, 38.5 / 2]]) {
    const sim2 = new Sim(C); const a = sim2.addRobot({ x: -60, y: 0, th: 0 }); const d = sim2.addRobot({ x: -60 + 19.25 + off + 1, y: 0, th, mode: 'idle', alliance: 'blue' });
    const d0 = d.x; run(sim2, 3, () => { a.input.throttle = 1; });
    check(`pusher moves a braked defender slowly (${lab})`, d.x - d0 > 10 && d.x - d0 < 160, `defender moved ${f1(d.x - d0)} in in 3 s, pusher speed now ${f1(a.vx)} in/s`);
  }
}
// 7. Performance: 400 active balls being plowed
{
  const sim = new Sim(C); const r = sim.addRobot({ x: -200, y: 0, th: 0 });
  for (let k = 0; k < 400; k++) sim.addBall(-150 + (k % 20) * 6, -60 + Math.floor(k / 20) * 6, 2.5 + Math.random() * 20, (Math.random() - .5) * 50, (Math.random() - .5) * 50, 0);
  const t0 = performance.now(); run(sim, 4, () => { r.input.throttle = 1; r.input.turn = 0.2; }); const ms = (performance.now() - t0) / 240;
  check('physics step under 4 ms/frame with 400 balls', ms < 4, `${ms.toFixed(2)} ms per 1/60 s frame (Node)`);
}
console.log(`\n${pass} passed, ${fail} failed`);
