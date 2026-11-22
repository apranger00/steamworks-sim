// Lightweight arcade-accurate physics for the STEAMWORKS sim. Engine-agnostic, no dependencies.
// Units: inches, seconds, pounds (mass ratios only). Frame: same as field.js (Z up).
//
//  - Robots: 2D rigid bodies (x, y, heading) with a traction-limited tank-drive controller.
//            Collide with static prisms (SAT), each other, gears and fuel.
//  - FUEL:   3D spheres with gravity, bounce, rolling resistance and sleeping.
//  - GEARS:  flat discs sliding on the carpet (2D), pushed by robots and walls.

export const G = 386.09; // in/s^2

export const ROBOT_DEFAULTS = {
  name: 'Robot',
  alliance: 'red',
  length: 38.5,       // bumper-to-bumper, along heading (32" frame + 2 x 3.25" bumpers)
  width: 34.5,        // 28" frame + bumpers
  height: 24,         // collision height of the body
  bumper: { thick: 3.25, z0: 2, z1: 7 },
  track: 24,          // wheel track width (tank drive)
  wheelR: 3,
  mass: 140,          // lb, incl. battery and bumpers
  maxSpeed: 144,      // in/s  (12 ft/s) free speed
  stallAccel: 600,    // in/s^2 motor acceleration at zero speed; falls linearly to 0 at maxSpeed
  traction: 1.1,      // wheel-carpet friction coefficient (caps drive and pushing force at traction x g)
  sideScrub: 0.85,    // side-slip resistance as a fraction of traction (6WD skids sideways)
  brakeFactor: 0.9,   // kinetic fraction of traction a braked (idle) robot resists with
  maxTurn: 4.7,       // rad/s (~270 deg/s)
};

const FUEL = { r: 2.5, mass: 0.1625, eGround: 0.45, eWall: 0.35, eBall: 0.5, eRobot: 0.25, roll: 14, sleepV: 0.8, sleepT: 0.4 };
const GEAR = { r: 5.5, h: 2, mass: 1.15, mu: 135 /* sliding decel in/s^2 */, spinDecay: 6 };

// ---------------------------------------------------------------------------
// Geometry helpers
function prepPrism(c) {
  const pts = c.pts, n = pts.length, normals = [];
  let minx = Infinity, miny = Infinity, maxx = -Infinity, maxy = -Infinity, cx = 0, cy = 0;
  for (let i = 0; i < n; i++) {
    const a = pts[i], b = pts[(i + 1) % n];
    const dx = b[0] - a[0], dy = b[1] - a[1], L = Math.hypot(dx, dy) || 1;
    normals.push([dy / L, -dx / L]); // outward for CCW
    minx = Math.min(minx, a[0]); maxx = Math.max(maxx, a[0]); miny = Math.min(miny, a[1]); maxy = Math.max(maxy, a[1]);
    cx += a[0] / n; cy += a[1] / n;
  }
  return { name: c.name, pts, normals, z0: c.z0, z1: c.z1, minx, miny, maxx, maxy, cx, cy };
}

// Closest point on convex polygon to (px, py). Returns {inside, qx, qy, d, nx, ny}
// When inside: (nx, ny) is the outward normal of the nearest edge and d its depth.
const _cp = { inside: false, qx: 0, qy: 0, d: 0, nx: 0, ny: 0 };
function polyClosest(P, px, py) {
  const pts = P.pts, nr = P.normals, n = pts.length;
  let inside = true, minDepth = Infinity, mi = 0;
  for (let i = 0; i < n; i++) {
    const s = nr[i][0] * (px - pts[i][0]) + nr[i][1] * (py - pts[i][1]);
    if (s > 0) { inside = false; break; }
    if (-s < minDepth) { minDepth = -s; mi = i; }
  }
  if (inside) {
    _cp.inside = true; _cp.d = minDepth; _cp.nx = nr[mi][0]; _cp.ny = nr[mi][1]; _cp.qx = px; _cp.qy = py;
    return _cp;
  }
  let best = Infinity, bx = 0, by = 0;
  for (let i = 0; i < n; i++) {
    const a = pts[i], b = pts[(i + 1) % n];
    const ex = b[0] - a[0], ey = b[1] - a[1];
    let t = ((px - a[0]) * ex + (py - a[1]) * ey) / (ex * ex + ey * ey);
    t = t < 0 ? 0 : t > 1 ? 1 : t;
    const qx = a[0] + ex * t, qy = a[1] + ey * t;
    const d2 = (px - qx) ** 2 + (py - qy) ** 2;
    if (d2 < best) { best = d2; bx = qx; by = qy; }
  }
  _cp.inside = false; _cp.qx = bx; _cp.qy = by; _cp.d = Math.sqrt(best);
  return _cp;
}

// Sphere (cx,cy,cz,r) vs vertical prism. Returns penetration and normal in out, or false.
function spherePrism(P, cx, cy, cz, r, out) {
  if (cz + r < P.z0 || cz - r > P.z1) return false;
  if (cx + r < P.minx || cx - r > P.maxx || cy + r < P.miny || cy - r > P.maxy) return false;
  const c = polyClosest(P, cx, cy);
  if (c.inside) {
    if (cz >= P.z0 && cz <= P.z1) {
      const side = c.d, top = P.z1 - cz, bot = cz - P.z0;
      if (top <= side && top <= bot) { out.nx = 0; out.ny = 0; out.nz = 1; out.pen = top + r; }
      else if (bot <= side) { out.nx = 0; out.ny = 0; out.nz = -1; out.pen = bot + r; }
      else { out.nx = c.nx; out.ny = c.ny; out.nz = 0; out.pen = side + r; }
      return true;
    }
    const dz = cz > P.z1 ? cz - P.z1 : P.z0 - cz;
    if (dz >= r) return false;
    out.nx = 0; out.ny = 0; out.nz = cz > P.z1 ? 1 : -1; out.pen = r - dz;
    return true;
  }
  const qz = cz < P.z0 ? P.z0 : cz > P.z1 ? P.z1 : cz;
  const dx = cx - c.qx, dy = cy - c.qy, dz = cz - qz;
  const d = Math.sqrt(dx * dx + dy * dy + dz * dz);
  if (d >= r || d < 1e-9) return false;
  out.nx = dx / d; out.ny = dy / d; out.nz = dz / d; out.pen = r - d;
  return true;
}

