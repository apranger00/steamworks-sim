// FIRST STEAMWORKS match timing and scoring (2017 Game Manual, sections 3.4, 3.6–3.8, 3.11.4, 4.1, 4.3,
// Table 4-1). Engine-agnostic: no rendering, no physics. Time is simulation time in seconds.
//
// For the video game both periods are driver-controlled; AUTO still uses AUTO point values.

export const RULES = {
  autoLen: 15,
  teleopLen: 135,           // 2:15
  endgame: 30,              // TOUCHPAD domes only light in the final 30 s
  // Pressure, in ninths of a kPa so fractions stay exact:
  ninths: { auto: { high: 9, low: 3 }, teleop: { high: 3, low: 1 } },
  boilerRate: 5,            // FUEL counted per second per GOAL
  capacity: { high: 150, low: 70 },
  mobility: 5,
  rotorPts: { auto: 60, teleop: 40 },
  takeoff: 50,
  pressureThreshold: 40,    // kPa
  bonus: { pressure: 20, rotors: 100 },  // playoffs only
  rp: { pressure: 1, rotors: 1, win: 2, tie: 1 }, // qualifications only
  gearsNeeded: [1, 2, 4, 6], // pilot-placed GEARS per ROTOR set (regional pre-population: 0,0,1,2)
  touchpadHold: 1.0,        // s pressed before T = 0
  touchpadPress: 0.5,       // in of plate displacement
  // Pilot timing (seconds). 'instant' sets all of these to 0.
  pilot: { lift: 1.0, install: 1.5, reserve: 1.5, crank: 2.0 },
};

const PERIOD_OF = (state) => (state === 'auto' ? 'auto' : 'teleop'); // practice scores at TELEOP values
const fmtT = (t) => t.toFixed(2);

function newAlliance(name, tg, reserveGear) {
  return {
    name, tg,
    ninths: 0, ninthsAuto: 0,
    fuel: { auto: { high: 0, low: 0 }, teleop: { high: 0, low: 0 } },
    queue: { high: 0, low: 0 }, procAcc: { high: 0, low: 0 },
    lost: 0, overflow: { high: 0, low: 0 },
    mobility: 0, mobilityRobots: new Set(), mobilityIneligible: new Set(), mobilitySeen: new Set(),
    rotorPts: { auto: 0, teleop: 0 },
    rotors: [false, false, false, false], rotorStartedAt: [null, null, null, null],
    sets: [0, 0, 0, 0], spareGears: 0, gearsDelivered: 0,
    pegs: tg.lifts.map((l) => ({ name: l.name, state: 'empty', t: 0 })), // empty | loaded | rising
    pilot: { task: null, t: 0, dur: 0 },
    // staged (AUTO: may not be removed, A05) | available | used | none (rule switched off)
    reserve: reserveGear ? 'staged' : 'none',
    touch: tg.touchpads.map(() => ({ pressed: false, held: 0, credited: false })),
    takeoff: 0,
    rp: 0, bonus: { pressure: 0, rotors: 0 }, pressureFlag: false, rotorsFlag: false,
    result: null,
  };
}

export class Match {
  // reserveGear: the official reserve GEAR on each airship. Off by default to make the game harder
  // (without it there is no free ROTOR 1 at the start of TELEOP).
  constructor(targets, { mode = 'quals', pilots = 'realistic', reserveGear = false } = {}) {
    this.targets = targets;
    this.reserveGear = reserveGear;
    this.mode = mode;
    this.pilots = pilots;
    this.reset();
  }

  reset() {
    this.state = 'practice';  // practice | auto | teleop | post
    this.t = 0;               // match time since start
    this.wallStart = null;
    this.events = [];
    this.red = newAlliance('red', this.targets.red, this.reserveGear);
    this.blue = newAlliance('blue', this.targets.blue, this.reserveGear);
    this._endgameAnnounced = false;
    this.log('Practice: scoring counts at TELEOP values; no timer. Start a match to play AUTO + TELEOP.', null);
  }

  start(wallNow = null) {
    const mode = this.mode, pilots = this.pilots;
    this.reset();
    this.mode = mode; this.pilots = pilots;
    this.events = [];
    this.state = 'auto';
    this.wallStart = wallNow;
    this.log(`AUTO start`, null, 'period');
  }

  abort() { this.reset(); }

  get running() { return this.state === 'auto' || this.state === 'teleop'; }
  get scoring() { return this.state !== 'post'; }
  alliance(a) { return a === 'red' ? this.red : this.blue; }

