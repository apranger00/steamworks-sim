// FRC 2017 STEAMWORKS field blockout — engine-agnostic layout data.
//
// Frame (inches): origin = field center on the carpet.
//   +X toward the BLUE alliance wall, -X toward RED.
//   +Y toward the scoring-table side (return loading stations, 4 hoppers).
//   -Y is the far side (boilers, 6 hopper containers incl. the center pair).
//   +Z up.
// The field is MIRROR-symmetric across X = 0 (both boilers are on -Y).
//
// Positions are measured from the official 2017 field CAD (FRC_Field_2017.SLDPRT)
// and cross-checked against the 2017 Game Manual and field drawings.

export const IN = 0.0254; // metres per inch

export const DIM = {
  carpet: { x: 936, y: 360, yCenter: 0.2 },
  field: { halfX: 326, halfY: 162.4 },
  tapeW: 2,
  startingLine: 356.9,      // centre of 2" white line, from field centre
  baseLine: 231.6,          // centre of 2" green line (93.3" from wall)
  launchpadLine: 140.0,     // centre of 2" alliance line (185.3" from wall)
  allianceWall: { y0: -124, y1: 124, plate: 36, top: 78.4, thick: 2,
                  posts: [-124, -52, 20, 50, 124] },
  overflow: { y0: 20, y1: 50, open: [22.5, 47.5], openZ: [25, 32.5] },
  // Return loading station: diagonal wall in each +Y corner.
  loadStation: { a: [326, 124.5], b: [254, 162.4], h: 78.4,
                 openings: [[1.375, 25.375], [50.125, 74.125]], openZ: [25, 32.5] },
  sideRail: { h: 20.2, scoringHalfLen: 252.6, farHalfLen: 288.7 },
  boiler: { cx: 322, cy: -160.5, size: 42, bodyH: 54, roofH: 72, roofRise: 18, frameH: 119, frameW: 58, frameD: 54.75,
            goalOffset: 3.5, stackR: 9, funnelR: 11.5, goalZ: 97,
            lowGoal: { w: 25, h: 8.75, z: 18 } },
  airship: { cx: 176.7, deckZ: 36.5, deckFlat: 70.5, railFlat: 101, railZ: 79,
             tankR: 11.75, tankTop: 108.5, rotorR: 53, rotorTop: 133.8,
             centralRotor: [138, 158, 22], touchpadZ: 58, davitR: 62.6 },
  hopper: { yIn: 166, yOut: 188.3, zBottom: 5, zTop: 46.2, floorZ: 24.6, slope: 2.2,
            containers: {
              scoring: [[-160.8, -133.8], [-104.2, -77.2], [77.2, 104.2], [133.8, 160.8]],
              far: [[-247.8, -220.8], [-191.2, -164.2], [-41.8, -14.8], [14.8, 41.8],
                    [164.2, 191.2], [220.8, 247.8]],
            } },
  fuelR: 2.5,
  gear: { od: 11, thick: 2, teeth: 10 },
};

const D2R = Math.PI / 180;
const hexR = (flat) => flat / Math.sqrt(3); // circumradius from flat-to-flat

// Regular polygon ring (2D points) — vertices at angle0 + k*360/n.
function ring(cx, cy, r, n, angle0) {
  const pts = [];
  for (let k = 0; k < n; k++) {
    const a = (angle0 + (k * 360) / n) * D2R;
    pts.push([cx + r * Math.cos(a), cy + r * Math.sin(a)]);
  }
  return pts;
}

function polar(cx, cy, r, deg) {
  return [cx + r * Math.cos(deg * D2R), cy + r * Math.sin(deg * D2R)];
}

// Item helpers -------------------------------------------------------------
// box: centre c [x,y,z], size [sx,sy,sz] in the item's local frame, rotZ degrees.
const box = (name, mat, c, size, rotZ = 0, group = 'field') => ({ type: 'box', name, mat, c, size, rotZ, group });
// segment box: footprint from a to b (2D), width w, z0..z1.
function segBox(name, mat, a, b, w, z0, z1, group = 'field') {
  const dx = b[0] - a[0], dy = b[1] - a[1];
  const len = Math.hypot(dx, dy);
  return box(name, mat, [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2, (z0 + z1) / 2],
    [len, w, z1 - z0], Math.atan2(dy, dx) / D2R, group);
}
// loft: ring0 at z0 to ring1 at z1 (same vertex count), optional caps.
// a flat convex polygon (3 or 4 coplanar 3D points) thickened by t along its normal: tilted panels (boiler nets)
const slab = (name, mat, pts, t, group = 'field') => ({ type: 'slab', name, mat, pts, t, group });
const loft = (name, mat, ring0, z0, ring1, z1, caps = [true, true], group = 'field') =>
  ({ type: 'loft', name, mat, ring0, z0, ring1, z1, caps, group });
// cylinder / cone between two 3D points.
const cyl = (name, mat, p0, p1, r0, r1 = r0, seg = 20, open = false, group = 'field') =>
  ({ type: 'cyl', name, mat, p0, p1, r0, r1, seg, open, group });
const tape = (name, color, a, b, w = DIM.tapeW) => ({ type: 'tape', name, color, a, b, w, group: 'tape' });