// Static uniform grid over prisms
class PrismGrid {
  constructor(prisms, cell = 24) {
    this.cell = cell; this.map = new Map(); this.prisms = prisms;
    prisms.forEach((p, i) => {
      for (let gx = Math.floor(p.minx / cell); gx <= Math.floor(p.maxx / cell); gx++)
        for (let gy = Math.floor(p.miny / cell); gy <= Math.floor(p.maxy / cell); gy++) {
          const k = gx * 4096 + gy; let a = this.map.get(k); if (!a) this.map.set(k, a = []); a.push(i);
        }
    });
    this._stamp = new Int32Array(prisms.length); this._tick = 0; this._out = [];
  }
  query(minx, miny, maxx, maxy) {
    const c = this.cell, out = this._out; out.length = 0; this._tick++;
    for (let gx = Math.floor(minx / c); gx <= Math.floor(maxx / c); gx++)
      for (let gy = Math.floor(miny / c); gy <= Math.floor(maxy / c); gy++) {
        const a = this.map.get(gx * 4096 + gy); if (!a) continue;
        for (const i of a) if (this._stamp[i] !== this._tick) { this._stamp[i] = this._tick; out.push(this.prisms[i]); }
      }
    return out;
  }
}

// ---------------------------------------------------------------------------
export class Robot {
  constructor(opts = {}) {
    Object.assign(this, structuredClone(ROBOT_DEFAULTS), opts);
    this.x = opts.x ?? 0; this.y = opts.y ?? 0; this.th = opts.th ?? 0;
    this.vx = 0; this.vy = 0; this.w = 0;
    this.input = { throttle: 0, turn: 0 };
    this.mode = opts.mode || 'drive'; // 'drive' | 'idle'
    this.lift = 0;      // height of the robot's underside above the carpet (climbing)
    this.climb = null;  // { rope, phase: 'attach' | 'up' | 'hold' | 'down', topZ }
    this.climbRate = opts.climbRate ?? 12; // in/s
    this.wheelL = 0; this.wheelR = 0;   // wheel surface speeds (for visuals)
    this.corners = [[0, 0], [0, 0], [0, 0], [0, 0]];
    this.normals = [[0, 0], [0, 0], [0, 0], [0, 0]];
    this.updateShape();
  }
  get I() { return this.mass * (this.length ** 2 + this.width ** 2) / 12; }
  updateShape() {
    const c = Math.cos(this.th), s = Math.sin(this.th), hl = this.length / 2, hw = this.width / 2;
    const loc = [[-hl, -hw], [hl, -hw], [hl, hw], [-hl, hw]];
    for (let i = 0; i < 4; i++) {
      this.corners[i][0] = this.x + c * loc[i][0] - s * loc[i][1];
      this.corners[i][1] = this.y + s * loc[i][0] + c * loc[i][1];
    }
    // outward normals for edges (0-1: -lateral? ) computed generally
    for (let i = 0; i < 4; i++) {
      const a = this.corners[i], b = this.corners[(i + 1) % 4];
      const dx = b[0] - a[0], dy = b[1] - a[1], L = Math.hypot(dx, dy);
      this.normals[i][0] = dy / L; this.normals[i][1] = -dx / L;
    }
    const r = Math.hypot(hl, hw);
    this.minx = this.x - r; this.maxx = this.x + r; this.miny = this.y - r; this.maxy = this.y + r;
  }
  asPrism() {
    // Shares arrays with the robot (valid until it moves)
    return { pts: this.corners, normals: this.normals, z0: this.lift, z1: this.lift + this.height,
             minx: this.minx, miny: this.miny, maxx: this.maxx, maxy: this.maxy };
  }
  climberPoint() {
    const d = this.length / 2 - 3;
    return [this.x + Math.cos(this.th) * d, this.y + Math.sin(this.th) * d];
  }
  // Climbing: move the climber under the rope line, winch up until the top presses the touchpad.
  _climbStep(dt) {
    const c = this.climb; this.vx = this.vy = this.w = 0; this.wheelL = this.wheelR = 0;
    if (c.phase === 'attach') {
      const [cx, cy] = this.climberPoint(), tx = c.rope.anchor[0], ty = c.rope.anchor[1];
      const dx = tx - cx, dy = ty - cy, d = Math.hypot(dx, dy), step = 30 * dt;
      if (d <= step) { this.x += dx; this.y += dy; c.phase = 'up'; }
      else { this.x += dx / d * step; this.y += dy / d * step; }
    } else if (c.phase === 'up') {
      const target = c.topZ - this.height;
      this.lift = Math.min(target, this.lift + this.climbRate * dt);
      if (this.lift >= target) c.phase = 'hold';
    } else if (c.phase === 'down') {
      this.lift = Math.max(0, this.lift - this.climbRate * 1.5 * dt);
      if (this.lift <= 0) this.climb = null;
    }
    this.updateShape();
  }
  // Tank-drive controller: blends velocities toward the commanded state, traction-limited.
  drive(dt) {
    if (this.climb) return this._climbStep(dt);
    const c = Math.cos(this.th), s = Math.sin(this.th);
    let vf = this.vx * c + this.vy * s, vl = -this.vx * s + this.vy * c;
    let tv = 0, tw = 0;
    const tg = this.traction * G;
    if (this.mode === 'drive') {
      tv = this.input.throttle * this.maxSpeed;
      tw = this.input.turn * this.maxTurn;
      // Tank kinematics: wheel speeds can't exceed max; turning while fast costs speed.
      const half = this.track / 2;
      let L = tv - tw * half, R = tv + tw * half;
      const m = Math.max(Math.abs(L), Math.abs(R));
      if (m > this.maxSpeed) { L *= this.maxSpeed / m; R *= this.maxSpeed / m; tv = (L + R) / 2; tw = (R - L) / (2 * half); }
    }
    const clamp = (v, lim) => (v > lim ? lim : v < -lim ? -lim : v);
    const dv = tv - vf;
    // Speeding up (in the direction of travel) is motor-limited and fades near free speed;
    // slowing down or reversing is traction-limited. An idle robot resists with braked wheels.
    let lim;
    if (this.mode !== 'drive') lim = tg * this.brakeFactor;
    else if (vf === 0 || Math.sign(dv) === Math.sign(vf)) lim = Math.min(tg, this.stallAccel * Math.max(0, 1 - Math.abs(vf) / this.maxSpeed));
    else lim = tg;
    vf += clamp(dv, lim * dt);
    vl += clamp(-vl, tg * this.sideScrub * dt);
    const angAccel = (2 * Math.min(tg, this.stallAccel)) / this.track;
    this.w += clamp(tw - this.w, angAccel * dt);
    this.vx = vf * c - vl * s; this.vy = vf * s + vl * c;
    this.wheelL = vf - this.w * this.track / 2; this.wheelR = vf + this.w * this.track / 2;
  }
  // Apply an impulse (jx, jy) at world point (px, py); mass in lb-units.
  applyImpulse(jx, jy, px, py) {
    const rx = px - this.x, ry = py - this.y;
    this.vx += jx / this.mass; this.vy += jy / this.mass;
    this.w += (rx * jy - ry * jx) / this.I;
  }
  pointVel(px, py) {
    const rx = px - this.x, ry = py - this.y;
    return [this.vx - this.w * ry, this.vy + this.w * rx];
  }
}

