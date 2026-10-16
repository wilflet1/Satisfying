import * as THREE from 'three';

// ---------------------------------------------------------------------------
// Tunables (metres / seconds)
// ---------------------------------------------------------------------------
const TABLE = { w: 1.525, l: 2.74, h: 0.76 };
const NET_H = 0.1525;
const BALL_R = 0.022;
const GRAVITY = -9.81;
// The paddle's grip sits here; the phone slider moves it left/right along X.
// Rotation happens around the grip, exactly like the phone turning in your hand.
const GRIP_Y = 0.98;
const GRIP_Z = 1.3;
const SLIDE_RANGE = 0.9;                        // slider end -> player X in metres
const CAM_OFFSET = new THREE.Vector3(0, 0.47, 1.0);   // camera relative to the player's grip home
const CAM_LOOK = new THREE.Vector3(0, -0.03, -0.5);
const MAX_HAND_OFFSET = 1.2;                    // clamp tracked hand movement (m)
const BLADE_R = 0.095;
const BLADE_OFFSET = 0.12;                      // grip -> blade centre along handle
const BLADE_HALF_T = 0.007;
const SUBSTEPS = 16;
const MAX_BALLS = 12;

// ---------------------------------------------------------------------------
// Renderer / scene
// ---------------------------------------------------------------------------
const renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
renderer.setSize(innerWidth, innerHeight);
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFShadowMap;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
document.body.appendChild(renderer.domElement);

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x1b2230);
scene.fog = new THREE.Fog(0x1b2230, 7, 16);

const camera = new THREE.PerspectiveCamera(60, innerWidth / innerHeight, 0.02, 50);
camera.position.set(0, 1.45, 2.3);
camera.lookAt(0, 0.95, 0.8);

addEventListener('resize', () => {
  camera.aspect = innerWidth / innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(innerWidth, innerHeight);
});

scene.add(new THREE.HemisphereLight(0xcfe3ff, 0x30261c, 0.9));
const sun = new THREE.DirectionalLight(0xffffff, 2.2);
sun.position.set(0.6, 6, 1.2);
sun.castShadow = true;
sun.shadow.mapSize.set(2048, 2048);
Object.assign(sun.shadow.camera, { left: -3, right: 3, top: 3, bottom: -3, near: 1, far: 10 });
sun.shadow.bias = -0.0005;
scene.add(sun);

// Room
const ROOM = { x: 4, zBack: -5, zFront: 4.5, ceil: 4 };
const floor = new THREE.Mesh(
  new THREE.PlaneGeometry(ROOM.x * 2, ROOM.zFront - ROOM.zBack),
  new THREE.MeshStandardMaterial({ color: 0x3a3f4a, roughness: 0.9 }),
);
floor.rotation.x = -Math.PI / 2;
floor.position.z = (ROOM.zFront + ROOM.zBack) / 2;
floor.receiveShadow = true;
scene.add(floor);
const grid = new THREE.GridHelper(10, 20, 0x555a66, 0x474c57);
grid.position.y = 0.001;
scene.add(grid);
const wallMat = new THREE.MeshStandardMaterial({ color: 0x252c3a, roughness: 1 });
const backWall = new THREE.Mesh(new THREE.PlaneGeometry(ROOM.x * 2, ROOM.ceil), wallMat);
backWall.position.set(0, ROOM.ceil / 2, ROOM.zBack);
backWall.receiveShadow = true;
scene.add(backWall);
for (const s of [-1, 1]) {
  const w = new THREE.Mesh(new THREE.PlaneGeometry(ROOM.zFront - ROOM.zBack, ROOM.ceil), wallMat);
  w.rotation.y = -s * Math.PI / 2;
  w.position.set(s * ROOM.x, ROOM.ceil / 2, (ROOM.zFront + ROOM.zBack) / 2);
  scene.add(w);
}