// Wall panel along a 2D segment with one rectangular opening (s0..s1 along the
// segment from a, z range oz0..oz1). Returns 4 boxes around the hole.
function panelWithHole(name, mat, a, b, thick, z0, z1, s0, s1, oz0, oz1) {
  const dx = b[0] - a[0], dy = b[1] - a[1];
  const L = Math.hypot(dx, dy), ux = dx / L, uy = dy / L;
  const at = (s) => [a[0] + ux * s, a[1] + uy * s];
  return [
    segBox(name, mat, a, at(s0), thick, z0, z1),
    segBox(name, mat, at(s1), b, thick, z0, z1),
    segBox(name, mat, at(s0), at(s1), thick, z0, oz0),
    segBox(name, mat, at(s0), at(s1), thick, oz1, z1),
  ]; // the opening itself is open (the game puts a chute behind it)
}

// ---------------------------------------------------------------------------
export function layout() {
  const items = [];
  const push = (...xs) => items.push(...xs.flat());
  const fuel = [];   // ball centres
  const gears = [];  // {c, rotZ, flat:true}

  // Carpet --------------------------------------------------------------
  push(box('Carpet', 'carpet', [0, DIM.carpet.yCenter, -0.25], [DIM.carpet.x, DIM.carpet.y, 0.5]));

  // Side guardrails -----------------------------------------------------
  const R = DIM.sideRail;
  for (const [side, half] of [[1, R.scoringHalfLen], [-1, R.farHalfLen]]) {
    const y = side * (DIM.field.halfY + 0.5);
    const nm = side > 0 ? 'Guardrail (scoring-table side)' : 'Guardrail (far side)';
    push(box(nm, 'poly', [0, y, R.h / 2], [2 * half, 0.25, R.h]));
    push(box(nm + ' rail', 'alu', [0, y + side * 0.75, R.h - 0.75], [2 * half, 1.5, 1.5]));
    push(box(nm + ' rail', 'alu', [0, y + side * 0.75, 0.75], [2 * half, 1.5, 1.5]));
    for (let x = -half; x <= half + 0.01; x += (2 * half) / Math.round(2 * half / 72)) {
      push(box(nm + ' post', 'alu', [x, y + side * 0.75, R.h / 2], [1.5, 1.5, R.h]));
    }
  }

  // Per end -----------------------------------------------------------------
  for (const s of [-1, 1]) {
    const own = s < 0 ? 'red' : 'blue';
    const opp = s < 0 ? 'blue' : 'red';
    const OWN = own.toUpperCase();
    const X = (x) => s * x; // mirror helper: x given as distance toward this end

    // Tape ----------------------------------------------------------------
    const cy0 = -179.8, cy1 = 180.2;
    push(tape(`${OWN} STARTING LINE`, 'white', [X(DIM.startingLine), cy0], [X(DIM.startingLine), cy1]));
    push(tape(`${OWN} BASE LINE`, 'green', [X(DIM.baseLine), -DIM.field.halfY], [X(DIM.baseLine), DIM.field.halfY]));
    push(tape(`${OWN} LAUNCHPAD LINE`, own, [X(DIM.launchpadLine), -DIM.field.halfY], [X(DIM.launchpadLine), DIM.field.halfY]));
    push(tape(`${OWN} KEY`, own, [X(325.0), -43.7], [X(202.5), -161.4]));
    push(tape(`${opp.toUpperCase()} RETRIEVAL ZONE`, opp, [X(326.0), 79.9], [X(163.0), 161.6]));
    // Alliance station (outside the wall)
    push(tape(`${OWN} ALLIANCE STATION`, own, [X(350.2), 123.3], [X(440.2), 123.3]));
    push(tape(`${OWN} ALLIANCE STATION`, own, [X(350.2), -124.7], [X(440.2), -124.7]));
    push(tape(`${OWN} ALLIANCE STATION`, own, [X(439.2), -125.7], [X(439.2), 124.3]));

    // Alliance wall ---------------------------------------------------------
    const W = DIM.allianceWall, O = DIM.overflow;
    const wx = X(DIM.field.halfX + W.thick / 2);
    const seg = (y0, y1) => {
      push(box(`${OWN} ALLIANCE WALL (diamond plate)`, 'steel', [wx, (y0 + y1) / 2, W.plate / 2], [W.thick, y1 - y0, W.plate]));
      push(box(`${OWN} PLAYER STATION window`, 'poly', [wx, (y0 + y1) / 2, (W.plate + W.top) / 2], [0.375, y1 - y0, W.top - W.plate]));
    };
    seg(W.y0, O.y0);
    seg(O.y1, W.y1);
    push(panelWithHole(`${OWN} OVERFLOW LOADING STATION`, 'poly',
      [wx, O.y0], [wx, O.y1], 0.5, 0, W.top, O.open[0] - O.y0, O.open[1] - O.y0, O.openZ[0], O.openZ[1]));
    for (const py of W.posts) push(box(`${OWN} ALLIANCE WALL post`, 'alu', [wx, py, W.top / 2], [2, 2, W.top]));
    push(box(`${OWN} ALLIANCE WALL top rail`, 'alu', [wx, 0, W.top - 0.75], [2, W.y1 - W.y0, 1.5]));
    // Driver-station shelves (outside the field)
    for (const [y0, y1] of [[-124, -52], [-52, 20], [50, 124]]) {
      push(box(`${OWN} PLAYER STATION shelf`, 'alu', [X(DIM.field.halfX + 2 + 6), (y0 + y1) / 2, 36.5], [12, y1 - y0 - 3, 1]));
    }

    // Return loading station (diagonal, +Y corner) — serves the OPPOSITE alliance
    const L = DIM.loadStation;
    const la = [X(L.a[0]), L.a[1]], lb = [X(L.b[0]), L.b[1]];
    const llen = Math.hypot(lb[0] - la[0], lb[1] - la[1]);
    const pad = (llen - 75.5) / 2;
    const dir = [(lb[0] - la[0]) / llen, (lb[1] - la[1]) / llen];
    const at = (t) => [la[0] + dir[0] * t, la[1] + dir[1] * t];
    // outward normal (away from field centre)
    let nrm = [-dir[1], dir[0]];
    const mid = at(llen / 2);
    if (nrm[0] * mid[0] + nrm[1] * mid[1] < 0) nrm = [-nrm[0], -nrm[1]];
    const nm = `${opp.toUpperCase()} RETURN LOADING STATION`;
    const o0 = L.openings[0], o1 = L.openings[1];
    push(segBox(nm, 'poly', at(0), at(pad + o0[0]), 0.5, 0, L.h));
    push(segBox(nm, 'poly', at(pad + o0[1]), at(pad + o1[0]), 0.5, 0, L.h));
    push(segBox(nm, 'poly', at(pad + o1[1]), at(llen), 0.5, 0, L.h));
    for (const o of [o0, o1]) {
      push(segBox(nm, 'poly', at(pad + o[0]), at(pad + o[1]), 0.5, 0, L.openZ[0]));
      push(segBox(nm, 'poly', at(pad + o[0]), at(pad + o[1]), 0.5, L.openZ[1], L.h));
      // the opening is open; the Unity game builds the chute behind it (LoadingStations)
    }
    push(segBox(nm + ' frame', 'alu', at(0), at(llen), 1.5, L.h - 1.5, L.h));
    for (const t of [0, llen]) push(box(nm + ' post', 'alu', [at(t)[0], at(t)[1], L.h / 2], [2, 2, L.h]));

    // Loading lane staging: outside the field, between the return loading
    // station and the starting line (X beyond the wall, Y > 124).
    for (const gy of [135, 150, 165]) {
      for (let k = 0; k < 6; k++) gears.push({ c: [X(332), gy, 1 + 2 * k], flat: true, rotZ: 18 * k });
    }
    {
      const p = [X(347), 150];
      push(box(`${opp.toUpperCase()} LOADING LANE fuel bin`, 'bin', [p[0], p[1], 6.5], [18, 33.5, 13], 0, 'pieces'));
      let n = 0;
      for (let layer = 0; n < 20; layer++) {
        for (let i = 0; i < 3 && n < 20; i++) for (let j = 0; j < 6 && n < 20; j++) {
          fuel.push([p[0] - 5 + i * 5, p[1] - 12.5 + j * 5, 3 + layer * 4.6]);
          n++;
        }
      }
    }

    // Boiler (-Y corner) -----------------------------------------------------
    const B = DIM.boiler;
    const bc = [X(B.cx), B.cy];
    const n2 = [-s / Math.SQRT2, 1 / Math.SQRT2]; // field-facing normal
    const half = B.size / 2, rDiag = half * Math.SQRT2;
    const sq = (r) => ring(bc[0], bc[1], r, 4, 0); // vertices on axes => square rotated 45 deg
    push(loft(`${OWN} BOILER`, own + 'Dark', sq(rDiag), 0, sq(rDiag), B.bodyH));
    // boiler-local frame (manual figure 3-33): u across the field face, d toward the field (face at d = +half)
    const u2 = [-n2[1], n2[0]];
    const P = (u, d, z) => [bc[0] + u2[0] * u + n2[0] * d, bc[1] + u2[1] * u + n2[1] * d, z];
    const P2 = (u, d) => P(u, d, 0).slice(0, 2);
    const ccw = (r) => { let a = 0; for (let k = 0; k < r.length; k++) { const p = r[k], q = r[(k + 1) % r.length]; a += p[0] * q[1] - q[0] * p[1]; } return a > 0; };
    // ROOF: the tapered top the net hangs from, sloping down toward the field (front edge at the body top, 54 in;
    // back edge 18 in higher), so misses roll forward and drop off the front onto the field
    {
      let r0 = [P2(-half, -half), P2(half, -half), P2(half, half), P2(-half, half)];
      let r1 = [P2(-half, -half), P2(half, -half), P2(half, -half + 0.5), P2(-half, -half + 0.5)];
      if (!ccw(r0)) { r0 = r0.reverse(); r1 = r1.reverse(); }
      push(loft(`${OWN} BOILER roof`, own + 'Dark', r0, B.bodyH, r1, B.bodyH + B.roofRise));
    }
    const gc = [bc[0] + n2[0] * B.goalOffset, bc[1] + n2[1] * B.goalOffset];
    push(cyl(`${OWN} BOILER stack`, 'copper', [gc[0], gc[1], B.bodyH], [gc[0], gc[1], 88], B.stackR, B.stackR, 24));
    push(cyl(`${OWN} HIGH EFFICIENCY GOAL`, 'copper', [gc[0], gc[1], 88], [gc[0], gc[1], B.goalZ], B.stackR, B.funnelR, 24, true));
    push(cyl(`${OWN} HIGH GOAL opening`, 'hole', [gc[0], gc[1], B.goalZ - 0.6], [gc[0], gc[1], B.goalZ - 0.5], B.funnelR - 0.6, B.funnelR - 0.6, 24));
    push(cyl(`${OWN} HIGH GOAL vision tape (4")`, 'retro', [gc[0], gc[1], 84], [gc[0], gc[1], 88], B.stackR + 0.1, B.stackR + 0.1, 24, true));
    push(cyl(`${OWN} HIGH GOAL vision tape (2")`, 'retro', [gc[0], gc[1], 78], [gc[0], gc[1], 80], B.stackR + 0.1, B.stackR + 0.1, 24, true));
    const face = [bc[0] + n2[0] * (half + 0.15), bc[1] + n2[1] * (half + 0.15)];
    push(box(`${OWN} LOW EFFICIENCY GOAL`, 'hole', [face[0], face[1], B.lowGoal.z + B.lowGoal.h / 2],
      [0.3, B.lowGoal.w, B.lowGoal.h], Math.atan2(n2[1], n2[0]) / D2R));
    // NET (figure 3-33): a funnel open toward the field, from the roof's back and side edges out to a frame
    // 9 ft 11 in tall, 4 ft 10 in across and 4 ft 6-3/4 in deep (its front posts in the field-face plane).
    // Misses hit it, roll down onto the roof and off its front.
    {
      const top = B.frameH, W = B.frameW / 2, back = half - B.frameD, roofBack = B.bodyH + B.roofRise, t = 0.6;
      push(slab(`${OWN} BOILER net`, 'net', [P(-half, -half, roofBack), P(half, -half, roofBack), P(W, back, top), P(-W, back, top)], t));
      for (const s2 of [-1, 1]) {
        // each side is two flat triangles (its bottom edge follows the sloping roof edge)
        push(slab(`${OWN} BOILER net`, 'net', [P(s2 * half, -half, roofBack), P(s2 * half, half, B.bodyH), P(s2 * W, half, top)], t));
        push(slab(`${OWN} BOILER net`, 'net', [P(s2 * half, -half, roofBack), P(s2 * W, half, top), P(s2 * W, back, top)], t));
      }
      // frame: four posts, top rails and side rails at body height
      const ang = Math.atan2(n2[1], n2[0]) / D2R;
      const posts = [[-W, half], [W, half], [W, back], [-W, back]];
      for (const [u, d] of posts) push(box(`${OWN} BOILER frame post`, own + 'Dark', [...P2(u, d), top / 2], [2, 2, top], ang));
      for (let k = 0; k < 4; k++) {
        const [ua, da] = posts[k], [ub, db] = posts[(k + 1) % 4];
        if (k === 0) continue; // nothing across the open front
        push(segBox(`${OWN} BOILER frame rail`, own + 'Dark', P2(ua, da), P2(ub, db), 1.5, top - 1.5, top));
        if (k !== 2) push(segBox(`${OWN} BOILER frame rail`, own + 'Dark', P2(ua, da), P2(ub, db), 1.5, B.bodyH - 1.5, B.bodyH));
      }
    }

    // Airship ------------------------------------------------------------------
    const A = DIM.airship;
    const ac = [X(A.cx), 0];
    const w = s < 0 ? 180 : 0; // direction the airship's lifts face (toward own wall)
    const rd = hexR(A.deckFlat), rr = hexR(A.railFlat);
    const hex = (r) => ring(ac[0], ac[1], r, 6, 30);
    push(loft(`${OWN} AIRSHIP base`, 'steelDark', hex(rd), 0, hex(rd), A.deckZ));
    push(loft(`${OWN} AIRSHIP deck`, 'deck', hex(rd), A.deckZ - 0.75, hex(rd), A.deckZ));
    push(loft(`${OWN} AIRSHIP walls`, 'poly', hex(rd), A.deckZ, hex(rr), A.railZ, [false, false]));
    const rv = hex(rr);
    for (let k = 0; k < 6; k++) {
      push(segBox(`${OWN} AIRSHIP rail`, own, rv[k], rv[(k + 1) % 6], 1.5, A.railZ - 1.5, A.railZ));
      push(segBox(`${OWN} AIRSHIP rail`, 'alu', rv[k], rv[(k + 1) % 6], 1.5, A.railZ - 12, A.railZ - 10.5));
    }
    push(loft(`${OWN} STEAM TANK`, 'tank', ring(ac[0], ac[1], A.tankR, 6, 30), A.deckZ, ring(ac[0], ac[1], A.tankR, 6, 30), A.tankTop));
    push(loft(`${OWN} STEAM TANK pressure lights`, own, ring(ac[0], ac[1], A.tankR + 0.1, 6, 30), A.deckZ + 8, ring(ac[0], ac[1], A.tankR + 0.1, 6, 30), A.deckZ + 20, [false, false]));
    // Central rotor (Da Vinci aerial screw)
    const [cz0, cz1, cr] = A.centralRotor;
    push(cyl(`${OWN} ROTOR 1 shaft`, 'alu', [ac[0], ac[1], A.tankTop], [ac[0], ac[1], cz1 + 1], 0.75));
    push({ type: 'helicoid', name: `${OWN} ROTOR 1 (central)`, mat: 'rotor', c: [ac[0], ac[1]], r0: 1.5, r1: cr, z0: cz0, z1: cz1, turns: 1, group: 'field' });
    // Small rotors on the rail (2, 3, 4)
    // CAD: small rotors sit at 90/210/330 deg on both airships (mirror symmetry).
    // Which one is ROTOR 2/3/4 is not yet confirmed, so they are named generically.
    for (const ang of [90, 210, 330]) {
      const p = polar(ac[0], ac[1], A.rotorR, ang);
      const nmr = `${OWN} small ROTOR (${ang}°)`;
      push(cyl(nmr + ' post', 'alu', [p[0], p[1], A.railZ], [p[0], p[1], A.rotorTop], 0.66));
      push(cyl(nmr + ' hub', 'rotor', [p[0], p[1], A.rotorTop - 1], [p[0], p[1], A.rotorTop + 0.5], 1.5));
      for (let b = 0; b < 3; b++) {
        const q = polar(p[0], p[1], 9.5, ang + 15 + b * 120);
        push(segBox(nmr + ' blade', 'rotor', p, q, 2.2, A.rotorTop - 0.4, A.rotorTop));
      }
    }
    // Lifts on the three faces toward the alliance wall
    for (const [k, off] of [[0, 0], [1, 60], [2, -60]]) {
      const ang = w + off;
      const u = [Math.cos(ang * D2R), Math.sin(ang * D2R)];
      const t = [-u[1], u[0]];
      const P = (r, lat) => [ac[0] + u[0] * r + t[0] * lat, ac[1] + u[1] * r + t[1] * lat];
      const nml = `${OWN} LIFT ${['center', 'left', 'right'][k]}`;
      for (const lat of [-7, 7]) {
        const p = P(rd * Math.sqrt(3) / 2 + 3, lat);
        push(box(nml + ' guide frame', 'steel', [p[0], p[1], 20], [2, 1.5, 40], ang));
      }
      const cp = P(rd * Math.sqrt(3) / 2 + 3.5, 0);
      push(box(nml + ' carriage', 'steel', [cp[0], cp[1], 13], [2, 12, 10], ang));
      push(cyl(nml + ' peg', 'spring', [...P(38.9, 0), 13], [...P(49.4, 0), 13], 0.69, 0.69, 12));
      for (const lat of [-4.125, 4.125]) {
        const vp = P(rd * Math.sqrt(3) / 2 + 4.6, lat);
        push(box(nml + ' vision target', 'retro', [vp[0], vp[1], 10.75 + 2.5], [0.1, 2, 5], ang));
      }
    }
    // Barriers flanking the center lift (HDPE, radial from the adjacent corners)
    for (const ang of [w + 30, w - 30]) {
      push(segBox(`${OWN} AIRSHIP barrier`, 'hdpe', polar(ac[0], ac[1], rd, ang), polar(ac[0], ac[1], rd + 24.75, ang), 0.5, 0, 6.25));
    }
    // Davits, ropes, touchpads (on the flats at w, w+120, w-120)
    for (const off of [0, 120, -120]) {
      const ang = w + off;
      const p = polar(ac[0], ac[1], A.davitR, ang);
      const pr = polar(ac[0], ac[1], rr * Math.sqrt(3) / 2, ang);
      const pc = polar(ac[0], ac[1], A.davitR + 1.75, ang);
      push(segBox(`${OWN} DAVIT arm`, 'steel', pr, pc, 2, A.railZ + 4, A.railZ + 6));
      push(box(`${OWN} DAVIT channel`, 'steel', [pc[0], pc[1], (A.touchpadZ - 2 + A.railZ + 6) / 2], [2, 2, A.railZ + 8 - A.touchpadZ], ang));
      push(cyl(`${OWN} ROPE`, 'rope', [p[0], p[1], A.railZ + 6], [p[0], p[1], 0.5], 0.5, 0.5, 10));
      // round plate (user, 2026-11-22), 10 in across, the rope through its centre
      push(cyl(`${OWN} TOUCHPAD`, 'touchpad', [p[0], p[1], A.touchpadZ - 0.125], [p[0], p[1], A.touchpadZ + 0.125], 5, 5, 32));
      push({ type: 'dome', name: `${OWN} TOUCHPAD dome`, mat: 'dome', c: [pc[0], pc[1], A.touchpadZ + 6], r: 2.5, group: 'field' });
    }
    // Reserve gear at the base of the steam tank (official rule; off by default in the sim)
    {
      const p = polar(ac[0], ac[1], A.tankR + 7, w + 180);
      gears.push({ c: [p[0], p[1], A.deckZ + 1], flat: true, rotZ: 0, reserve: OWN.toLowerCase() });
    }

  }

  // Midline (sim addition, not on the 2017 field): charcoal tape across the field at X = 0
  // (no MIDLINE tape: removed at the user's request, 2026-11-24)

  // Hoppers --------------------------------------------------------------------
  const H = DIM.hopper;
  for (const [side, list] of [[1, H.containers.scoring], [-1, H.containers.far]]) {
    list.forEach(([x0, x1], idx) => {
      const yIn = side * H.yIn, yOut = side * H.yOut;
      const cx = (x0 + x1) / 2, cy = (yIn + yOut) / 2;
      const sx = x1 - x0, sy = Math.abs(yOut - yIn);
      const nm = `HOPPER container (${side > 0 ? 'scoring side' : 'far side'} #${idx + 1})`;
      // translucent walls (back, sides, the front below the floor, and the DOOR above it, which opens when the
      // pair's push panel is pressed), aluminium frame and floor, and a shallow wedge on the floor sloping down
      // toward the field so the FUEL leans on the door and spills out once it opens
      const yMin = Math.min(yIn, yOut), yMax = Math.max(yIn, yOut), T = 0.5;
      const zW = (H.zBottom + H.zTop) / 2, hW = H.zTop - H.zBottom;
      push(box(nm + ' back', 'polyHopper', [cx, yOut - side * T / 2, zW], [sx, T, hW]));
      for (const wx of [x0 + T / 2, x1 - T / 2]) push(box(nm + ' side', 'polyHopper', [wx, cy, zW], [T, sy, hW]));
      const fz = H.floorZ + 0.25; // top of the floor plate
      push(box(nm + ' front', 'polyHopper', [cx, yIn + side * T / 2, (H.zBottom + fz) / 2], [sx, T, fz - H.zBottom]));
      push(box(nm + ' door', 'polyHopper', [cx, yIn + side * T / 2, (fz + H.zTop) / 2], [sx, T, H.zTop - fz]));
      push(box(nm + ' floor', 'alu', [cx, cy, H.floorZ], [sx - 1, sy - 1, 0.5]));
      const rect = (ya, yb) => [[x0 + T, ya], [x1 - T, ya], [x1 - T, yb], [x0 + T, yb]];
      const wedge0 = rect(yMin + T, yMax - T);
      const wedge1 = side > 0 ? rect(yMax - 2 * T, yMax - T) : rect(yMin + T, yMin + 2 * T);
      push(loft(nm + ' floor slope', 'alu', wedge0, fz, wedge1, fz + H.slope));
      for (const fx of [x0 + 0.75, x1 - 0.75]) for (const fy of [yIn + side * 0.75, yOut - side * 0.75]) {
        push(box(nm + ' frame', 'alu', [fx, fy, H.zTop / 2], [1.5, 1.5, H.zTop]));
      }
      // 50 fuel per container: non-overlapping 4 x 4 layers resting on the slope (they settle when live)
      let n = 0;
      for (let layer = 0; n < 50; layer++) {
        for (let i = 0; i < 4 && n < 50; i++) for (let j = 0; j < 4 && n < 50; j++) {
          const fy = cy + (j - 1.5) * 5.1, t = (Math.abs(fy) - H.yIn) / (H.yOut - H.yIn);
          fuel.push([cx + (i - 1.5) * 5.3, fy, fz + H.slope * t + DIM.fuelR + 0.15 + layer * 5.15]);
          n++;
        }
      }
    });
    // Push panels sit in the guardrail between each pair of containers
    for (let k = 0; k + 1 < list.length; k += 2) {
      const gx = (list[k][1] + list[k + 1][0]) / 2;
      push(box('HOPPER push panel', 'pushPanel', [gx, side * (DIM.field.halfY - 0.5), 2 + 7.5], [22, 0.5, 15]));
    }
  }

  return { items, fuel, gears };
}