// SAT between two convex polygons. Returns {nx, ny, pen, px, py} with n pointing from B to A, or null.
function sat(Apts, An, Bpts, Bn) {
  let best = Infinity, bnx = 0, bny = 0, fromA = false;
  const test = (axes, isA) => {
    for (const [ax, ay] of axes) {
      let amin = Infinity, amax = -Infinity, bmin = Infinity, bmax = -Infinity;
      for (const p of Apts) { const d = p[0] * ax + p[1] * ay; if (d < amin) amin = d; if (d > amax) amax = d; }
      for (const p of Bpts) { const d = p[0] * ax + p[1] * ay; if (d < bmin) bmin = d; if (d > bmax) bmax = d; }
      const o = Math.min(amax - bmin, bmax - amin);
      if (o <= 0) return false;
      if (o < best) {
        best = o;
        // orient from B to A
        if (amax - bmin < bmax - amin) { bnx = -ax; bny = -ay; } else { bnx = ax; bny = ay; }
        fromA = isA;
      }
    }
    return true;
  };
  if (!test(An, true) || !test(Bn, false)) return null;
  // Contact point: deepest vertex of the polygon that does NOT own the axis
  let px = 0, py = 0;
  if (fromA) { // axis is a face of A -> deepest vertex of B along +n (into A)
    let m = -Infinity; for (const p of Bpts) { const d = p[0] * bnx + p[1] * bny; if (d > m) { m = d; px = p[0]; py = p[1]; } }
  } else {     // axis is a face of B -> deepest vertex of A along -n (into B)
    let m = Infinity; for (const p of Apts) { const d = p[0] * bnx + p[1] * bny; if (d < m) { m = d; px = p[0]; py = p[1]; } }
  }
  return { nx: bnx, ny: bny, pen: best, px, py };
}

// ---------------------------------------------------------------------------
export class Sim {
  constructor(colliderList, { maxBalls = 600, maxGears = 60 } = {}) {
    this.prisms = colliderList.map(prepPrism);
    this.grid = new PrismGrid(this.prisms);
    this.groundPrisms = this.prisms.filter((p) => p.z0 < GEAR.h);
    this.gearGrid = new PrismGrid(this.groundPrisms);
    this.robots = [];
    // balls (structure of arrays)
    this.maxBalls = maxBalls;
    this.b = { x: new Float64Array(maxBalls), y: new Float64Array(maxBalls), z: new Float64Array(maxBalls),
               vx: new Float64Array(maxBalls), vy: new Float64Array(maxBalls), vz: new Float64Array(maxBalls),
               still: new Float64Array(maxBalls), sleep: new Uint8Array(maxBalls),
               owner: new Int16Array(maxBalls), ownerT: new Float64Array(maxBalls) };
    this.nBalls = 0;
    this.gears = [];
    this.maxGears = maxGears;
    this.ropes = [];
    // Ball sensors (goal openings). onSense(sensor, ballIndex) returns true to consume the ball.
    this.sensors = [];
    this.onSense = null;
    this.time = 0;
    this.stats = { contacts: 0, lost: 0 };
    this._hash = new Map();
    this._tmp = { nx: 0, ny: 0, nz: 0, pen: 0 };
  }

