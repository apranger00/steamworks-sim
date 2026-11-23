// Turns the engine-agnostic layout (inches, Z-up) into three.js meshes (metres, Y-up).
// Mapping: spec (X, Y, Z) -> three (X, Z, -Y), scaled by IN.
import { IN } from './field.js';

export function makeMaterials(THREE) {
  const std = (color, extra = {}) => new THREE.MeshStandardMaterial({ color, roughness: 0.75, metalness: 0.05, ...extra });
  const clear = (color, opacity) => new THREE.MeshStandardMaterial({
    color, transparent: true, opacity, roughness: 0.1, metalness: 0.0, depthWrite: false, side: THREE.DoubleSide });
  return {
    carpet: std(0x387d2d, { roughness: 1 }),
    poly: clear(0xcfe6ff, 0.22),
    polyHopper: clear(0xdfeeff, 0.18),
    alu: std(0xb9bec4, { metalness: 0.6, roughness: 0.4 }),
    steel: std(0x6e747a, { metalness: 0.5, roughness: 0.5 }),
    steelDark: std(0x3b3f44, { metalness: 0.3, roughness: 0.7 }),
    deck: std(0x8a8f94, { roughness: 0.9 }),
    hole: new THREE.MeshBasicMaterial({ color: 0x0b0b0d }),
    red: std(0xd32030),
    blue: std(0x1f5fd1),
    redDark: std(0x7a2a24, { metalness: 0.25, roughness: 0.6 }),
    blueDark: std(0x26395f, { metalness: 0.25, roughness: 0.6 }),
    copper: std(0xb06a3b, { metalness: 0.6, roughness: 0.35, side: THREE.DoubleSide }),
    retro: std(0xe8eef2, { metalness: 0.2, roughness: 0.2, emissive: 0x2a2f33 }),
    tank: std(0x9a7b4f, { metalness: 0.5, roughness: 0.45 }),
    rotor: std(0xc9a227, { metalness: 0.6, roughness: 0.35, side: THREE.DoubleSide }),
    spring: std(0x9aa3ab, { metalness: 0.8, roughness: 0.3 }),
    hdpe: std(0xf1efe6, { roughness: 0.6 }),
    rope: std(0xc8a86b, { roughness: 1 }),
    touchpad: clear(0xffffff, 0.55),
    dome: std(0xf5f5f5, { transparent: true, opacity: 0.8, roughness: 0.2 }),
    net: new THREE.MeshBasicMaterial({ color: 0x222222, transparent: true, opacity: 0.25, side: THREE.DoubleSide, depthWrite: false }),
    pushPanel: clear(0xffe27a, 0.45),
    bin: std(0x2b2b2b, { roughness: 0.9 }),
    fuel: std(0xf3e21b, { roughness: 0.55 }),
    gear: std(0xd9a514, { roughness: 0.45, metalness: 0.15 }),
    tape: {
      white: std(0xf4f4f4, { roughness: 0.9, polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -2 }),
      green: std(0x1fbf4c, { roughness: 0.9, polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -2 }),
      red: std(0xe0202e, { roughness: 0.9, polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -2 }),
      blue: std(0x2266e0, { roughness: 0.9, polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -2 }),
      charcoal: std(0x36393d, { roughness: 0.9, polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -2 }),
    },
  };
}

// spec inches -> three metres
export const v3 = (THREE, x, y, z) => new THREE.Vector3(x * IN, z * IN, -y * IN);