// Return loading stations, by the alliance that uses them (red's is in the +X corner, at the blue end).
// face: the panel's ends (a, b); inward: unit normal into the field; chutes: opening centres on the face;
// zoneDepth: the RETRIEVAL ZONE reaches this far from the face (far edge of the tape, manual 3.2).
export function loadingStations() {
  const L = DIM.loadStation, out = {};
  for (const [al, s] of [['red', 1], ['blue', -1]]) {
    const a = [s * L.a[0], L.a[1]], b = [s * L.b[0], L.b[1]];
    const len = Math.hypot(b[0] - a[0], b[1] - a[1]), dir = [(b[0] - a[0]) / len, (b[1] - a[1]) / len];
    const pad = (len - 75.5) / 2;
    let inward = [-dir[1], dir[0]];
    const mid = [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2];
    if (inward[0] * mid[0] + inward[1] * mid[1] > 0) inward = [-inward[0], -inward[1]];
    const chutes = L.openings.map((o) => { const t = pad + (o[0] + o[1]) / 2; return [a[0] + dir[0] * t, a[1] + dir[1] * t]; });
    out[al] = { a, b, inward, chutes, chuteHalfW: (L.openings[0][1] - L.openings[0][0]) / 2, chuteZ0: L.openZ[0], chuteZ1: L.openZ[1], zoneDepth: 42 };
  }
  return out;
}