// Table
const table = new THREE.Group();
const top = new THREE.Mesh(
  new THREE.BoxGeometry(TABLE.w, 0.03, TABLE.l),
  new THREE.MeshStandardMaterial({ color: 0x1f5fa8, roughness: 0.55 }),
);
top.position.y = TABLE.h - 0.015;
top.receiveShadow = top.castShadow = true;
table.add(top);
const lineMat = new THREE.MeshBasicMaterial({ color: 0xffffff });
const addLine = (w, l, x, z) => {
  const m = new THREE.Mesh(new THREE.PlaneGeometry(w, l), lineMat);
  m.rotation.x = -Math.PI / 2;
  m.position.set(x, TABLE.h + 0.0005, z);
  table.add(m);
};
addLine(TABLE.w, 0.02, 0, TABLE.l / 2 - 0.01);
addLine(TABLE.w, 0.02, 0, -TABLE.l / 2 + 0.01);
addLine(0.02, TABLE.l, TABLE.w / 2 - 0.01, 0);
addLine(0.02, TABLE.l, -TABLE.w / 2 + 0.01, 0);
addLine(0.003, TABLE.l, 0, 0);
const legMat = new THREE.MeshStandardMaterial({ color: 0x222222, metalness: 0.5, roughness: 0.4 });
for (const x of [-0.65, 0.65]) for (const z of [-1.2, 1.2]) {
  const leg = new THREE.Mesh(new THREE.BoxGeometry(0.05, TABLE.h - 0.03, 0.05), legMat);
  leg.position.set(x, (TABLE.h - 0.03) / 2, z);
  leg.castShadow = true;
  table.add(leg);
}
const net = new THREE.Mesh(
  new THREE.PlaneGeometry(TABLE.w + 0.3, NET_H),
  new THREE.MeshStandardMaterial({ color: 0xffffff, transparent: true, opacity: 0.55, side: THREE.DoubleSide }),
);
net.position.set(0, TABLE.h + NET_H / 2, 0);
table.add(net);
scene.add(table);

// Paddle. Modelled in the phone's own frame: +Y = top of phone, +Z = out of screen.
// The grip (bottom of phone) is the origin.
const paddle = new THREE.Group();
const bladeGeo = new THREE.CylinderGeometry(BLADE_R, BLADE_R, BLADE_HALF_T, 48);
bladeGeo.rotateX(Math.PI / 2);
const front = new THREE.Mesh(bladeGeo, new THREE.MeshStandardMaterial({ color: 0xd42a2a, roughness: 0.7 }));
front.position.set(0, BLADE_OFFSET, BLADE_HALF_T / 2);
const back = new THREE.Mesh(bladeGeo, new THREE.MeshStandardMaterial({ color: 0x111111, roughness: 0.7 }));
back.position.set(0, BLADE_OFFSET, -BLADE_HALF_T / 2);
const handle = new THREE.Mesh(
  new THREE.BoxGeometry(0.028, 0.1, 0.022),
  new THREE.MeshStandardMaterial({ color: 0xc89b5f, roughness: 0.6 }),
);
handle.position.y = 0.0;
for (const m of [front, back, handle]) { m.castShadow = true; paddle.add(m); }
scene.add(paddle);

// ---------------------------------------------------------------------------
// Orientation input
// ---------------------------------------------------------------------------
// Earth frame (X east, Y north, Z up) -> three.js (X right, Y up, Z toward viewer)
const EARTH_TO_THREE = new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(1, 0, 0), -Math.PI / 2);
const calib = new THREE.Quaternion();
const rawQ = new THREE.Quaternion();     // latest phone orientation in three.js frame (pre-calibration)
const targetQ = new THREE.Quaternion();  // calibrated paddle orientation
const imuOffset = new THREE.Vector3();  // hand movement estimated from the accelerometer (three.js frame)
let slideX = 0;                          // -1..1 from the phone slider
let haveSample = false;
let lastSampleAt = -1e9;
let wantCenter = true;