  // Seconds left in the current period (null in practice)
  periodLeft() {
    if (this.state === 'auto') return Math.max(0, RULES.autoLen - this.t);
    if (this.state === 'teleop') return Math.max(0, RULES.autoLen + RULES.teleopLen - this.t);
    if (this.state === 'post') return 0;
    return null;
  }

  log(text, alliance, kind = 'info') {
    this.events.push({ t: this.state === 'practice' ? null : this.t, state: this.state, alliance, kind, text });
    if (this.events.length > 400) this.events.shift();
  }

  // ---------------------------------------------------------------------------
  // Inputs from the simulation
  fuelLoaded(alliance, goal) {
    const A = this.alliance(alliance);
    if (!this.scoring) return false;
    if (A.queue[goal] >= RULES.capacity[goal]) { A.overflow[goal]++; return false; } // falls back on the field
    A.queue[goal]++;
    return true;
  }

  deliverGear(alliance, liftIndex) {
    const A = this.alliance(alliance);
    if (!this.scoring) return { ok: false, msg: 'Match over' };
    const peg = A.pegs[liftIndex];
    if (peg.state !== 'empty') return { ok: false, msg: `${peg.name} lift is busy` };
    peg.state = 'loaded'; peg.t = 0;
    A.gearsDelivered++;
    this.log(`GEAR delivered to ${peg.name} lift`, alliance, 'gear');
    return { ok: true, msg: `GEAR on ${peg.name} lift` };
  }

  // robots: [{ alliance, corners:[[x,y]x4], climb: { touchpad: index } | null, topZ }]
  update(dt, robots = [], wallNow = null) {
    if (this.state === 'post') return;
    // Period clock. Split dt at period boundaries so counting is attributed to the right period.
    let remaining = dt;
    while (remaining > 0) {
      let step = remaining;
      if (this.state === 'auto') step = Math.min(step, RULES.autoLen - this.t);
      if (this.state === 'teleop') step = Math.min(step, RULES.autoLen + RULES.teleopLen - this.t);
      if (step > 0) this._advance(step, robots);
      remaining -= step;
      if (this.state !== 'practice') this.t += step;
      if (this.state === 'auto' && this.t >= RULES.autoLen - 1e-9) {
        this.t = RULES.autoLen;
        this._endAuto(wallNow);
      } else if (this.state === 'teleop' && this.t >= RULES.autoLen + RULES.teleopLen - 1e-9) {
        this.t = RULES.autoLen + RULES.teleopLen;
        this._endTeleop(robots, wallNow);
        return;
      }
      if (step <= 0 && remaining > 0 && this.state === 'practice') { this._advance(remaining, robots); break; }
    }
    if (this.state === 'teleop' && !this._endgameAnnounced && this.periodLeft() <= RULES.endgame) {
      this._endgameAnnounced = true; this.log('30 seconds left: TOUCHPADS now light when pressed', null, 'period');
    }
  }

  _advance(dt, robots) {
    for (const A of [this.red, this.blue]) {
      this._boiler(A, 'high', dt);
      this._boiler(A, 'low', dt);
      this._pilot(A, dt);
    }
    // AUTO mobility: a BUMPER breaks the BASE LINE plane by T=0
    if (this.state === 'auto') {
      for (const r of robots) {
        const A = this.alliance(r.alliance);
        if (A.mobilityRobots.has(r.id) || A.mobilityIneligible.has(r.id)) continue;
        const s = A.tg.side, line = A.tg.baseLineX;
        const past = r.corners.some((c) => (s < 0 ? c[0] > line : c[0] < line));
        // a robot must start the match behind its BASE LINE (it starts against the alliance wall):
        // the first time a robot is seen in AUTO decides whether it can earn mobility
        if (!A.mobilitySeen.has(r.id)) {
          A.mobilitySeen.add(r.id);
          if (past) { A.mobilityIneligible.add(r.id); this.log(`${r.name} started past the BASE LINE: not eligible for AUTO mobility`, r.alliance, 'info'); continue; }
        }
        if (past) {
          A.mobilityRobots.add(r.id); A.mobility += RULES.mobility;
          this.log(`AUTO mobility: ${r.name} broke the BASE LINE (+${RULES.mobility})`, r.alliance, 'score');
        }
      }
    }
    // TOUCHPADS
    for (const A of [this.red, this.blue]) {
      A.touch.forEach((tp, i) => {
        const tz = A.tg.touchpads[i].z + RULES.touchpadPress;
        const pressed = robots.some((r) => r.alliance === A.name && r.climb && r.climb.touchpad === i && r.topZ >= tz - 1e-6);
        if (pressed && !tp.pressed) this.log(`TOUCHPAD ${i + 1} pressed`, A.name, 'climb');
        if (!pressed && tp.pressed) this.log(`TOUCHPAD ${i + 1} released after ${tp.held.toFixed(1)} s`, A.name, 'climb');
        tp.pressed = pressed;
        tp.held = pressed ? tp.held + dt : 0;
      });
    }
  }