// LOADING LANES (the user's name for the overflow opening in each alliance's own wall): where every game piece that
// doesn't start in a robot goes (manual 4.1: up to 10 FUEL and 1 GEAR per team). Same shape as loadingStations(),
// one chute; zoneHalfW limits the zone along the wall (a robot within zoneDepth of the wall and this close to the
// opening's centre, measured along the wall).
export function loadingLanes() {
  const O = DIM.overflow, out = {};
  for (const [al, s] of [['red', -1], ['blue', 1]]) {
    const x = s * DIM.field.halfX, cy = (O.open[0] + O.open[1]) / 2;
    out[al] = { a: [x, O.y0], b: [x, O.y1], inward: [-s, 0], chutes: [[x, cy]], chuteHalfW: (O.open[1] - O.open[0]) / 2,
                chuteZ0: O.openZ[0], chuteZ1: O.openZ[1], zoneDepth: 36, zoneHalfW: 40 };
  }
  return out;
}

// Hopper pairs and their push panels (for the game: pushing a panel dumps both containers' FUEL)
export function hoppers() {
  const H = DIM.hopper, out = [];
  for (const [side, list] of [[1, H.containers.scoring], [-1, H.containers.far]]) {
    for (let k = 0; k + 1 < list.length; k += 2) {
      const gx = (list[k][1] + list[k + 1][0]) / 2;
      out.push({ panel: [gx, side * (DIM.field.halfY - 0.5)], panelHalfW: 11, side,
                 containers: [list[k][0], list[k][1], list[k + 1][0], list[k + 1][1]],
                 yIn: side * H.yIn, yOut: side * H.yOut, floorZ: H.floorZ, fieldHalfY: DIM.field.halfY, railH: DIM.sideRail.h });
    }
  }
  return out;
}