// AR (WebXR) tracking from the phone camera: real position in metres, Y up.
const xrQ = new THREE.Quaternion(), xrP = new THREE.Vector3();
const xrCalib = new THREE.Quaternion(), xrOrigin = new THREE.Vector3();
let lastXrAt = -1e9, wantXrCenter = true;

// Yaw that turns the phone's current heading to "toward the monitor" (-Z).
function yawAlign(q, out) {
  const dir = new THREE.Vector3(0, 1, 0).applyQuaternion(q);
  if (Math.hypot(dir.x, dir.z) < 0.3) dir.set(0, 0, -1).applyQuaternion(q); // phone pointing straight up/down
  const heading = Math.atan2(dir.x, dir.z);
  return out.setFromAxisAngle(new THREE.Vector3(0, 1, 0), Math.PI - heading);
}

function center() {
  if (haveSample) yawAlign(rawQ, calib);
  if (performance.now() - lastXrAt < 1000) { yawAlign(xrQ, xrCalib); xrOrigin.copy(xrP); }
}

// Mouse fallback so the scene is usable before a phone connects.
const mouse = { x: 0, y: 0 };
addEventListener('pointermove', (e) => {
  mouse.x = (e.clientX / innerWidth) * 2 - 1;
  mouse.y = (e.clientY / innerHeight) * 2 - 1;
});
const FLAT_FORWARD = new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(1, 0, 0), -Math.PI / 2);
const _e = new THREE.Euler();
function mouseQuat(out) {
  _e.set(-mouse.y * 0.8, 0, 0, 'YXZ');
  return out.setFromEuler(_e).multiply(FLAT_FORWARD);
}

// ---------------------------------------------------------------------------
// Networking
// ---------------------------------------------------------------------------
const connEl = document.getElementById('conn');
const statsEl = document.getElementById('stats');
let ws;
let sampleCount = 0, rate = 0, hits = 0;
setInterval(() => { rate = sampleCount; sampleCount = 0; }, 1000);

function connect() {
  ws = new WebSocket(`${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/?role=screen`);
  ws.onmessage = (e) => {
    const m = JSON.parse(e.data);
    if (m.t === 'o') {
      rawQ.set(m.q[0], m.q[1], m.q[2], m.q[3]).premultiply(EARTH_TO_THREE);
      haveSample = true;
      lastSampleAt = performance.now();
      sampleCount++;
      if (wantCenter) { yawAlign(rawQ, calib); wantCenter = false; }
      if (m.p) imuOffset.set(m.p[0], m.p[1], m.p[2]).applyQuaternion(EARTH_TO_THREE).applyQuaternion(calib);
      else imuOffset.set(0, 0, 0);
    } else if (m.t === 'x') {
      xrQ.set(m.q[0], m.q[1], m.q[2], m.q[3]);
      xrP.set(m.p[0], m.p[1], m.p[2]);
      lastXrAt = performance.now();
      if (wantXrCenter) { yawAlign(xrQ, xrCalib); xrOrigin.copy(xrP); wantXrCenter = false; }
    } else if (m.t === 's') {
      slideX = THREE.MathUtils.clamp(m.x, -1, 1);
    } else if (m.t === 'center') {
      if (haveSample) yawAlign(rawQ, calib); else wantCenter = true;
      if (performance.now() - lastXrAt < 1000) { yawAlign(xrQ, xrCalib); xrOrigin.copy(xrP); } else wantXrCenter = true;
    } else if (m.t === 'drop') {
      dropBall();
    } else if (m.t === 'status') {
      connEl.textContent = m.phones ? 'connected' : 'not connected';
      connEl.className = m.phones ? 'ok' : 'bad';
      if (!m.phones) { haveSample = false; wantCenter = wantXrCenter = true; imuOffset.set(0, 0, 0); }
    }
  };
  ws.onclose = () => setTimeout(connect, 800);
}
connect();
fetch('/info').then((r) => r.json()).then((i) => { document.getElementById('url').textContent = i.phoneUrl; });