  addRobot(opts) { const r = new Robot(opts); this.robots.push(r); return r; }
  // Sensors: { kind: 'disc', c:[x,y], z, r }  — ball centre drops through z + ballR within r of c
  //          { kind: 'face', face:[x,y], n:[nx,ny], u:[ux,uy], halfW, z0, z1 } — ball reaches the face
  //          plane (moving inward) with its whole body inside the opening
  addSensor(s) { this.sensors.push(s); return s; }
  // Climbing: a robot whose front is against a rope latches on (returns false if it is not in reach).
  canClimb(r, rope) {
    if (r.climb || !rope.deployed) return false;
    const c = Math.cos(-r.th), s = Math.sin(-r.th);
    for (let i = 0; i < rope.n; i++) {
      if (rope.z[i] > r.height) continue;
      const dx = rope.x[i] - r.x, dy = rope.y[i] - r.y;
      const lx = c * dx - s * dy, ly = s * dx + c * dy;
      if (lx > r.length / 2 - 2 && lx < r.length / 2 + 10 && Math.abs(ly) < 12) return true;
    }
    return false;
  }
  startClimb(r, rope, topZ) { r.climb = { rope, phase: 'attach', topZ }; r.vx = r.vy = r.w = 0; }
  releaseClimb(r) { if (r.climb) r.climb.phase = 'down'; }
  // owner: a robot the ball ignores for `ownerTime` s (fuel leaving that robot's mechanism)
  addBall(x, y, z, vx = 0, vy = 0, vz = 0, owner = null, ownerTime = 0.3) {
    if (this.nBalls >= this.maxBalls) return -1;
    const i = this.nBalls++, b = this.b;
    b.x[i] = x; b.y[i] = y; b.z[i] = z; b.vx[i] = vx; b.vy[i] = vy; b.vz[i] = vz; b.still[i] = 0; b.sleep[i] = 0;
    b.owner[i] = owner ? this.robots.indexOf(owner) : -1; b.ownerT[i] = owner ? ownerTime : 0;
    return i;
  }
  removeBall(i) {
    const b = this.b, j = --this.nBalls;
    for (const k of ['x', 'y', 'z', 'vx', 'vy', 'vz', 'still', 'sleep', 'owner', 'ownerT']) b[k][i] = b[k][j];
  }
  clearBalls() { this.nBalls = 0; }
  addGear(x, y, th = 0) {
    if (this.gears.length >= this.maxGears) return null;
    const g = { x, y, th, vx: 0, vy: 0, w: 0 }; this.gears.push(g); return g;
  }

  step(frameDt, substeps = 4) {
    const dt = Math.min(frameDt, 1 / 20) / substeps;
    for (let s = 0; s < substeps; s++) this._sub(dt);
    this.time += frameDt;
  }

  _sub(dt) {
    const robots = this.robots;
    // 1. Robots: controller, integrate, collide
    for (const r of robots) {
      r.drive(dt);
      r.x += r.vx * dt; r.y += r.vy * dt; r.th += r.w * dt;
      r.updateShape();
    }
    for (let it = 0; it < 2; it++) {
      for (const r of robots) this._robotStatic(r);
      for (let i = 0; i < robots.length; i++) for (let j = i + 1; j < robots.length; j++) this._robotRobot(robots[i], robots[j]);
    }
    // 2. Gears
    this._gears(dt);
    // 3. Balls
    this._balls(dt);
    // 4. Ropes (after balls so the ball hash is fresh)
    for (const rope of this.ropes) this._rope(rope, dt);
  }

  // Rope: verlet chain of points. Pinned at the anchor, held within pinR of the touchpad centre at
  // the point that hangs at touchpad height. Robots and fuel push it; it never pushes back on robots.
  addRope(spec, segments = 24) {
    const n = segments + 1, seg = spec.length / segments;
    const rope = { ...spec, n, seg, x: new Float64Array(n), y: new Float64Array(n), z: new Float64Array(n),
                   px: new Float64Array(n), py: new Float64Array(n), pz: new Float64Array(n) };
    rope.pinIndex = Math.round((spec.anchor[2] - spec.pin[2]) / seg);
    rope.deployed = true;
    for (let i = 0; i < n; i++) {
      let z = spec.anchor[2] - i * seg, x = spec.anchor[0], y = spec.anchor[1];
      if (z < spec.radius) { // excess lies on the carpet, trailing outward from the airship
        const over = spec.radius - z; z = spec.radius;
        x += over * (spec.out ? spec.out[0] : 1); y += over * (spec.out ? spec.out[1] : 0);
      }
      rope.x[i] = rope.px[i] = x; rope.y[i] = rope.py[i] = y; rope.z[i] = rope.pz[i] = z;
    }
    this.ropes.push(rope);
    return rope;
  }