function loftGeometry(THREE, it) {
  const n = it.ring0.length;
  const pos = [];
  const P = (p, z) => [p[0] * IN, z * IN, -p[1] * IN];
  const tri = (a, b, c) => pos.push(...a, ...b, ...c);
  for (let k = 0; k < n; k++) {
    const a0 = P(it.ring0[k], it.z0), b0 = P(it.ring0[(k + 1) % n], it.z0);
    const a1 = P(it.ring1[k], it.z1), b1 = P(it.ring1[(k + 1) % n], it.z1);
    tri(a0, b0, b1); tri(a0, b1, a1);
  }
  if (it.caps[0]) { const c = P(centroid(it.ring0), it.z0); for (let k = 0; k < n; k++) tri(c, P(it.ring0[(k + 1) % n], it.z0), P(it.ring0[k], it.z0)); }
  if (it.caps[1]) { const c = P(centroid(it.ring1), it.z1); for (let k = 0; k < n; k++) tri(c, P(it.ring1[k], it.z1), P(it.ring1[(k + 1) % n], it.z1)); }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.computeVertexNormals();
  return g;
}
const centroid = (r) => [r.reduce((s, p) => s + p[0], 0) / r.length, r.reduce((s, p) => s + p[1], 0) / r.length];

function helicoidGeometry(THREE, it) {
  const pos = [], idx = [];
  const nu = 6, nv = 72;
  for (let j = 0; j <= nv; j++) {
    const t = j / nv, a = t * it.turns * Math.PI * 2, z = it.z0 + (it.z1 - it.z0) * t;
    for (let i = 0; i <= nu; i++) {
      const r = it.r0 + (it.r1 - it.r0) * (i / nu);
      pos.push((it.c[0] + r * Math.cos(a)) * IN, z * IN, -(it.c[1] + r * Math.sin(a)) * IN);
    }
  }
  for (let j = 0; j < nv; j++) for (let i = 0; i < nu; i++) {
    const a = j * (nu + 1) + i, b = a + 1, c = a + nu + 1, d = c + 1;
    idx.push(a, b, d, a, d, c);
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
  g.setIndex(idx);
  g.computeVertexNormals();
  return g;
}

export function gearGeometry(THREE, od = 11, thick = 2, teeth = 10) {
  const shape = new THREE.Shape();
  const ro = od / 2 * IN, rr = (od / 2 - 1.1) * IN;
  const steps = teeth * 4;
  for (let k = 0; k <= steps; k++) {
    const a = (k / steps) * Math.PI * 2;
    const r = (k % 4 === 1 || k % 4 === 2) ? ro : rr;
    if (k === 0) shape.moveTo(r * Math.cos(a), r * Math.sin(a)); else shape.lineTo(r * Math.cos(a), r * Math.sin(a));
  }
  const hole = new THREE.Path();
  hole.absarc(0, 0, 0.6 * IN, 0, Math.PI * 2, true);
  shape.holes.push(hole);
  const g = new THREE.ExtrudeGeometry(shape, { depth: thick * IN, bevelEnabled: false });
  g.translate(0, 0, -thick * IN / 2);
  g.rotateX(-Math.PI / 2); // lie flat: extrusion axis -> world Y
  return g;
}

export function buildField(THREE, layoutData) {
  const M = makeMaterials(THREE);
  const groups = { field: new THREE.Group(), tape: new THREE.Group(), pieces: new THREE.Group() };
  const up = new THREE.Vector3(0, 1, 0);

  for (const it of layoutData.items) {
    if (it.mat === 'rope') continue; // ropes are simulated and drawn by the page
    let mesh;
    const mat = it.type === 'tape' ? M.tape[it.color] : M[it.mat];
    if (!mat) throw new Error('Unknown material: ' + (it.mat || it.color) + ' on ' + it.name);
    if (it.type === 'box') {
      const g = new THREE.BoxGeometry(it.size[0] * IN, it.size[2] * IN, it.size[1] * IN);
      mesh = new THREE.Mesh(g, mat);
      mesh.position.copy(v3(THREE, ...it.c));
      mesh.rotation.order = 'YXZ';
      mesh.rotation.y = it.rotZ * Math.PI / 180;
      if (it.tilt) mesh.rotation.x = it.tilt * Math.PI / 180;
    } else if (it.type === 'slab') {
      // flat convex polygon thickened along its normal
      const p = it.pts.map((q) => v3(THREE, q[0], q[1], q[2]));
      const nrm = new THREE.Vector3().subVectors(p[1], p[0]).cross(new THREE.Vector3().subVectors(p[2], p[0])).normalize().multiplyScalar(it.t * IN);
      const q = p.map((v) => v.clone().add(nrm)), n = p.length, pos = [];
      const tri = (a, b, c) => pos.push(a.x, a.y, a.z, b.x, b.y, b.z, c.x, c.y, c.z);
      for (let k = 1; k + 1 < n; k++) { tri(p[0], p[k], p[k + 1]); tri(q[0], q[k + 1], q[k]); }
      for (let k = 0; k < n; k++) { const a = p[k], b = p[(k + 1) % n]; tri(a, b, q[(k + 1) % n]); tri(a, q[(k + 1) % n], q[k]); }
      const g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
      g.computeVertexNormals();
      mesh = new THREE.Mesh(g, mat);
    } else if (it.type === 'tape') {
      const dx = it.b[0] - it.a[0], dy = it.b[1] - it.a[1];
      const len = Math.hypot(dx, dy);
      const g = new THREE.BoxGeometry(len * IN, 0.06 * IN, it.w * IN);
      mesh = new THREE.Mesh(g, mat);
      mesh.position.copy(v3(THREE, (it.a[0] + it.b[0]) / 2, (it.a[1] + it.b[1]) / 2, 0.03));
      mesh.rotation.y = Math.atan2(dy, dx);
    } else if (it.type === 'loft') {
      mesh = new THREE.Mesh(loftGeometry(THREE, it), mat);
    } else if (it.type === 'cyl') {
      const a = v3(THREE, ...it.p0), b = v3(THREE, ...it.p1);
      const dir = new THREE.Vector3().subVectors(b, a);
      const len = dir.length();
      // CylinderGeometry: radiusTop is at +Y, so r1 (at p1) is the top.
      const g = new THREE.CylinderGeometry(it.r1 * IN, it.r0 * IN, len, it.seg, 1, it.open);
      mesh = new THREE.Mesh(g, mat);
      mesh.position.copy(a).addScaledVector(dir, 0.5);
      mesh.quaternion.setFromUnitVectors(up, dir.normalize());
    } else if (it.type === 'helicoid') {
      mesh = new THREE.Mesh(helicoidGeometry(THREE, it), mat);
    } else if (it.type === 'dome') {
      const g = new THREE.SphereGeometry(it.r * IN, 16, 8, 0, Math.PI * 2, 0, Math.PI / 2);
      mesh = new THREE.Mesh(g, mat);
      mesh.position.copy(v3(THREE, ...it.c));
    } else {
      throw new Error('Unknown item type ' + it.type);
    }
    mesh.name = it.name;
    const opaque = !mat.transparent;
    mesh.castShadow = opaque && it.type !== 'tape' && it.mat !== 'carpet';
    mesh.receiveShadow = opaque;
    (groups[it.group] || groups.field).add(mesh);
  }

  // FUEL: one instanced mesh
  const ballGeo = new THREE.SphereGeometry(2.5 * IN, 14, 10);
  const fuel = new THREE.InstancedMesh(ballGeo, M.fuel, layoutData.fuel.length);
  const m4 = new THREE.Matrix4();
  layoutData.fuel.forEach((p, i) => { m4.makeTranslation(p[0] * IN, p[2] * IN, -p[1] * IN); fuel.setMatrixAt(i, m4); });
  fuel.instanceMatrix.needsUpdate = true;
  fuel.name = 'FUEL';
  fuel.castShadow = true; fuel.receiveShadow = true;
  groups.pieces.add(fuel);

  // GEARS: instanced (reserve GEARS are drawn by the page only when that rule is on)
  const staged = layoutData.gears.filter((g) => !g.reserve);
  const gearGeo = gearGeometry(THREE);
  const gears = new THREE.InstancedMesh(gearGeo, M.gear, staged.length);
  const q = new THREE.Quaternion(), sc = new THREE.Vector3(1, 1, 1);
  staged.forEach((g, i) => {
    q.setFromAxisAngle(up, (g.rotZ || 0) * Math.PI / 180);
    m4.compose(v3(THREE, ...g.c), q, sc);
    gears.setMatrixAt(i, m4);
  });
  gears.instanceMatrix.needsUpdate = true;
  gears.name = 'GEAR';
  gears.castShadow = true; gears.receiveShadow = true;
  groups.pieces.add(gears);

  return { groups, materials: M };
}

// Official-CAD ghost: header 'CAD1', nVerts u32, nTris u32, grid f32 (inches),
// then int16 xyz (spec frame, grid units), then u32 triangle indices.
export function parseCadGhost(THREE, buf) {
  const dv = new DataView(buf);
  const magic = String.fromCharCode(dv.getUint8(0), dv.getUint8(1), dv.getUint8(2), dv.getUint8(3));
  if (magic !== 'CAD1') throw new Error('Bad CAD ghost file');
  const nv = dv.getUint32(4, true), nt = dv.getUint32(8, true), grid = dv.getFloat32(12, true);
  const q = new Int16Array(buf, 16, nv * 3);
  const idx = new Uint32Array(buf, 16 + nv * 6, nt * 3);
  const pos = new Float32Array(nv * 3);
  const k = grid * IN;
  for (let i = 0; i < nv; i++) {
    pos[3 * i] = q[3 * i] * k;
    pos[3 * i + 1] = q[3 * i + 2] * k;
    pos[3 * i + 2] = -q[3 * i + 1] * k;
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  g.setIndex(new THREE.BufferAttribute(idx, 1));
  g.computeVertexNormals();
  return g;
}

// Robot visual: 6WD base, alliance bumpers, simple superstructure with a yellow front intake bar.
// Returns { root, update(robot, dt) } — root is positioned/rotated from the physics state.
export function makeRobotMesh(THREE, spec, allianceColor) {
  const root = new THREE.Group();
  const std = (c, e = {}) => new THREE.MeshStandardMaterial({ color: c, roughness: 0.6, metalness: 0.1, ...e });
  const box = (sx, sy, sz, x, y, z, mat) => { // robot-local inches: x forward, y left, z up
    const m = new THREE.Mesh(new THREE.BoxGeometry(sx * IN, sz * IN, sy * IN), mat);
    m.position.set(x * IN, z * IN, -y * IN); m.castShadow = true; m.receiveShadow = true; root.add(m); return m;
  };
  const L = spec.length, W = spec.width, bt = spec.bumper.thick;
  const fl = L - 2 * bt, fw = W - 2 * bt; // frame size
  const bz = (spec.bumper.z0 + spec.bumper.z1) / 2, bh = spec.bumper.z1 - spec.bumper.z0;
  const bump = std(allianceColor, { roughness: 0.85 });
  box(L, bt, bh, 0, W / 2 - bt / 2, bz, bump);
  box(L, bt, bh, 0, -W / 2 + bt / 2, bz, bump);
  box(bt, fw, bh, L / 2 - bt / 2, 0, bz, bump);
  box(bt, fw, bh, -L / 2 + bt / 2, 0, bz, bump);
  const alu = std(0xb9bec4, { metalness: 0.6, roughness: 0.4 });
  const dark = std(0x2d3135);
  box(fl, fw, 2, 0, 0, 3.5, alu);                         // frame
  box(fl - 4, fw - 6, 0.25, 0, 0, 2.6, dark);             // belly pan
  // Superstructure: electronics tower + gear holder + front intake bar
  box(fl - 10, fw - 8, 14, -3, 0, 4.5 + 7, dark);
  box(4, fw - 2, 3, fl / 2 - 3, 0, 9, std(0xf2c230));      // front intake (marks the front)
  box(5, fw - 12, 5, fl / 2 - 3, 0, 21, std(0x60676e));    // gear mechanism
  // Held GEAR (infinite supply for testing), standing upright at peg height, facing forward
  const heldGear = new THREE.Mesh(gearGeometry(THREE), std(0xd9a514, { roughness: 0.45 }));
  heldGear.geometry.rotateZ(Math.PI / 2); // axis -> robot forward (local X)
  heldGear.position.set((L / 2 - 2.5) * IN, 13 * IN, 0); heldGear.castShadow = true; root.add(heldGear);
  // Shooter turret at the robot centre (fuel exits at Z 30)
  const turret = new THREE.Group(); turret.position.set(0, 18.5 * IN, 0); root.add(turret);
  const tb = new THREE.Mesh(new THREE.CylinderGeometry(5 * IN, 5.5 * IN, 3 * IN, 20), std(0x3d4349)); tb.position.y = 1.5 * IN; tb.castShadow = true; turret.add(tb);
  const hood = new THREE.Group(); hood.position.y = 6 * IN; turret.add(hood);
  const barrel = new THREE.Mesh(new THREE.BoxGeometry(11 * IN, 5.5 * IN, 6 * IN), std(0x8a9096, { metalness: 0.5 }));
  barrel.position.x = 3 * IN; barrel.castShadow = true; hood.add(barrel);
  // Arrow on the roof pointing forward
  const arrowShape = new THREE.Shape();
  arrowShape.moveTo(9 * IN, 0); arrowShape.lineTo(-3 * IN, 6 * IN); arrowShape.lineTo(-3 * IN, -6 * IN); arrowShape.closePath();
  const arrow = new THREE.Mesh(new THREE.ShapeGeometry(arrowShape), new THREE.MeshBasicMaterial({ color: 0xffffff }));
  arrow.rotation.x = -Math.PI / 2; arrow.position.set(-10 * IN, 18.55 * IN, 0); arrow.scale.set(0.6, 0.6, 0.6); root.add(arrow);
  // Wheels: 3 per side (drop-centre 6WD)
  const wheels = { L: [], R: [] };
  const wGeo = new THREE.CylinderGeometry(spec.wheelR * IN, spec.wheelR * IN, 1.5 * IN, 18);
  wGeo.rotateX(Math.PI / 2); // axle along three Z (= robot lateral)
  const tread = std(0x1b1b1b, { roughness: 0.95 });
  const hub = std(0x8b9096, { metalness: 0.5 });
  for (const side of [1, -1]) for (const x of [-11, 0, 11]) {
    const g = new THREE.Group();
    const w = new THREE.Mesh(wGeo, tread); w.castShadow = true; g.add(w);
    const h = new THREE.Mesh(new THREE.BoxGeometry(spec.wheelR * 1.6 * IN, 0.8 * IN, 1.6 * IN), hub); g.add(h); // spokes show rotation
    g.position.set(x * IN, (spec.wheelR - (x === 0 ? 0.06 : 0)) * IN, -side * (spec.track / 2) * IN);
    root.add(g); (side > 0 ? wheels.L : wheels.R).push(g);
  }
  let aL = 0, aR = 0;
  return {
    root, turret, hood, heldGear,
    // aim: { yaw (world rad), pitch (rad) } or null to face forward
    aim(r, a) {
      turret.rotation.y = a ? a.yaw - r.th : 0;
      hood.rotation.z = a ? Math.min(1.3, a.pitch) : 0.35;
    },
    update(r, dt) {
      root.position.set(r.x * IN, (r.lift || 0) * IN, -r.y * IN);
      root.rotation.y = r.th;
      aL += (r.wheelL / spec.wheelR) * dt; aR += (r.wheelR / spec.wheelR) * dt;
      for (const g of wheels.L) g.rotation.z = -aL;
      for (const g of wheels.R) g.rotation.z = -aR;
    },
  };
}