// ---------------------------------------------------------------------------
// Audio
// ---------------------------------------------------------------------------
let audio;
function unlockAudio() {
  if (!audio) audio = new AudioContext();
  if (audio.state === 'suspended') audio.resume();
}
addEventListener('pointerdown', unlockAudio);
addEventListener('keydown', unlockAudio);

function pock(freq, vol) {
  if (!audio || audio.state !== 'running') return;
  const t = audio.currentTime;
  const o = audio.createOscillator();
  const g = audio.createGain();
  o.type = 'sine';
  o.frequency.setValueAtTime(freq, t);
  o.frequency.exponentialRampToValueAtTime(freq * 0.6, t + 0.06);
  g.gain.setValueAtTime(Math.min(1, vol), t);
  g.gain.exponentialRampToValueAtTime(0.0001, t + 0.08);
  o.connect(g).connect(audio.destination);
  o.start(t);
  o.stop(t + 0.09);
}

// ---------------------------------------------------------------------------
// Balls
// ---------------------------------------------------------------------------
const ballGeo = new THREE.SphereGeometry(BALL_R, 24, 16);
const ballMat = new THREE.MeshStandardMaterial({ color: 0xffffff, emissive: 0xff8a2a, emissiveIntensity: 0.25, roughness: 0.35 });
const balls = [];

function dropBall() {
  // Above the line the paddle slides along, so the slider can always reach it.
  const b = {
    mesh: new THREE.Mesh(ballGeo, ballMat),
    pos: new THREE.Vector3(
      THREE.MathUtils.clamp(playerX + THREE.MathUtils.randFloat(-0.25, 0.25), -TABLE.w / 2 + 0.05, TABLE.w / 2 - 0.05),
      1.8, GRIP_Z - BLADE_OFFSET * 0.8),
    vel: new THREE.Vector3(0, 0, 0),
    still: 0,
    cooldown: 0,
  };
  b.mesh.castShadow = true;
  b.mesh.position.copy(b.pos);
  scene.add(b.mesh);
  balls.push(b);
  if (balls.length > MAX_BALLS) removeBall(balls[0]);
}

function removeBall(b) {
  scene.remove(b.mesh);
  balls.splice(balls.indexOf(b), 1);
}

document.getElementById('dropBtn').addEventListener('click', dropBall);
const hud = document.getElementById('hud');
const toggleHud = () => { hud.style.display = hud.style.display === 'none' ? '' : 'none'; };
document.getElementById('hideBtn').addEventListener('click', toggleHud);
addEventListener('keydown', (e) => {
  if (e.code === 'Space') { e.preventDefault(); dropBall(); }
  if (e.code === 'KeyH') toggleHud();
  if (e.code === 'KeyC' && haveSample) center();
});

// ---------------------------------------------------------------------------
// Paddle kinematics
// ---------------------------------------------------------------------------
const gripPrev = new THREE.Vector3(), gripCur = new THREE.Vector3();
const qPrev = new THREE.Quaternion(), qCur = new THREE.Quaternion();
const vGrip = new THREE.Vector3();   // smoothed linear velocity of grip
const omega = new THREE.Vector3();   // smoothed angular velocity
let firstFrame = true;

let playerX = 0;
let trackMode = 'mouse';
const handOffset = new THREE.Vector3();
const _cam = new THREE.Vector3();