  // Stowed: coiled up just under the davit (pilots deploy ROPES for the end game). Deploying lets it fall.
  stowRope(R) {
    R.deployed = false;
    this._coil(R);
  }
  deployRope(R) {
    if (R.deployed) return;
    this._coil(R); // start from the coil with zero velocity, then gravity does the rest
    R.deployed = true;
  }
  _coil(R) {
    const { n, x, y, z, px, py, pz } = R, [ax, ay, az] = R.anchor;
    for (let i = 0; i < n; i++) {
      const a = i * 1.45, rad = i === 0 ? 0 : 2.4;
      x[i] = px[i] = ax + Math.cos(a) * rad; y[i] = py[i] = ay + Math.sin(a) * rad;
      z[i] = pz[i] = az - 1.5 - Math.min(i, 1) * 1.5 - i * 0.22;
    }
  }
  _rope(R, dt) {
    if (!R.deployed) return;
    const { n, x, y, z, px, py, pz, seg, radius: rr } = R;
    const damp = 0.996, g = G * dt * dt;
    // integrate (skip the anchor)
    for (let i = 1; i < n; i++) {
      const vx = (x[i] - px[i]) * damp, vy = (y[i] - py[i]) * damp, vz = (z[i] - pz[i]) * damp;
      px[i] = x[i]; py[i] = y[i]; pz[i] = z[i];
      x[i] += vx; y[i] += vy; z[i] += vz - g;
    }
    x[0] = R.anchor[0]; y[0] = R.anchor[1]; z[0] = R.anchor[2];
    const T = this._tmp, b = this.b, br = FUEL.r;
    for (let iter = 0; iter < 10; iter++) {
      // distance constraints
      for (let i = 0; i < n - 1; i++) {
        const dx = x[i + 1] - x[i], dy = y[i + 1] - y[i], dz = z[i + 1] - z[i];
        const d = Math.sqrt(dx * dx + dy * dy + dz * dz) || 1e-9;
        if (d <= seg) continue; // a rope can go slack but not stretch
        const k = (d - seg) / d, wa = i === 0 ? 0 : 0.5, wb = i === 0 ? 1 : 0.5;
        x[i] += dx * k * wa; y[i] += dy * k * wa; z[i] += dz * k * wa;
        x[i + 1] -= dx * k * wb; y[i + 1] -= dy * k * wb; z[i + 1] -= dz * k * wb;
      }
      // touchpad cut-out keeps the rope near its centre
      const k = R.pinIndex;
      if (k > 0 && k < n) {
        const ox = x[k] - R.pin[0], oy = y[k] - R.pin[1], od = Math.hypot(ox, oy);
        if (od > R.pinR) { x[k] = R.pin[0] + ox / od * R.pinR; y[k] = R.pin[1] + oy / od * R.pinR; }
      }
      // carpet
      for (let i = 1; i < n; i++) if (z[i] < rr) {
        z[i] = rr;
        // friction: the rope drags on the carpet
        px[i] = x[i] - (x[i] - px[i]) * 0.6; py[i] = y[i] - (y[i] - py[i]) * 0.6;
      }
      // a climbing robot holds its rope at the climber, at the robot's top
      for (const rb of this.robots) {
        if (!rb.climb || rb.climb.rope !== R) continue;
        const [hx, hy] = rb.climberPoint(), hz = rb.lift + rb.height;
        const kk = Math.max(R.pinIndex + 1, Math.min(n - 1, Math.round((R.anchor[2] - hz) / seg)));
        x[kk] = hx; y[kk] = hy; z[kk] = Math.max(hz, rr);
      }
      // robots push the rope aside (or drape it over the top)
      for (const rb of this.robots) {
        if (rb.climb && rb.climb.rope === R) continue;
        const P = rb.asPrism();
        for (let i = 1; i < n; i++) {
          if (x[i] + rr < P.minx || x[i] - rr > P.maxx || y[i] + rr < P.miny || y[i] - rr > P.maxy) continue;
          if (!spherePrism(P, x[i], y[i], z[i], rr, T)) continue;
          x[i] += T.nx * T.pen; y[i] += T.ny * T.pen; z[i] += T.nz * T.pen;
        }
      }
    }
    // static field (airship base, barriers, rails...) — once per substep is enough
    for (let i = 1; i < n; i++) {
      for (const P of this.grid.query(x[i] - rr, y[i] - rr, x[i] + rr, y[i] + rr)) {
        if (!spherePrism(P, x[i], y[i], z[i], rr, T)) continue;
        x[i] += T.nx * T.pen; y[i] += T.ny * T.pen; z[i] += T.nz * T.pen;
      }
    }
    // fuel: light mutual push (rope segments weigh about as much as a ball)
    const cell = 2 * br, H = this._hash;
    const key = (gx, gy, gz) => (gx * 73856093) ^ (gy * 19349663) ^ (gz * 83492791);
    const lim = (br + rr) ** 2;
    for (let i = 1; i < n; i++) {
      const gx = Math.floor(x[i] / cell), gy = Math.floor(y[i] / cell), gz = Math.floor(z[i] / cell);
      for (let ox = -1; ox <= 1; ox++) for (let oy = -1; oy <= 1; oy++) for (let oz = -1; oz <= 1; oz++) {
        const a = H.get(key(gx + ox, gy + oy, gz + oz)); if (!a) continue;
        for (const j of a) {
          if (j >= this.nBalls) continue;
          const dx = b.x[j] - x[i], dy = b.y[j] - y[i], dz = b.z[j] - z[i], d2 = dx * dx + dy * dy + dz * dz;
          if (d2 >= lim || d2 < 1e-12) continue;
          const d = Math.sqrt(d2), nx = dx / d, ny = dy / d, nz = dz / d, pen = br + rr - d;
          x[i] -= nx * pen * 0.5; y[i] -= ny * pen * 0.5; z[i] -= nz * pen * 0.5;
          b.x[j] += nx * pen * 0.5; b.y[j] += ny * pen * 0.5; b.z[j] += nz * pen * 0.5;
          const vn = b.vx[j] * nx + b.vy[j] * ny + b.vz[j] * nz;
          if (vn < 0) { // equal-mass, soft hit: the ball loses most of its speed into the rope and carries on
            b.vx[j] -= 0.65 * vn * nx; b.vy[j] -= 0.65 * vn * ny; b.vz[j] -= 0.65 * vn * nz;
            px[i] += 0.35 * vn * nx * dt; py[i] += 0.35 * vn * ny * dt; pz[i] += 0.35 * vn * nz * dt; }
          b.sleep[j] = 0; b.still[j] = 0;
        }
      }
    }
  }