  _boiler(A, goal, dt) {
    if (A.queue[goal] <= 0) { A.procAcc[goal] = 0; return; }
    A.procAcc[goal] += dt;
    const per = 1 / RULES.boilerRate;
    while (A.queue[goal] > 0 && A.procAcc[goal] >= per - 1e-9) {
      A.procAcc[goal] -= per;
      A.queue[goal]--;
      const period = PERIOD_OF(this.state);
      A.fuel[period][goal]++;
      const before = Math.floor(A.ninths / 9);
      A.ninths += RULES.ninths[period][goal];
      if (period === 'auto') A.ninthsAuto += RULES.ninths[period][goal];
      const after = Math.floor(A.ninths / 9);
      if (after > before) this.log(`Pressure ${after} kPa (+${after - before} pt) — ${goal === 'high' ? 'High' : 'Low'} Efficiency FUEL counted in ${period.toUpperCase()}`, A.name, 'score');
      if (!A.pressureFlag && A.ninths >= RULES.pressureThreshold * 9) {
        A.pressureFlag = true;
        if (this.mode === 'playoffs') { A.bonus.pressure = RULES.bonus.pressure; this.log(`40 kPa reached: +${RULES.bonus.pressure} (playoff bonus)`, A.name, 'score'); }
        else { this.log('40 kPa reached: +1 Ranking Point', A.name, 'score'); }
      }
    }
  }

  // PILOT model: one task at a time. Priority: crank a completed set, take a GEAR off a lift,
  // then (TELEOP only) use the reserve GEAR. GEARS fill the lowest unfinished set (order of
  // placement within a set does not matter; ROTORS only start in order).
  _pilot(A, dt) {
    const P = A.pilot, T = this.pilots === 'instant' ? { lift: 0, install: 0, reserve: 0, crank: 0 } : RULES.pilot;
    // carriages that were raised come back down
    for (const peg of A.pegs) if (peg.state === 'rising') { peg.t += dt; if (peg.t >= Math.max(T.lift, 0)) { peg.state = 'empty'; peg.t = 0; } }
    let budget = dt, guard = 0;
    while (guard++ < 50) {
      if (!P.task) {
        P.task = this._nextTask(A); P.t = 0;
        if (!P.task) return;
        P.dur = P.task.kind === 'crank' ? T.crank : P.task.kind === 'gear' ? T.lift + T.install : T.reserve;
        if (P.task.kind === 'gear') { const peg = A.pegs[P.task.peg]; peg.state = 'rising'; peg.t = 0; }
        if (P.task.kind === 'reserve') A.reserve = 'used';
        if (P.task.kind === 'crank') this.log(`PILOT cranking ROTOR ${P.task.rotor + 1}`, A.name, 'gear');
      }
      const need = P.dur - P.t;
      if (budget < need - 1e-9) { P.t += budget; return; }
      budget -= Math.max(0, need);
      this._finishTask(A, P.task);
      P.task = null;
    }
  }

  _nextTask(A) {
    const k = A.rotors.indexOf(false);
    if (k > 0 && A.rotors[k - 1] && A.sets[k] >= RULES.gearsNeeded[k]) return { kind: 'crank', rotor: k };
    const peg = A.pegs.findIndex((p) => p.state === 'loaded');
    if (peg >= 0) return { kind: 'gear', peg };
    if (A.reserve === 'available' && A.sets.some((n, i) => n < RULES.gearsNeeded[i])) return { kind: 'reserve' };
    return null;
  }

  _finishTask(A, task) {
    if (task.kind === 'crank') return this._startRotor(A, task.rotor);
    const k = A.sets.findIndex((n, i) => n < RULES.gearsNeeded[i]);
    const what = task.kind === 'reserve' ? 'Reserve GEAR' : 'GEAR';
    if (k < 0) { A.spareGears++; this.log(`${what} not needed: all GEAR sets complete`, A.name, 'gear'); return; }
    A.sets[k]++;
    this.log(`${what} installed in set ${k + 1} (${A.sets[k]}/${RULES.gearsNeeded[k]})`, A.name, 'gear');
    if (k === 0 && A.sets[0] >= RULES.gearsNeeded[0]) this._startRotor(A, 0); // ROTOR 1 starts when placed
  }