// ---------------------------------------------------------------------------
// Collision geometry (engine-agnostic). Every collider is a vertical prism:
// a convex polygon (CCW, inches) extruded from z0 to z1.
//   robot: true  -> robots collide with it (anything reaching below robot height)
export const FIELD_OUTLINE = [
  // CCW, starting at the red wall's boiler-side end
  [-326, -124], [-322, -130.8], [-292.3, -160.5], [-288.7, -162.4],
  [288.7, -162.4], [292.3, -160.5], [322, -130.8], [326, -124],
  [326, 124.5], [254, 162.4], [-254, 162.4], [-326, 124.5],
];
const CHUTE_EDGES = new Set([7, 8, 10, 11]), CHUTE_Z = 25; // edges whose openings have chutes behind them
const OUTLINE_H = [78.4, 54, 54, 20.2, 54, 54, 78.4, 78.4, 78.4, 20.2, 78.4, 78.4]; // boiler edges: body height (54)

function obb(c, sx, sy, rotZ) {
  const a = rotZ * D2R, ca = Math.cos(a), sa = Math.sin(a);
  return [[-1, -1], [1, -1], [1, 1], [-1, 1]].map(([u, v]) =>
    [c[0] + ca * u * sx / 2 - sa * v * sy / 2, c[1] + sa * u * sx / 2 + ca * v * sy / 2]);
}