  _robotStatic(r) {
    if (r.climb && r.climb.phase !== 'attach') return;
    const cand = this.grid.query(r.minx, r.miny, r.maxx, r.maxy);
    for (const P of cand) {
      if (P.z0 >= r.height || P.z1 <= 0.5) continue; // only things at robot height (ignores tape-level items)
      if (r.maxx < P.minx || r.minx > P.maxx || r.maxy < P.miny || r.miny > P.maxy) continue;
      const c = sat(r.corners, r.normals, P.pts, P.normals);
      if (!c) continue;
      // push robot out
      r.x += c.nx * c.pen; r.y += c.ny * c.pen;
      r.updateShape();
      // impulse at contact (static body = infinite mass)
      const [vpx, vpy] = r.pointVel(c.px, c.py);
      const vn = vpx * c.nx + vpy * c.ny;
      if (vn < 0) {
        const rx = c.px - r.x, ry = c.py - r.y, rn = rx * c.ny - ry * c.nx;
        const e = 0.05;
        const j = -(1 + e) * vn / (1 / r.mass + (rn * rn) / r.I);
        // friction along the surface
        const tx = -c.ny, ty = c.nx, vt = vpx * tx + vpy * ty;
        const rt = rx * ty - ry * tx;
        let jt = -vt / (1 / r.mass + (rt * rt) / r.I);
        const mu = 0.25; if (Math.abs(jt) > mu * j) jt = Math.sign(jt) * mu * j;
        r.applyImpulse(c.nx * j + tx * jt, c.ny * j + ty * jt, c.px, c.py);
      }
      this.stats.contacts++;
    }
  }

  _robotRobot(a, b) {
    if (a.maxx < b.minx || a.minx > b.maxx || a.maxy < b.miny || a.miny > b.maxy) return;
    const c = sat(a.corners, a.normals, b.corners, b.normals);
    if (!c) return;
    if (a.lift > 0.5 && b.lift > 0.5) return;
    const ma = a.climb ? 1e9 : a.mass, mb = b.climb ? 1e9 : b.mass, tot = ma + mb;
    if (a.lift > b.height || b.lift > a.height) return; // one is hanging above the other
    a.x += c.nx * c.pen * (mb / tot); a.y += c.ny * c.pen * (mb / tot);
    b.x -= c.nx * c.pen * (ma / tot); b.y -= c.ny * c.pen * (ma / tot);
    a.updateShape(); b.updateShape();
    const [ax, ay] = a.pointVel(c.px, c.py), [bx, by] = b.pointVel(c.px, c.py);
    const vn = (ax - bx) * c.nx + (ay - by) * c.ny;
    if (vn >= 0) return;
    const rax = c.px - a.x, ray = c.py - a.y, rbx = c.px - b.x, rby = c.py - b.y;
    const ran = rax * c.ny - ray * c.nx, rbn = rbx * c.ny - rby * c.nx;
    const e = 0.1;
    const ia = a.climb ? 0 : 1, ib = b.climb ? 0 : 1;
    const j = -(1 + e) * vn / (ia / ma + ib / mb + ia * ran * ran / a.I + ib * rbn * rbn / b.I);
    if (ia) a.applyImpulse(c.nx * j, c.ny * j, c.px, c.py);
    if (ib) b.applyImpulse(-c.nx * j, -c.ny * j, c.px, c.py);
  }

  _gears(dt) {
    const gears = this.gears, R = GEAR.r;
    for (const g of gears) {
      const sp = Math.hypot(g.vx, g.vy);
      if (sp > 0) { const ns = Math.max(0, sp - GEAR.mu * dt); g.vx *= ns / sp; g.vy *= ns / sp; }
      g.w *= Math.max(0, 1 - GEAR.spinDecay * dt);
      g.x += g.vx * dt; g.y += g.vy * dt; g.th += g.w * dt;
      // static ground-level prisms
      for (const P of this.gearGrid.query(g.x - R, g.y - R, g.x + R, g.y + R)) {
        if (P.z1 <= 0.3) continue;
        const c = polyClosest(P, g.x, g.y);
        let nx, ny, pen;
        if (c.inside) { nx = c.nx; ny = c.ny; pen = c.d + R; }
        else { if (c.d >= R || c.d < 1e-9) continue; nx = (g.x - c.qx) / c.d; ny = (g.y - c.qy) / c.d; pen = R - c.d; }
        g.x += nx * pen; g.y += ny * pen;
        const vn = g.vx * nx + g.vy * ny;
        if (vn < 0) { g.vx -= 1.2 * vn * nx; g.vy -= 1.2 * vn * ny; }
      }
      // robots
      for (const r of this.robots) {
        if (g.x + R < r.minx || g.x - R > r.maxx || g.y + R < r.miny || g.y - R > r.maxy) continue;
        const c = polyClosest(r.asPrism(), g.x, g.y);
        let nx, ny, pen;
        if (c.inside) { nx = c.nx; ny = c.ny; pen = c.d + R; }
        else { if (c.d >= R || c.d < 1e-9) continue; nx = (g.x - c.qx) / c.d; ny = (g.y - c.qy) / c.d; pen = R - c.d; }
        g.x += nx * pen; g.y += ny * pen;
        const px = g.x - nx * R, py = g.y - ny * R;
        const [rvx, rvy] = r.pointVel(px, py);
        const vn = (g.vx - rvx) * nx + (g.vy - rvy) * ny;
        if (vn < 0) {
          const rx = px - r.x, ry = py - r.y, rn = rx * ny - ry * nx;
          const j = -(1 + 0.2) * vn / (1 / GEAR.mass + 1 / r.mass + rn * rn / r.I);
          g.vx += j * nx / GEAR.mass; g.vy += j * ny / GEAR.mass;
          r.applyImpulse(-j * nx, -j * ny, px, py);
          // a glancing push spins the gear a little
          const tvx = (g.vx - rvx) - vn * nx, tvy = (g.vy - rvy) - vn * ny;
          g.w += ((-ny) * tvx + nx * tvy) * 0.02;
        }
      }
    }
    // gear-gear
    for (let i = 0; i < gears.length; i++) for (let k = i + 1; k < gears.length; k++) {
      const a = gears[i], b = gears[k];
      const dx = a.x - b.x, dy = a.y - b.y, d2 = dx * dx + dy * dy;
      if (d2 >= 4 * R * R || d2 < 1e-9) continue;
      const d = Math.sqrt(d2), nx = dx / d, ny = dy / d, pen = 2 * R - d;
      a.x += nx * pen / 2; a.y += ny * pen / 2; b.x -= nx * pen / 2; b.y -= ny * pen / 2;
      const vn = (a.vx - b.vx) * nx + (a.vy - b.vy) * ny;
      if (vn < 0) { const j = -(1 + 0.3) * vn / 2; a.vx += j * nx; a.vy += j * ny; b.vx -= j * nx; b.vy -= j * ny; }
    }
  }

