// Simplified robot mechanisms for testing scoring (infinite GEARS and FUEL, no intake).
// Engine-agnostic; inches, seconds.
import { G } from './physics.js';

export const MECH = {
  shooter: {
    rate: 6,            // FUEL per second while held
    exitZ: 30,          // launch height above the carpet (top of the robot + turret)
    entryDeg: 55,       // descent angle the solver aims for at the goal opening
    maxSpeed: 480,      // in/s (40 ft/s) launch speed limit
    minRange: 30,       // in, horizontal from turret to goal centre
    maxRange: 300,      // in (25 ft)
    spreadDeg: 0.6,     // 1-sigma aim error, yaw and pitch
    speedErr: 0.012,    // 1-sigma fractional speed error
  },
  dump: { rate: 10, z: 22, speed: 70, width: 14 }, // low-goal hopper dump out of the front
  gear: { maxDist: 4, maxLat: 3.5, maxAngle: 20 }, // peg tip vs gear slot at the front bumper
};

// Velocity that carries a ball from p0 through target, arriving descending at `entryDeg`.
// Derivation: x = vx t, z = vz t - g t^2/2; at T: vx T = D, vz T - g T^2/2 = H,
// slope (vz - g T)/vx = -tan(entry)  =>  g T^2 / 2 = H + D tan(entry).
export function solveShot(p0, target, entryDeg = MECH.shooter.entryDeg) {
  const dx = target[0] - p0[0], dy = target[1] - p0[1], H = target[2] - p0[2];
  const D = Math.hypot(dx, dy);
  const k = H + D * Math.tan(entryDeg * Math.PI / 180);
  if (D < 1e-6 || k <= 0) return null;
  const T = Math.sqrt((2 * k) / G);
  const vh = D / T, vz = (H + 0.5 * G * T * T) / T;
  return { vx: (dx / D) * vh, vy: (dy / D) * vh, vz, speed: Math.hypot(vh, vz), range: D, time: T, pitch: Math.atan2(vz, vh), yaw: Math.atan2(dy, dx) };
}

// Gaussian-ish noise from a uniform rng
const gauss = (rnd) => (rnd() + rnd() + rnd() - 1.5) * 2; // ~N(0,1) within ±3

export function aimHigh(robot, goal) {
  const S = MECH.shooter;
  const p0 = [robot.x, robot.y, S.exitZ + robot.lift];
  // aim at the point where the ball centre clears the rim: one radius above the opening
  const sol = solveShot(p0, [goal.c[0], goal.c[1], goal.z + 2.5]);
  if (!sol) return { ok: false, reason: 'No shot', range: 0 };
  if (sol.range < S.minRange) return { ok: false, reason: 'Too close', range: sol.range, sol };
  if (sol.range > S.maxRange) return { ok: false, reason: 'Out of range', range: sol.range, sol };
  if (sol.speed > S.maxSpeed) return { ok: false, reason: 'Out of range', range: sol.range, sol };
  return { ok: true, range: sol.range, sol, p0 };
}

// Launch one ball: solver velocity with aim/speed noise, plus the robot's own velocity
// (shooting on the move is not compensated — it's a driver skill).
export function fireHigh(robot, goal, rnd, spread = true) {
  const a = aimHigh(robot, goal);
  if (!a.ok) return null;
  const S = MECH.shooter, s = a.sol;
  const e = spread ? 1 : 0, d2r = Math.PI / 180;
  const yaw = s.yaw + e * gauss(rnd) * S.spreadDeg * d2r;
  const pitch = s.pitch + e * gauss(rnd) * S.spreadDeg * d2r;
  const sp = s.speed * (1 + e * gauss(rnd) * S.speedErr);
  const vh = sp * Math.cos(pitch);
  return { p: a.p0, v: [vh * Math.cos(yaw) + robot.vx, vh * Math.sin(yaw) + robot.vy, sp * Math.sin(pitch)] };
}

// Low goal: balls roll out of the front of the robot at bumper height + a bit.
export function dumpLow(robot, rnd) {
  const D = MECH.dump, c = Math.cos(robot.th), s = Math.sin(robot.th);
  // leaves from just inside the front opening; the sim lets it pass through its own robot briefly
  const fwd = robot.length / 2 - 2.6, lat = (rnd() - 0.5) * D.width;
  return {
    p: [robot.x + c * fwd - s * lat, robot.y + s * fwd + c * lat, D.z + robot.lift],
    v: [c * D.speed + robot.vx, s * D.speed + robot.vy, 0],
  };
}

// Is the robot's front gear slot on a lift peg? Returns the best lift (or null) and diagnostics.
export function gearAlign(robot, lifts) {
  const G_ = MECH.gear, c = Math.cos(robot.th), s = Math.sin(robot.th);
  const fx = robot.x + c * robot.length / 2, fy = robot.y + s * robot.length / 2;
  let best = null;
  lifts.forEach((L, i) => {
    // robot must face the lift: forward vs -u
    const cosA = -(c * L.u[0] + s * L.u[1]);
    const angle = Math.acos(Math.max(-1, Math.min(1, cosA))) * 180 / Math.PI;
    const rx = fx - L.tip[0], ry = fy - L.tip[1];
    const dist = rx * L.u[0] + ry * L.u[1];         // >0: front is still in front of the tip
    const lat = Math.abs(-rx * L.u[1] + ry * L.u[0]);
    const ok = angle <= G_.maxAngle && lat <= G_.maxLat && dist <= G_.maxDist && dist >= -2;
    const score = Math.abs(dist) + lat + angle / 5;
    if (!best || score < best.score) best = { index: i, name: L.name, ok, dist, lat, angle, score };
  });
  return best;
}
