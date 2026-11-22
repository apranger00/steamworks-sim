import { targets, colliders, ropes, layout } from '../web/field.js';
import { Match, RULES } from '../web/game.js';
import { Sim } from '../web/physics.js';
import { fireHigh, aimHigh, dumpLow, gearAlign, MECH } from '../web/mechanisms.js';
const TG = targets();
let pass = 0, fail = 0;
const check = (n, ok, info = '') => { ok ? pass++ : fail++; console.log((ok ? 'PASS ' : 'FAIL ') + n + (info ? '  — ' + info : '')); };
const runTo = (m, tEnd, dt = 1 / 60, robots = [], each) => { while (m.state !== 'post' && m.t < tEnd - 1e-9) { const step = Math.min(dt, tEnd - m.t); each && each(m.t); m.update(step, robots); } };
const load = (m, al, goal, n) => { for (let i = 0; i < n; i++) m.fuelLoaded(al, goal); };

// --- Timing
{
  for (const dt of [1 / 60, 1 / 30, 0.037, 1 / 144]) {
    const m = new Match(TG); m.start();
    while (m.state !== 'post') m.update(dt);
    const autoEnd = m.events.find((e) => e.text.startsWith('AUTO end')).t;
    const ev = m.events.filter((e) => e.kind === 'period').map((e) => `${e.text.split(' at')[0]}@${e.t?.toFixed(3)}`);
    check(`periods exact at dt=${dt.toFixed(4)}: AUTO 15.000, match 150.000`, Math.abs(autoEnd - 15) < 1e-9 && Math.abs(m.t - 150) < 1e-9, ev.slice(0, 5).join(', '));
  }
  const m = new Match(TG); m.start(); runTo(m, 7.5);
  check('AUTO clock counts down 15 → 0', Math.abs(m.periodLeft() - 7.5) < 1e-9, `at match t=7.5 left=${m.periodLeft()}`);
  runTo(m, 15.0); m.update(1e-6);
  check('TELEOP clock starts at 2:15', m.state === 'teleop' && Math.abs(m.periodLeft() - 135) < 1e-5, `state ${m.state}, left ${m.periodLeft().toFixed(3)}`);
  runTo(m, 120.0);
  check('endgame notice at 30 s left', m.events.some((e) => e.text.startsWith('30 seconds')), `event at t=${m.events.find((e) => e.text.startsWith('30 seconds'))?.t}`);
}
// --- Manual worked examples (Section 4.3)
{
  const m = new Match(TG); m.start(); runTo(m, 15.01); // into TELEOP
  load(m, 'red', 'low', 12); load(m, 'red', 'high', 5); runTo(m, 20);
  let s = m.score('red');
  check('TELEOP 12 low + 5 high = 3 kPa, 3 pts', s.kPa === 3 && s.telePressure === 3, `kPa ${s.kPa.toFixed(3)} pts ${s.telePressure}`);
  const m2 = new Match(TG); m2.start(); runTo(m2, 15.01);
  load(m2, 'red', 'low', 14); load(m2, 'red', 'high', 5); runTo(m2, 20);
  s = m2.score('red');
  // Manual 4.3 prints "32/9 kPa (= 14/9 + 5/3)", but 14/9 + 15/9 = 29/9. Either way: 3 points.
  check('TELEOP 14 low + 5 high = 29/9 kPa (manual typo: 32/9), 3 pts (not 4)', Math.abs(s.kPa - 29 / 9) < 1e-12 && s.telePressure === 3, `kPa ${s.kPa.toFixed(4)} pts ${s.telePressure}`);
  const m3 = new Match(TG); m3.start();
  load(m3, 'red', 'low', 17); runTo(m3, 10);
  s = m3.score('red');
  check('AUTO 17 low = 5⅔ kPa, 5 pts', Math.abs(s.kPa - 17 / 3) < 1e-12 && s.autoPressure === 5, `kPa ${s.kPa.toFixed(4)} auto pts ${s.autoPressure}`);
  runTo(m3, 16); load(m3, 'red', 'low', 3); runTo(m3, 20);
  s = m3.score('red');
  check('…then 3 low in TELEOP → 6 kPa, 6th point (fraction carried)', s.kPa === 6 && s.autoPressure === 5 && s.telePressure === 1 && s.autoPressure + s.telePressure === 6, `kPa ${s.kPa} auto ${s.autoPressure} tele ${s.telePressure} total ${s.total}`);
}
// --- Processing rate and period attribution
{
  const m = new Match(TG); m.start(); runTo(m, 14.0);
  load(m, 'red', 'high', 10); runTo(m, 30);
  const s = m.score('red');
  check('10 high loaded at 14.0 s: 5 count in AUTO (5/s), 5 in TELEOP', s.fuel.auto.high === 5 && s.fuel.teleop.high === 5, `auto ${s.fuel.auto.high}, teleop ${s.fuel.teleop.high}`);
  check('…= 5 + 5/3 kPa → 5 AUTO pts + 1 TELEOP pt', s.autoPressure === 5 && s.telePressure === 1, `kPa ${s.kPa.toFixed(3)} (${s.autoPressure} + ${s.telePressure})`);
  const m2 = new Match(TG); m2.start(); runTo(m2, 149.5);
  load(m2, 'blue', 'low', 5); runTo(m2, 151);
  const b = m2.score('blue');
  check('5 low loaded at T-0.5 s: 2 counted, 3 lost at T=0', b.fuel.teleop.low === 2 && b.lost === 3, `counted ${b.fuel.teleop.low}, lost ${b.lost}`);
  const m3 = new Match(TG); m3.start(); load(m3, 'red', 'high', 200); runTo(m3, 1);
  check('High goal capacity 150: extra fuel falls back', m3.red.overflow.high === 50 && m3.red.queue.high === 145, `overflow ${m3.red.overflow.high}, queued ${m3.red.queue.high}`);
}
// --- Rotors, pilots, reserve (official reserve-gear rule switched on)
{
  const m = new Match(TG, { reserveGear: true }); m.start();
  runTo(m, 5); m.deliverGear('red', 0);            // R1: lift 1.0 + install 1.5 -> 7.5
  runTo(m, 7.9); m.deliverGear('red', 0); m.deliverGear('red', 1); // set 2 at 10.4, 12.9; crank 2.0 -> 14.9
  runTo(m, 30);
  let s = m.score('red');
  check('ROTOR 1 started in AUTO (+60) and ROTOR 2 cranked by 14.9 s (+60)', s.rotorAuto === 120 && s.rotors[0] && s.rotors[1], `auto rotor pts ${s.rotorAuto}; R1 @${m.red.rotorStartedAt[0]?.toFixed(2)} R2 @${m.red.rotorStartedAt[1]?.toFixed(2)}`);
  check('reserve GEAR used at TELEOP start (into set 3), not in AUTO', m.red.reserve === 'used' && s.sets[2] >= 1, `reserve ${m.red.reserve}, sets ${s.sets.join('/')}`);
  const m2 = new Match(TG); m2.start();
  runTo(m2, 5.2); m2.deliverGear('red', 0); runTo(m2, 8.1); m2.deliverGear('red', 0); m2.deliverGear('red', 1); // crank ends 15.1
  runTo(m2, 30);
  s = m2.score('red');
  check('ROTOR 2 finishing at 15.1 s scores TELEOP 40, not 60', s.rotorAuto === 60 && s.rotorTele === 40, `auto ${s.rotorAuto} tele ${s.rotorTele}; R2 @${m2.red.rotorStartedAt[1]?.toFixed(2)}`);
  const m3 = new Match(TG, { reserveGear: true }); m3.start(); runTo(m3, 30);
  s = m3.score('red');
  check('official rule, no GEARS delivered: reserve starts ROTOR 1 in TELEOP (+40)', s.rotorTele === 40 && s.rotorAuto === 0, `R1 @${m3.red.rotorStartedAt[0]?.toFixed(2)} (15 + 1.5 s pilot)`);
  const peg = m3.deliverGear('red', 2); const busy = m3.deliverGear('red', 2);
  check('a lift holds one GEAR until the PILOT takes it', peg.ok && !busy.ok, busy.msg);
}
// --- All rotors, 40 kPa, quals vs playoffs, takeoff, mobility, win RP
{
  for (const mode of ['quals', 'playoffs']) {
    const m = new Match(TG, { mode, pilots: 'instant', reserveGear: true }); m.start();
    runTo(m, 16);
    for (let k = 0; k < 12; k++) { m.deliverGear('red', k % 3); m.update(0.001); }
    load(m, 'red', 'high', 120); // 40 kPa in TELEOP
    const robots = [{ id: 1, name: 'R1', alliance: 'red', corners: [[0, 0], [0, 0], [0, 0], [0, 0]], climb: { touchpad: 0 }, topZ: 59 }];
    runTo(m, 148.9);
    runTo(m, 151, 1 / 60, robots);
    const s = m.score('red');
    const want = mode === 'quals' ? { total: 4 * 40 + 40 + 50, rp: 1 + 1 + 2 } : { total: 4 * 40 + 40 + 50 + 20 + 100, rp: null };
    check(`${mode}: 4 rotors (160) + 40 kPa (40) + takeoff (50)${mode === 'playoffs' ? ' + 20 + 100' : ''} = ${want.total}`, s.total === want.total && s.rp === want.rp, `total ${s.total}, RP ${s.rp}, rotors ${s.rotors.map(Number).join('')}, kPa ${s.kPa}`);
  }
  const m = new Match(TG); m.start();
  const robots = [{ id: 1, name: 'R', alliance: 'red', corners: [[-300, 0], [-262, 0], [-262, 34], [-300, 34]], climb: { touchpad: 1 }, topZ: 59 }];
  runTo(m, 1, 1 / 60, robots);                       // starts behind the line: no points yet
  const before = m.score('red').mobility;
  robots[0].corners = [[-240, 0], [-230, 0], [-240, 5], [-245, 5]]; // bumper now past -230.6
  runTo(m, 3, 1 / 60, robots);
  check('AUTO mobility +5 once, after driving across', before === 0 && m.score('red').mobility === 5, `mobility ${m.score('red').mobility}`);
  const m2 = new Match(TG); m2.start(); runTo(m2, 16, 1 / 60, robots);
  // touchpad: pressed from 149.2 only (0.8 s) -> no points; separate match pressed from 148.5 -> 50
  const tp = (from) => { const mm = new Match(TG); mm.start(); runTo(mm, from); runTo(mm, 151, 1 / 60, [{ ...robots[0], corners: [[0, 0], [0, 0], [0, 0], [0, 0]] }]); return mm.score('red').takeoff; };
  check('TOUCHPAD held 1.5 s at T=0 → 50', tp(148.5) === 50);
  check('TOUCHPAD held 0.8 s at T=0 → 0 (needs 1 s)', tp(149.2) === 0);
  const tpRel = (() => { const mm = new Match(TG); mm.start(); runTo(mm, 140); runTo(mm, 149, 1 / 60, [{ ...robots[0], corners: [[0, 0], [0, 0], [0, 0], [0, 0]] }]); runTo(mm, 151); return mm.score('red').takeoff; })();
  check('TOUCHPAD released before T=0 → 0', tpRel === 0);
  const mob = new Match(TG); mob.start(); runTo(mob, 16); runTo(mob, 20, 1 / 60, robots);
  check('crossing the BASE LINE in TELEOP gives no mobility', mob.score('red').mobility === 0);
  const w = new Match(TG); w.start(); load(w, 'red', 'high', 3); runTo(w, 151);
  check('quals: win = +2 RP, loser 0', w.score('red').rp === 2 && w.score('blue').rp === 0, `red ${w.score('red').total}-${w.score('blue').total}, RP ${w.score('red').rp}/${w.score('blue').rp}`);
  const tie = new Match(TG); tie.start(); runTo(tie, 151);
  check('quals: 0–0 tie = +1 RP each', tie.score('red').rp === 1 && tie.score('blue').rp === 1);
}
// --- End to end with physics: shots, low dump, gear alignment, climbing
{
  const lay = layout();
  const mkSim = () => { const sim = new Sim(colliders(lay)); ropes(lay).forEach((r) => sim.addRope(r));
    const m = new Match(TG);
    for (const al of ['red', 'blue']) {
      sim.addSensor({ kind: 'disc', ...TG[al].highGoal, alliance: al, goal: 'high' });
      sim.addSensor({ kind: 'face', ...TG[al].lowGoal, alliance: al, goal: 'low' });
    }
    sim.onSense = (S) => m.fuelLoaded(S.alliance, S.goal);
    return { sim, m }; };
  let seed = 7; const rnd = () => ((seed = (seed * 16807) % 2147483647) / 2147483647);
  const rows = [];
  // clear lines of fire, fanning out from the goal at ~20 deg from the far-side rail (away from the airship)
  const g0 = TG.red.highGoal.c, dir = [Math.cos(20 * Math.PI / 180), Math.sin(20 * Math.PI / 180)];
  for (const ft of [5, 8, 12, 16, 20, 24]) { const x = g0[0] + dir[0] * ft * 12, y = g0[1] + dir[1] * ft * 12, label = `${ft} ft`;
    for (const spread of [false, true]) {
      const { sim, m } = mkSim(); const r = sim.addRobot({ x, y, th: 0 });
      const a = aimHigh(r, TG.red.highGoal); let shots = 0;
      for (let k = 0; k < 60 * 9; k++) { if (k % 10 === 0 && shots < 30) { const s = fireHigh(r, TG.red.highGoal, rnd, spread); if (s) { sim.addBall(...s.p, ...s.v); shots++; } } sim.step(1 / 60); m.update(1 / 60); }
      rows.push(`${label} (${(a.range / 12).toFixed(1)} ft, ${spread ? 'spread' : 'exact'}): ${m.red.queue.high + m.red.fuel.teleop.high}/${shots}`);
      if (!spread) check(`exact shot from ${label} goes in`, m.red.fuel.teleop.high + m.red.queue.high === shots && shots === 30, rows[rows.length - 1]);
    }
  }
  console.log('   high-goal accuracy: ' + rows.filter((r) => r.includes('spread')).join(' | '));
  // out of range
  { const { sim } = mkSim(); const r = sim.addRobot({ x: 100, y: 0, th: 0 }); check('shooter refuses beyond 25 ft', !aimHigh(r, TG.red.highGoal).ok, aimHigh(r, TG.red.highGoal).reason); }
  // low goal: drive into the boiler face squarely, then dump
  {
    const { sim, m } = mkSim(); const L = TG.red.lowGoal;
    const th = Math.atan2(-L.n[1], -L.n[0]);
    const r = sim.addRobot({ x: L.face[0] + L.n[0] * 40, y: L.face[1] + L.n[1] * 40, th });
    for (let k = 0; k < 90; k++) { r.input.throttle = 0.4; sim.step(1 / 60); }
    let shots = 0; for (let k = 0; k < 60 * 4; k++) { r.input.throttle = 0.15; if (k % 6 === 0 && shots < 30) { const d = dumpLow(r, rnd); sim.addBall(...d.p, ...d.v, r); shots++; } sim.step(1 / 60); m.update(1 / 60); }
    const got = m.red.queue.low + m.red.fuel.teleop.low;
    check('low-goal dump against the boiler face scores', got >= 25, `${got}/${shots} into the low goal`);
    // dumping away from the boiler doesn't count
    const { sim: s2, m: m2 } = mkSim(); const r2 = s2.addRobot({ x: -200, y: 0, th: 0 });
    for (let k = 0; k < 240; k++) { if (k % 6 === 0) { const d = dumpLow(r2, rnd); s2.addBall(...d.p, ...d.v, r2); } s2.step(1 / 60); m2.update(1 / 60); }
    check('dumping in open field scores nothing', m2.red.fuel.teleop.low + m2.red.queue.low === 0);
  }
  // gear: drive straight at the center peg from the red wall, check alignment detection
  {
    const { sim } = mkSim(); const r = sim.addRobot({ x: -326 + 19.75, y: 0, th: 0 });
    for (let k = 0; k < 150; k++) { r.input.throttle = 1; sim.step(1 / 60); }
    const g = gearAlign(r, TG.red.lifts);
    check('robot pressed onto center peg is aligned', g.ok && g.name === 'center', `dist ${g.dist.toFixed(2)} lat ${g.lat.toFixed(2)} angle ${g.angle.toFixed(1)}`);
    const { sim: s3 } = mkSim(); const r3 = s3.addRobot({ x: -326 + 19.75, y: 6, th: 0 });
    for (let k = 0; k < 150; k++) { r3.input.throttle = 1; s3.step(1 / 60); }
    const g3 = gearAlign(r3, TG.red.lifts);
    check('6" off-centre is not aligned', !g3.ok, `lat ${g3.lat.toFixed(2)}`);
  }
  // climbing: drive front into the red center rope, latch, climb, press touchpad
  {
    const { sim, m } = mkSim(); const R = sim.ropes.find((r) => r.anchor[0] < -230);
    const r = sim.addRobot({ x: R.anchor[0] - 80, y: R.anchor[1] + 20, th: 0 });
    for (let k = 0; k < 60; k++) { sim.step(1 / 60); }
    // approach facing the airship so the rope meets the front bumper
    r.x = R.anchor[0] - 50; r.y = R.anchor[1]; r.updateShape();
    let can = false, k = 0; for (; k < 120 && !can; k++) { r.input.throttle = 0.3; sim.step(1 / 60); can = sim.canClimb(r, R); }
    check('front against own rope → can latch', can, `after ${(k / 60).toFixed(2)} s, robot front X ${(r.x + 19.25).toFixed(1)} vs rope ${R.anchor[0].toFixed(1)}`);
    sim.startClimb(r, R, TG.red.touchpads[0].z + 1.0);
    let t = 0; while (r.climb?.phase !== 'hold' && t < 10) { r.input.throttle = 0; sim.step(1 / 60); t += 1 / 60; }
    const top = r.lift + r.height;
    check('climb reaches the touchpad (+1" press)', r.climb?.phase === 'hold' && Math.abs(top - 59) < 0.01, `top ${top.toFixed(2)} in after ${t.toFixed(2)} s; rope pin z ${R.z[R.pinIndex].toFixed(1)}`);
    const cp = r.climberPoint();
    check('robot hangs with climber on the rope line', Math.hypot(cp[0] - R.anchor[0], cp[1] - R.anchor[1]) < 0.01);
    sim.releaseClimb(r); t = 0; while (r.climb && t < 10) { sim.step(1 / 60); t += 1 / 60; }
    check('release lowers the robot back to the carpet', !r.climb && r.lift === 0, `down in ${t.toFixed(2)} s`);
  }
}
console.log(`\n${pass} passed, ${fail} failed`);
{
  const m = new Match(TG); m.start();
  const parked = [{ id: 9, name: 'Parked', alliance: 'blue', corners: [[-60, 0], [-40, 0], [-40, 20], [-60, 20]], climb: null, topZ: 24 }];
  runTo(m, 15.5, 1 / 60, parked);
  console.log((m.score('blue').mobility === 0 ? 'PASS' : 'FAIL') + ' robot that starts past its BASE LINE earns no mobility');
}

// --- Default: no reserve GEAR on the airships (harder game)
{
  const m = new Match(TG); m.start(); runTo(m, 30);
  const s0 = m.score('red');
  console.log((s0.rotorTele === 0 && !s0.rotors[0] && s0.reserve === 'none' ? 'PASS' : 'FAIL') + ' default: no reserve GEAR, so no free ROTOR 1 in TELEOP  — rotor pts ' + s0.rotorTele + ', reserve ' + s0.reserve);
  const run = (n) => { const mm = new Match(TG, { pilots: 'instant' }); mm.start(); runTo(mm, 16); for (let k = 0; k < n; k++) { mm.deliverGear('red', k % 3); mm.update(0.001); } runTo(mm, 20); return mm.score('red').rotors.filter(Boolean).length; };
  const r12 = run(12), r13 = run(13);
  console.log((r12 === 3 && r13 === 4 ? 'PASS' : 'FAIL') + ` default: 12 delivered GEARS start 3 ROTORS, 13 start all 4  — ${r12} / ${r13}`);
}