  _balls(dt) {
    const b = this.b, n = this.nBalls, r = FUEL.r, T = this._tmp;
    const kill = this._kill || (this._kill = new Uint8Array(this.maxBalls)); kill.fill(0, 0, n);
    const wake = (i) => { b.sleep[i] = 0; b.still[i] = 0; };
    for (let i = 0; i < n; i++) {
      if (b.sleep[i]) {
        // resting balls wake when a robot or a moving gear comes near
        let near = false;
        for (const R of this.robots) {
          if (b.x[i] + r >= R.minx && b.x[i] - r <= R.maxx && b.y[i] + r >= R.miny && b.y[i] - r <= R.maxy) { near = true; break; }
        }
        if (!near) for (const g of this.gears) {
          if ((g.vx || g.vy) && Math.abs(b.x[i] - g.x) < GEAR.r + r && Math.abs(b.y[i] - g.y) < GEAR.r + r) { near = true; break; }
        }
        if (!near) continue;
        wake(i);
      }
      b.vz[i] -= G * dt;
      const ox = b.x[i], oy = b.y[i], oz = b.z[i];
      b.x[i] += b.vx[i] * dt; b.y[i] += b.vy[i] * dt; b.z[i] += b.vz[i] * dt;
      if (this.sensors.length && this._sense(i, ox, oy, oz)) { kill[i] = 1; continue; }
      // ground
      if (b.z[i] < r) {
        b.z[i] = r;
        if (b.vz[i] < 0) b.vz[i] = b.vz[i] < -20 ? -b.vz[i] * FUEL.eGround : 0;
      }
      if (b.z[i] <= r + 0.05) { // rolling resistance
        const sp = Math.hypot(b.vx[i], b.vy[i]);
        if (sp > 0) { const ns = Math.max(0, sp - FUEL.roll * dt); b.vx[i] *= ns / sp; b.vy[i] *= ns / sp; }
      }
      // static prisms
      const cand = this.grid.query(b.x[i] - r, b.y[i] - r, b.x[i] + r, b.y[i] + r);
      for (const P of cand) {
        if (!spherePrism(P, b.x[i], b.y[i], b.z[i], r, T)) continue;
        b.x[i] += T.nx * T.pen; b.y[i] += T.ny * T.pen; b.z[i] += T.nz * T.pen;
        const vn = b.vx[i] * T.nx + b.vy[i] * T.ny + b.vz[i] * T.nz;
        if (vn < 0) {
          const k = (1 + FUEL.eWall) * vn;
          b.vx[i] -= k * T.nx; b.vy[i] -= k * T.ny; b.vz[i] -= k * T.nz;
          // surface friction
          b.vx[i] *= 0.97; b.vy[i] *= 0.97;
        }
      }
      // robots (a ball leaving a robot's mechanism ignores that robot briefly)
      if (b.ownerT[i] > 0) b.ownerT[i] -= dt;
      for (let ri = 0; ri < this.robots.length; ri++) {
        const R = this.robots[ri];
        if (b.ownerT[i] > 0 && b.owner[i] === ri) continue;
        if (b.x[i] + r < R.minx || b.x[i] - r > R.maxx || b.y[i] + r < R.miny || b.y[i] - r > R.maxy) continue;
        if (!spherePrism(R.asPrism(), b.x[i], b.y[i], b.z[i], r, T)) continue;
        this._ballRobot(i, R, T);
      }
      // gears (flat discs, z 0..h)
      for (const g of this.gears) {
        const dx = b.x[i] - g.x, dy = b.y[i] - g.y, dh = Math.hypot(dx, dy);
        if (dh > GEAR.r + r || b.z[i] - r > GEAR.h) continue;
        const qd = Math.min(dh, GEAR.r), qx = g.x + (dh > 1e-9 ? dx / dh * qd : 0), qy = g.y + (dh > 1e-9 ? dy / dh * qd : 0);
        const qz = Math.min(Math.max(b.z[i], 0), GEAR.h);
        let nx = b.x[i] - qx, ny = b.y[i] - qy, nz = b.z[i] - qz, d = Math.hypot(nx, ny, nz);
        if (d >= r) continue;
        if (d < 1e-6) { nx = 0; ny = 0; nz = 1; d = 0; } else { nx /= d; ny /= d; nz /= d; }
        const pen = r - d;
        b.x[i] += nx * pen; b.y[i] += ny * pen; b.z[i] += nz * pen;
        const vn = (b.vx[i] - g.vx) * nx + (b.vy[i] - g.vy) * ny + b.vz[i] * nz;
        if (vn < 0) {
          const j = -(1 + FUEL.eWall) * vn / (1 / FUEL.mass + (nx * nx + ny * ny) / GEAR.mass);
          b.vx[i] += j * nx / FUEL.mass; b.vy[i] += j * ny / FUEL.mass; b.vz[i] += j * nz / FUEL.mass;
          g.vx -= j * nx / GEAR.mass; g.vy -= j * ny / GEAR.mass;
        }
      }
    }
    // ball-ball via spatial hash (cell = diameter)
    const cell = 2 * r, H = this._hash; H.clear();
    const key = (gx, gy, gz) => (gx * 73856093) ^ (gy * 19349663) ^ (gz * 83492791);
    for (let i = 0; i < n; i++) {
      const k = key(Math.floor(b.x[i] / cell), Math.floor(b.y[i] / cell), Math.floor(b.z[i] / cell));
      let a = H.get(k); if (!a) H.set(k, a = []); a.push(i);
    }
    const r2 = 4 * r * r;
    for (let i = 0; i < n; i++) {
      const gx = Math.floor(b.x[i] / cell), gy = Math.floor(b.y[i] / cell), gz = Math.floor(b.z[i] / cell);
      for (let ox = -1; ox <= 1; ox++) for (let oy = -1; oy <= 1; oy++) for (let oz = -1; oz <= 1; oz++) {
        const a = H.get(key(gx + ox, gy + oy, gz + oz)); if (!a) continue;
        for (const j of a) {
          if (j <= i) continue;
          if (b.sleep[i] && b.sleep[j]) continue;
          if (kill[i] || kill[j]) continue;
          const dx = b.x[i] - b.x[j], dy = b.y[i] - b.y[j], dz = b.z[i] - b.z[j];
          const d2 = dx * dx + dy * dy + dz * dz;
          if (d2 >= r2 || d2 < 1e-12) continue;
          const d = Math.sqrt(d2), nx = dx / d, ny = dy / d, nz = dz / d, pen = 2 * r - d;
          // a sleeping ball acts as an anchor unless pushed hard
          const wi = b.sleep[i] ? 0 : 1, wj = b.sleep[j] ? 0 : 1, ws = wi + wj;
          b.x[i] += nx * pen * wi / ws; b.y[i] += ny * pen * wi / ws; b.z[i] += nz * pen * wi / ws;
          b.x[j] -= nx * pen * wj / ws; b.y[j] -= ny * pen * wj / ws; b.z[j] -= nz * pen * wj / ws;
          const vn = (b.vx[i] - b.vx[j]) * nx + (b.vy[i] - b.vy[j]) * ny + (b.vz[i] - b.vz[j]) * nz;
          if (vn < 0) {
            if (-vn > 6) { wake(i); wake(j); }
            const jj = -(1 + FUEL.eBall) * vn / 2;
            if (!b.sleep[i]) { b.vx[i] += jj * nx; b.vy[i] += jj * ny; b.vz[i] += jj * nz; }
            if (!b.sleep[j]) { b.vx[j] -= jj * nx; b.vy[j] -= jj * ny; b.vz[j] -= jj * nz; }
          }
        }
      }
    }
    // sleeping and out-of-bounds
    for (let i = this.nBalls - 1; i >= 0; i--) {
      if (kill[i]) { this.removeBall(i); kill[i] = kill[this.nBalls]; continue; }
      if (Math.abs(b.x[i]) > 470 || Math.abs(b.y[i]) > 185 || b.z[i] < -20) { this.removeBall(i); this.stats.lost++; continue; }
      if (b.sleep[i]) continue;
      const sp = Math.abs(b.vx[i]) + Math.abs(b.vy[i]) + Math.abs(b.vz[i]);
      if (sp < FUEL.sleepV && b.z[i] < r + 5.5) { b.still[i] += dt; if (b.still[i] > FUEL.sleepT) { b.sleep[i] = 1; b.vx[i] = b.vy[i] = b.vz[i] = 0; } }
      else b.still[i] = 0;
    }
  }