const _dq = new THREE.Quaternion(), _qp = new THREE.Quaternion(), _v = new THREE.Vector3(), _w = new THREE.Vector3();
function updatePaddle(dt) {
  const now = performance.now();
  const sensorLive = haveSample && now - lastSampleAt < 250;
  const xrLive = now - lastXrAt < 500;
  const live = sensorLive || xrLive;

  // Rotation: orientation sensor (fastest), else AR pose, else mouse.
  if (sensorLive) targetQ.copy(rawQ).premultiply(calib);
  else if (xrLive) targetQ.copy(xrQ).premultiply(xrCalib);
  else mouseQuat(targetQ);

  // Hand movement, 1:1 in metres. AR tracking is exact; the accelerometer is a
  // short-lived estimate. Rotation alone never moves the grip.
  if (xrLive) handOffset.subVectors(xrP, xrOrigin).applyQuaternion(xrCalib);
  else if (sensorLive) handOffset.copy(imuOffset);
  else handOffset.set(0, 0, 0);
  handOffset.clampLength(0, MAX_HAND_OFFSET);
  trackMode = xrLive ? 'AR camera tracking' : sensorLive ? 'motion sensors' : 'mouse mode';

  // Slider moves the player: camera and paddle together.
  playerX = (live ? slideX : mouse.x) * SLIDE_RANGE;
  _cam.set(playerX, GRIP_Y, GRIP_Z);
  camera.position.copy(_cam).add(CAM_OFFSET);
  camera.lookAt(_cam.add(CAM_LOOK));

  qPrev.copy(qCur);
  gripPrev.copy(gripCur);
  qCur.copy(targetQ); // no smoothing on pose: show exactly the latest sample
  gripCur.set(playerX, GRIP_Y, GRIP_Z).add(handOffset);
  gripCur.y = Math.max(gripCur.y, 0.05);
  if (firstFrame) { qPrev.copy(qCur); gripPrev.copy(gripCur); firstFrame = false; }

  // Velocities (smoothed a little, since phone samples arrive unevenly vs frames)
  _v.subVectors(gripCur, gripPrev).divideScalar(dt);
  _w.set(0, 0, 0);
  _dq.copy(qCur).multiply(_qp.copy(qPrev).invert());
  if (_dq.w < 0) _dq.set(-_dq.x, -_dq.y, -_dq.z, -_dq.w);
  const angle = 2 * Math.acos(Math.min(1, _dq.w));
  const s = Math.sqrt(1 - _dq.w * _dq.w);
  if (s > 1e-6) _w.set(_dq.x / s, _dq.y / s, _dq.z / s).multiplyScalar(angle / dt);
  const k = 0.55;
  vGrip.lerp(_v, k);
  omega.lerp(_w, k);

  paddle.position.copy(gripCur);
  paddle.quaternion.copy(qCur);
}

// ---------------------------------------------------------------------------
// Physics
// ---------------------------------------------------------------------------
const _qi = new THREE.Quaternion(), _grip = new THREE.Vector3(), _q = new THREE.Quaternion();
const _local = new THREE.Vector3(), _n = new THREE.Vector3(), _cp = new THREE.Vector3();
const _vp = new THREE.Vector3(), _rel = new THREE.Vector3(), _r = new THREE.Vector3();

function collidePaddle(b, alpha) {
  // Paddle pose interpolated across the frame -> swept collision, no tunnelling.
  _grip.lerpVectors(gripPrev, gripCur, alpha);
  _q.slerpQuaternions(qPrev, qCur, alpha);
  _qi.copy(_q).invert();
  _local.subVectors(b.pos, _grip).applyQuaternion(_qi);
  const dy = _local.y - BLADE_OFFSET;
  if (Math.abs(_local.z) > BALL_R + BLADE_HALF_T) return;
  if (_local.x * _local.x + dy * dy > BLADE_R * BLADE_R) return;

  const side = _local.z >= 0 ? 1 : -1;
  _n.set(0, 0, side).applyQuaternion(_q);
  // Velocity of the paddle surface at the contact point.
  _cp.set(_local.x, _local.y, 0).applyQuaternion(_q).add(_grip);
  _r.subVectors(_cp, _grip);
  _vp.crossVectors(omega, _r).add(vGrip);
  _rel.subVectors(b.vel, _vp);
  const vn = _rel.dot(_n);
  if (vn < 0) {
    const tangential = _rel.clone().addScaledVector(_n, -vn).multiplyScalar(0.85);
    _rel.copy(tangential).addScaledVector(_n, -vn * 0.88);
    b.vel.copy(_rel).add(_vp);
    if (b.cooldown <= 0) {
      const power = -vn;
      hits++;
      pock(900 + Math.min(power, 12) * 40, 0.25 + power * 0.08);
      if (ws && ws.readyState === 1) ws.send(JSON.stringify({ t: 'hit', power }));
      b.cooldown = 0.05;
    }
  }
  // Push out of the blade.
  _local.z = side * (BALL_R + BLADE_HALF_T + 0.0005);
  b.pos.copy(_local.applyQuaternion(_q).add(_grip));
}