  _startRotor(A, k) {
    if (A.rotors[k] || !this.scoring) return;
    A.rotors[k] = true; A.rotorStartedAt[k] = this.t;
    const period = PERIOD_OF(this.state);
    // Points are for each ROTOR turning by the period's T=0 and not previously scored. ROTORS never
    // stop, so crediting at start time with the current period's value is equivalent.
    A.rotorPts[period] += RULES.rotorPts[period];
    this.log(`ROTOR ${k + 1} turning (+${RULES.rotorPts[period]}, ${period.toUpperCase()})`, A.name, 'score');
    if (A.rotors.every(Boolean) && !A.rotorsFlag) {
      A.rotorsFlag = true;
      if (this.mode === 'playoffs') { A.bonus.rotors = RULES.bonus.rotors; this.log(`All four ROTORS: +${RULES.bonus.rotors} (playoff bonus)`, A.name, 'score'); }
      else this.log('All four ROTORS: +1 Ranking Point', A.name, 'score');
    }
  }

  _endAuto(wallNow) {
    const wall = wallNow != null && this.wallStart != null ? ` (wall clock ${((wallNow - this.wallStart) / 1000).toFixed(2)} s)` : '';
    this.log(`AUTO end at ${fmtT(this.t)} s${wall}`, null, 'period');
    for (const A of [this.red, this.blue]) {
      const q = A.queue.high + A.queue.low;
      if (q) this.log(`${q} FUEL still in the BOILER at end of AUTO will count at TELEOP values`, A.name, 'info');
      if (A.reserve === 'staged') A.reserve = 'available'; // A05 only applies during AUTO
    }
    this.state = 'teleop';
    this.log('TELEOP start', null, 'period');
  }

  _endTeleop(robots, wallNow) {
    const wall = wallNow != null && this.wallStart != null ? ` (wall clock ${((wallNow - this.wallStart) / 1000).toFixed(2)} s)` : '';
    this.log(`TELEOP end — match over at ${fmtT(this.t)} s${wall}`, null, 'period');
    for (const A of [this.red, this.blue]) {
      const q = A.queue.high + A.queue.low;
      if (q) { A.lost += q; A.queue.high = A.queue.low = 0; this.log(`${q} FUEL not counted before T=0 (does not score)`, A.name, 'info'); }
      A.touch.forEach((tp, i) => {
        if (tp.pressed && tp.held >= RULES.touchpadHold - 1e-9) {
          tp.credited = true; A.takeoff += RULES.takeoff;
          this.log(`Ready for takeoff: TOUCHPAD ${i + 1} pressed at T=0 (+${RULES.takeoff})`, A.name, 'score');
        } else if (tp.pressed) {
          this.log(`TOUCHPAD ${i + 1} pressed only ${tp.held.toFixed(2)} s at T=0 (needs ${RULES.touchpadHold} s): no points`, A.name, 'info');
        }
      });
    }
    this.state = 'post';
    const r = this.score('red'), b = this.score('blue');
    for (const [A, mine, theirs] of [[this.red, r.total, b.total], [this.blue, b.total, r.total]]) {
      A.result = mine > theirs ? 'win' : mine === theirs ? 'tie' : 'loss';
    }
    this.log(`Final: Red ${r.total} – Blue ${b.total}`, null, 'period');
  }

  // ---------------------------------------------------------------------------
  score(alliance) {
    const A = this.alliance(alliance);
    const autoPressure = Math.floor(A.ninthsAuto / 9);
    const totalPressure = Math.floor(A.ninths / 9);
    const auto = A.mobility + autoPressure + A.rotorPts.auto;
    const teleop = (totalPressure - autoPressure) + A.rotorPts.teleop + A.takeoff + A.bonus.pressure + A.bonus.rotors;
    let rp = null;
    if (this.mode === 'quals') {
      rp = (A.pressureFlag ? RULES.rp.pressure : 0) + (A.rotorsFlag ? RULES.rp.rotors : 0);
      if (A.result === 'win') rp += RULES.rp.win;
      if (A.result === 'tie') rp += RULES.rp.tie;
    }
    return {
      total: auto + teleop, auto, teleop,
      mobility: A.mobility, autoPressure, telePressure: totalPressure - autoPressure,
      kPa: A.ninths / 9, rotorAuto: A.rotorPts.auto, rotorTele: A.rotorPts.teleop,
      takeoff: A.takeoff, bonus: A.bonus.pressure + A.bonus.rotors, rp,
      rotors: A.rotors.slice(), sets: A.sets.slice(), queue: { ...A.queue }, fuel: structuredClone(A.fuel),
      pegs: A.pegs.map((p) => p.state), reserve: A.reserve, lost: A.lost, result: A.result,
    };
  }
}