  _sense(i, ox, oy, oz) {
    const b = this.b, r = FUEL.r;
    for (const S of this.sensors) {
      let hit = false;
      if (S.kind === 'disc') {
        const plane = S.z + r;
        if (oz >= plane && b.z[i] < plane && b.vz[i] < 0) {
          const t = (oz - plane) / (oz - b.z[i]);
          const hx = ox + (b.x[i] - ox) * t, hy = oy + (b.y[i] - oy) * t;
          hit = Math.hypot(hx - S.c[0], hy - S.c[1]) <= S.capture;
        }
      } else if (S.kind === 'face') {
        const d0 = (ox - S.face[0]) * S.n[0] + (oy - S.face[1]) * S.n[1];
        const d1 = (b.x[i] - S.face[0]) * S.n[0] + (b.y[i] - S.face[1]) * S.n[1];
        if (d0 >= r && d1 < r) {
          const t = (d0 - r) / (d0 - d1);
          const hx = ox + (b.x[i] - ox) * t, hy = oy + (b.y[i] - oy) * t, hz = oz + (b.z[i] - oz) * t;
          const lat = (hx - S.face[0]) * S.u[0] + (hy - S.face[1]) * S.u[1];
          hit = Math.abs(lat) <= S.halfW - r + 0.5 && hz >= S.z0 + r - 0.5 && hz <= S.z1 - r + 0.5;
        }
      }
      if (hit && this.onSense && this.onSense(S, i)) return true;
    }
    return false;
  }

  _ballRobot(i, R, T) {
    const b = this.b, r = FUEL.r;
    b.sleep[i] = 0; b.still[i] = 0;
    b.x[i] += T.nx * T.pen; b.y[i] += T.ny * T.pen; b.z[i] += T.nz * T.pen;
    const px = b.x[i] - T.nx * r, py = b.y[i] - T.ny * r;
    const [rvx, rvy] = R.pointVel(px, py);
    const vn = (b.vx[i] - rvx) * T.nx + (b.vy[i] - rvy) * T.ny + b.vz[i] * T.nz;
    if (vn >= 0) return;
    const rx = px - R.x, ry = py - R.y, rn = rx * T.ny - ry * T.nx;
    const hn2 = T.nx * T.nx + T.ny * T.ny;
    const j = -(1 + FUEL.eRobot) * vn / (1 / FUEL.mass + hn2 / R.mass + (rn * rn) / R.I);
    b.vx[i] += j * T.nx / FUEL.mass; b.vy[i] += j * T.ny / FUEL.mass; b.vz[i] += j * T.nz / FUEL.mass;
    R.applyImpulse(-j * T.nx, -j * T.ny, px, py);
  }
}