function step(b, h, alpha) {
  b.vel.y += GRAVITY * h;
  b.vel.multiplyScalar(1 - 0.12 * h); // air drag
  b.pos.addScaledVector(b.vel, h);
  b.cooldown -= h;

  // Table top
  const onTable = Math.abs(b.pos.x) < TABLE.w / 2 && Math.abs(b.pos.z) < TABLE.l / 2;
  if (onTable && b.pos.y < TABLE.h + BALL_R && b.pos.y > TABLE.h - 0.03 && b.vel.y < 0) {
    b.pos.y = TABLE.h + BALL_R;
    if (b.vel.y < -0.4) pock(520, Math.min(0.5, -b.vel.y * 0.12));
    b.vel.y = b.vel.y > -0.15 ? 0 : b.vel.y * -0.9;
    b.vel.x *= 0.97; b.vel.z *= 0.97;
  }
  // Net
  if (Math.abs(b.pos.z) < BALL_R && b.pos.y < TABLE.h + NET_H && b.pos.y > TABLE.h && Math.abs(b.pos.x) < TABLE.w / 2 + 0.15) {
    b.pos.z = Math.sign(b.vel.z || 1) * -BALL_R;
    b.vel.z *= -0.25;
    b.vel.x *= 0.5;
  }
  // Room
  if (b.pos.y < BALL_R) {
    b.pos.y = BALL_R;
    if (b.vel.y < -0.5) pock(380, Math.min(0.4, -b.vel.y * 0.08));
    b.vel.y = Math.abs(b.vel.y) < 0.3 ? 0 : -b.vel.y * 0.75;
    b.vel.x *= 0.96; b.vel.z *= 0.96;
  }
  if (b.pos.y > ROOM.ceil - BALL_R) { b.pos.y = ROOM.ceil - BALL_R; b.vel.y *= -0.7; }
  if (Math.abs(b.pos.x) > ROOM.x - BALL_R) { b.pos.x = Math.sign(b.pos.x) * (ROOM.x - BALL_R); b.vel.x *= -0.7; }
  if (b.pos.z < ROOM.zBack + BALL_R) { b.pos.z = ROOM.zBack + BALL_R; b.vel.z *= -0.7; }
  if (b.pos.z > ROOM.zFront - BALL_R) { b.pos.z = ROOM.zFront - BALL_R; b.vel.z *= -0.7; }

  collidePaddle(b, alpha);
}

// ---------------------------------------------------------------------------
// Loop
// ---------------------------------------------------------------------------
let last = performance.now();
renderer.setAnimationLoop((now) => {
  const dt = Math.min(0.05, Math.max(0.001, (now - last) / 1000));
  last = now;
  updatePaddle(dt);

  const h = dt / SUBSTEPS;
  for (const b of [...balls]) {
    for (let i = 1; i <= SUBSTEPS; i++) step(b, h, i / SUBSTEPS);
    b.mesh.position.copy(b.pos);
    const resting = b.pos.y < BALL_R + 0.001 && b.vel.lengthSq() < 0.01;
    b.still = resting ? b.still + dt : 0;
    if (resting) b.vel.multiplyScalar(0.9);
    if (b.still > 3) removeBall(b);
  }

  statsEl.innerHTML = `hits: ${hits}<br>${trackMode}${haveSample ? ` · ${rate} Hz` : ''}`;
  renderer.render(scene, camera);
});

dropBall();