export function colliders(lay = layout()) {
  const out = [];
  const add = (name, pts, z0, z1) => out.push({ name, pts, z0, z1 });

  // Thick perimeter slabs outside each outline edge (robust against tunnelling).
  const n = FIELD_OUTLINE.length, T = 24, EXT = 8;
  for (let i = 0; i < n; i++) {
    const a = FIELD_OUTLINE[i], b = FIELD_OUTLINE[(i + 1) % n];
    const dx = b[0] - a[0], dy = b[1] - a[1], L = Math.hypot(dx, dy);
    const ux = dx / L, uy = dy / L, nx = uy, ny = -ux; // outward for a CCW outline
    const a2 = [a[0] - ux * EXT, a[1] - uy * EXT], b2 = [b[0] + ux * EXT, b[1] + uy * EXT];
    // Alliance walls and loading-station faces have chutes behind their openings (z from CHUTE_Z up): the slab
    // stops below them, and above that the wall's own panels (with their open holes) are the boundary
    const top = CHUTE_EDGES.has(i) ? CHUTE_Z - 1 : OUTLINE_H[i];
    add('Field perimeter', [a2, [a2[0] + nx * T, a2[1] + ny * T], [b2[0] + nx * T, b2[1] + ny * T], b2], -10, top);
  }

  const skipMat = new Set(['carpet', 'hole', 'retro', 'rope', 'dome']);
  for (const it of lay.items) {
    if (it.group !== 'field' || skipMat.has(it.mat)) continue;
    if (it.type === 'box' && !it.tilt) {
      add(it.name, obb(it.c, it.size[0], it.size[1], it.rotZ), it.c[2] - it.size[2] / 2, it.c[2] + it.size[2] / 2);
    } else if (it.type === 'loft') {
      // prisms exactly; frustums (boiler roof, airship walls) approximated by their mid ring here, with
      // the exact bottom and top rings kept for engines that can collide with a convex frustum (Unity)
      const ring = it.ring0.map((p, k) => [(p[0] + it.ring1[k][0]) / 2, (p[1] + it.ring1[k][1]) / 2]);
      const frustum = it.ring0.some((p, k) => Math.hypot(p[0] - it.ring1[k][0], p[1] - it.ring1[k][1]) > 1e-6);
      out.push({ name: it.name, pts: ring, z0: it.z0, z1: it.z1, ...(frustum ? { pts0: it.ring0, pts1: it.ring1 } : {}) });
    } else if (it.type === 'cyl') {
      const [p0, p1] = [it.p0, it.p1], r = Math.max(it.r0, it.r1);
      if (Math.hypot(p1[0] - p0[0], p1[1] - p0[1]) < 1e-6) {
        add(it.name, ring(p0[0], p0[1], r, 12, 0), Math.min(p0[2], p1[2]), Math.max(p0[2], p1[2]));
      } else {
        const len = Math.hypot(p1[0] - p0[0], p1[1] - p0[1]);
        const c = [(p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2];
        const ang = Math.atan2(p1[1] - p0[1], p1[0] - p0[0]) / D2R;
        const zc = (p0[2] + p1[2]) / 2;
        add(it.name, obb(c, len, 2 * r, ang), zc - r, zc + r);
      }
    }
  }
  return out;
}

// Ropes as simulated chains: anchored at the davit, threaded through the touchpad plate.
// Default FIELD ROPE: 86" below the knot (manual 3.8); a few inches rest on the carpet.
export function ropes(lay = layout()) {
  return lay.items.filter((it) => it.mat === 'rope').map((it) => ({
    name: it.name,
    anchor: it.p0.slice(),                              // top, at the davit fingers
    pin: [it.p0[0], it.p0[1], DIM.airship.touchpadZ],  // touchpad centre the rope passes through
    pinR: 1.5,                                          // free play inside the 3" x 6" cut-out
    length: 88,
    radius: 0.5,
    // slack on the carpet trails away from the airship centre
    out: (() => { const cx = Math.sign(it.p0[0]) * DIM.airship.cx, dx = it.p0[0] - cx, dy = it.p0[1], d = Math.hypot(dx, dy); return [dx / d, dy / d]; })(),
  }));
}

// Scoring targets per alliance (geometry only; rules live in game.js).
//   baseLineX: the plane a bumper must break in AUTO (far edge of the green tape, seen from the wall)
//   highGoal:  opening disc at Z 97 (21.5" dia); a ball counts as loaded when its centre drops through
//              the opening within `capture` of the centre (ball must clear the rim)
//   lowGoal:   25 x 8.75 opening on the boiler's field face, bottom at 18
//   lifts:     peg tip position and outward normal, centre/left/right
//   touchpads: centre (= rope line) and plate height
export function targets() {
  const out = {};
  const A = DIM.airship, B = DIM.boiler;
  for (const s of [-1, 1]) {
    const al = s < 0 ? 'red' : 'blue';
    const bc = [s * B.cx, B.cy];
    const n2 = [-s / Math.SQRT2, 1 / Math.SQRT2];
    const gc = [bc[0] + n2[0] * B.goalOffset, bc[1] + n2[1] * B.goalOffset];
    const face = [bc[0] + n2[0] * B.size / 2, bc[1] + n2[1] * B.size / 2];
    const ac = [s * A.cx, 0], w = s < 0 ? 180 : 0;
    const lifts = [0, 60, -60].map((off) => {
      const a = (w + off) * D2R, u = [Math.cos(a), Math.sin(a)];
      const name = off === 0 ? 'center' : (u[1] < 0 ? 'boiler-side' : 'loading-side');
      return { name, u, tip: [ac[0] + u[0] * 49.4, ac[1] + u[1] * 49.4], z: 13 };
    });
    const touchpads = [0, 120, -120].map((off) => {
      const a = (w + off) * D2R;
      return { c: [ac[0] + Math.cos(a) * A.davitR, ac[1] + Math.sin(a) * A.davitR], z: A.touchpadZ };
    });
    out[al] = {
      side: s,
      baseLineX: s * (DIM.baseLine - DIM.tapeW / 2),
      // LAUNCHPAD: from the alliance wall to the field-centre edge of its launchpad line (the tape is in it)
      launchpadX: s * (DIM.launchpadLine - DIM.tapeW / 2),
      // KEY: the boiler corner cut off by the key tape (a -> b); the boiler's side of the line is inside
      key: [s * 325.0, -43.7, s * 202.5, -161.4],
      highGoal: { c: gc, z: B.goalZ, r: 21.5 / 2, capture: 21.5 / 2 - DIM.fuelR + 1 },
      lowGoal: { face, n: n2, u: [-n2[1], n2[0]], halfW: B.lowGoal.w / 2, z0: B.lowGoal.z, z1: B.lowGoal.z + B.lowGoal.h },
      airship: ac,
      lifts,
      touchpads,
    };
  }
  return out;
}
