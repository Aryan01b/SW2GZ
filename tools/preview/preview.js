// SW2GZ browser preview — three.js + URDFLoader, fed by the File System Access
// API (no server needed beyond static-serving this file). User picks a
// workspace folder; we walk it, surface every package that has a urdf/.xacro,
// strip xacro on the fly into raw URDF, and resolve mesh refs by looking
// FileHandles up in the workspace tree and serving them as blob URLs.

import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { STLLoader }     from 'three/addons/loaders/STLLoader.js';
import { ColladaLoader } from 'three/addons/loaders/ColladaLoader.js';
import URDFLoader        from 'urdf-loader';

// ── DOM refs ─────────────────────────────────────────────────────
const stage     = document.getElementById('stage');
const empty     = document.getElementById('empty');
const status    = document.getElementById('status');
const pkginfo   = document.getElementById('pkginfo');
const jointsDiv = document.getElementById('joints');
const resetBtn  = document.getElementById('reset');
const openBtn   = document.getElementById('openBtn');
const openBtn2  = document.getElementById('openBtn2');
const pkgSelect = document.getElementById('pkgSelect');
const wsErr     = document.getElementById('wsErr');

// ── Three scene ──────────────────────────────────────────────────
const scene = new THREE.Scene();
scene.background = null;

const camera = new THREE.PerspectiveCamera(45, 1, 0.01, 100);
camera.position.set(0.9, 0.7, 0.9);
camera.up.set(0, 0, 1);

const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
renderer.setPixelRatio(window.devicePixelRatio);
stage.appendChild(renderer.domElement);
renderer.domElement.style.display = 'none';   // shown after first load

function resize() {
  const w = stage.clientWidth;
  const h = stage.clientHeight;
  renderer.setSize(w, h, false);
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
}
window.addEventListener('resize', resize);
resize();

scene.add(new THREE.HemisphereLight(0x8090a0, 0x202028, 0.7));
const key = new THREE.DirectionalLight(0xffffff, 0.9);
key.position.set(2, 3, 2.5);
scene.add(key);

const grid = new THREE.GridHelper(4, 40, 0x444c5a, 0x232a36);
grid.rotation.x = Math.PI / 2;
grid.material.opacity = 0.45;
grid.material.transparent = true;
scene.add(grid);

scene.add(new THREE.AxesHelper(0.3));

// ── Gz corner gizmo ─────────────────────────────────────────────
// Small RGB axes triad pinned to the top-right corner of the viewport that
// shows the world coord frame at a glance — does NOT move with orbit. Drawn
// as a separate scene + ortho camera overlay, rendered after the main scene
// each frame with autoClear=false.
const gizmoSize = 90; // pixels
const gizmoScene = new THREE.Scene();
const gizmoCam = new THREE.OrthographicCamera(-1.2, 1.2, 1.2, -1.2, 0.1, 10);
gizmoCam.position.set(0, 0, 3);
gizmoCam.up.set(0, 0, 1);
gizmoScene.add(new THREE.AxesHelper(1));

const controls = new OrbitControls(camera, renderer.domElement);
controls.target.set(0, 0, 0.25);
controls.enableDamping = true;
controls.dampingFactor = 0.08;
controls.update();

// Robot + per-load state.
let robot = null;
let linkLabels = [];
let blobUrls = [];

// ── Workspace tree ───────────────────────────────────────────────
// fileMap is relPath -> FileSystemFileHandle. relPath uses forward slashes
// rooted at the workspace folder the user picked.
const fileMap = new Map();

async function walkDir(handle, prefix) {
  for await (const [name, entry] of handle.entries()) {
    const rel = prefix ? `${prefix}/${name}` : name;
    if (entry.kind === 'directory') {
      await walkDir(entry, rel);
    } else {
      fileMap.set(rel, entry);
    }
  }
}

// Find every package (a parent directory of any urdf/*.urdf.xacro). Returns
// [{ name, root, xacro }] where name is the package name (parent-of-urdf
// folder), root is its relPath, xacro is the relPath of the .urdf.xacro.
function findPackages() {
  const pkgs = [];
  for (const rel of fileMap.keys()) {
    const m = rel.match(/^(.*\/)?([^/]+)\/urdf\/([^/]+)\.urdf\.xacro$/);
    if (!m) continue;
    const parents = m[1] || '';
    const pkgName = m[2];
    pkgs.push({
      name: pkgName,
      root: `${parents}${pkgName}`,
      xacro: rel,
    });
  }
  return pkgs;
}

// ── Xacro → URDF strip (mirrors serve.ps1 ConvertTo-Urdf) ────────
function xacroToUrdf(text, opts) {
  // self-closing xacro tags
  text = text.replace(/<xacro:[a-zA-Z_:]+\b[^/>]*\/>/g, '');
  // paired xacro blocks (DOTALL via [\s\S])
  text = text.replace(/<xacro:([a-zA-Z_:]+)\b[^>]*>[\s\S]*?<\/xacro:\1>/g, '');
  // xmlns:xacro on root
  text = text.replace(/\s+xmlns:xacro="[^"]*"/g, '');
  // Optionally rewrite package://<pkg>/<rest> to a relative path so URDFLoader
  // can lookup meshes via the existing fileMap or HTTP loader without needing
  // its `packages` lookup populated.
  if (opts && opts.packageRoot != null) {
    text = text.replace(/package:\/\/[^/]+\//g, opts.packageRoot);
  }
  return text;
}

// ── Mesh resolution ─────────────────────────────────────────────
// urdf-loader's loadMeshCb takes a URL. We're given URLs like
// "package://full_arm/meshes/foo.dae" — strip "package://<pkg>/" and look
// the rest up in fileMap under <pkgRoot>/.
async function blobForMesh(packageRoot, urdfPath) {
  // urdfPath looks like "<pkgRoot>/meshes/foo.dae" after xacroToUrdf rewrote
  // package:// → "<pkgRoot>/", or "package://full_arm/meshes/foo.dae" if a
  // future code path skips that rewrite. Normalize both shapes.
  let rel = urdfPath;
  rel = rel.replace(/^package:\/\/[^/]+\//, '');
  rel = rel.replace(new RegExp('^' + packageRoot.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '/'), '');
  // candidate paths to try, in order
  const candidates = [];
  candidates.push(`${packageRoot}/${rel}`);
  // SW2GZ-specific fallback: visual .dae → collision .stl
  if (/\.dae$/i.test(rel)) {
    const stl = rel.replace(/\.dae$/i, '_collision.stl');
    candidates.push(`${packageRoot}/${stl}`);
  }
  for (const c of candidates) {
    const handle = fileMap.get(c);
    if (handle) {
      const file = await handle.getFile();
      const url = URL.createObjectURL(file);
      blobUrls.push(url);
      return { url, ext: c.split('.').pop().toLowerCase() };
    }
  }
  return null;
}

// ── Load + render a package ─────────────────────────────────────
async function loadPackage(pkg) {
  setStatus(`loading ${pkg.name}…`);
  // Clear previous robot.
  if (robot) {
    scene.remove(robot);
    robot = null;
  }
  for (const el of linkLabels) el.el.remove();
  linkLabels = [];
  for (const u of blobUrls) URL.revokeObjectURL(u);
  blobUrls = [];
  jointsDiv.innerHTML = '';
  resetBtn.style.display = 'none';

  // Read + strip xacro. Rewrite package:// to "<pkgRoot>/" so URDFLoader's
  // mesh URLs match what blobForMesh expects when looking them up in fileMap.
  const xacroFile  = await fileMap.get(pkg.xacro).getFile();
  const xacroText  = await xacroFile.text();
  const urdfText   = xacroToUrdf(xacroText, { packageRoot: `${pkg.root}/` });

  // Configure URDFLoader.
  const loader = new URDFLoader();
  // URDFLoader warns "<pkg> not found in provided package list" and skips
  // loadMeshCb when packages is empty — even with a custom loadMeshCb. Set a
  // function that accepts ANY package name; loadMeshCb below handles the actual
  // resolution (either via fileMap or our HTTP-fallback proxy).
  loader.packages = () => '.';
  loader.loadMeshCb = async (path, manager, done) => {
    try {
      const resolved = await blobForMesh(pkg.root, path);
      if (!resolved) { done(new THREE.Object3D()); return; }
      const { url, ext } = resolved;
      if (ext === 'stl') {
        new STLLoader(manager).load(url, geom => {
          geom.computeVertexNormals();
          const mat = new THREE.MeshStandardMaterial({
            color: 0xb8c2cf, roughness: 0.65, metalness: 0.1,
          });
          done(new THREE.Mesh(geom, mat));
        }, undefined, err => { console.warn('STL fail', url, err); done(new THREE.Object3D()); });
      } else if (ext === 'dae') {
        new ColladaLoader(manager).load(url, dae => {
          done(dae.scene);
        }, undefined, err => { console.warn('DAE fail', url, err); done(new THREE.Object3D()); });
      } else {
        done(new THREE.Object3D());
      }
    } catch (e) { console.warn('mesh resolve threw', path, e); done(new THREE.Object3D()); }
  };

  let loaded;
  try {
    loaded = loader.parse(urdfText);
  } catch (e) {
    showError(`URDF parse failed: ${e.message}`);
    return;
  }

  robot = loaded;
  scene.add(robot);

  // Frame triads per link.
  const linkNames = Object.keys(robot.links);
  for (const ln of linkNames) {
    const link = robot.links[ln];
    link.add(new THREE.AxesHelper(0.12));
    const el = document.createElement('div');
    el.className = 'link-label';
    el.textContent = ln;
    stage.appendChild(el);
    linkLabels.push({ link, el });
  }

  // Joint TF markers — smaller triads at each movable joint's child-link
  // frame. After the joint-origin fix these should overlap the link triad
  // exactly; any visible offset is a frame-resolution bug we want to expose.
  const moveTypesViz = new Set(['revolute', 'continuous', 'prismatic']);
  for (const [, joint] of Object.entries(robot.joints)) {
    if (!moveTypesViz.has(joint.jointType)) continue;
    const childName = joint.child;
    const childLink = childName ? robot.links[childName] : null;
    const host = childLink || joint;
    host.add(new THREE.AxesHelper(0.06));
  }

  // Joint sliders.
  const moveTypes = new Set(['revolute', 'continuous', 'prismatic']);
  const jointEntries = Object.entries(robot.joints).filter(([, j]) => moveTypes.has(j.jointType));
  for (const [name, joint] of jointEntries) {
    // URDF says continuous joints don't have limits. URDFLoader still
    // populates joint.limit.{lower,upper}=0 when the <limit> element exists
    // without lower/upper attrs (which is our exporter's output). Treat
    // continuous AND zero-range as "no limit" and fall back to ±π.
    const isCont = joint.jointType === 'continuous';
    const lo = joint.limit?.lower, up = joint.limit?.upper;
    const zeroRange = lo === 0 && up === 0;
    const lower = (isCont || zeroRange || !Number.isFinite(lo)) ? -Math.PI : lo;
    const upper = (isCont || zeroRange || !Number.isFinite(up)) ?  Math.PI : up;
    const row = document.createElement('div');
    row.className = 'joint';
    row.innerHTML = `
      <div class="name">${name} <span style="color:var(--subtle)">(${joint.jointType})</span></div>
      <div class="row">
        <input type="range" min="${lower}" max="${upper}" step="0.001" value="0">
        <span class="val">0.000</span>
      </div>`;
    const input = row.querySelector('input');
    const val   = row.querySelector('.val');
    input.addEventListener('input', () => {
      const v = parseFloat(input.value);
      robot.setJointValue(name, v);
      val.textContent = v.toFixed(3);
    });
    jointsDiv.appendChild(row);
  }
  if (jointEntries.length > 0) resetBtn.style.display = 'block';

  resetBtn.onclick = () => {
    for (const [name] of jointEntries) robot.setJointValue(name, 0);
    for (const inp of jointsDiv.querySelectorAll('input')) {
      inp.value = '0';
      inp.dispatchEvent(new Event('input', { bubbles: true }));
    }
  };

  pkginfo.textContent = `${pkg.name} · ${linkNames.length} links · ${jointEntries.length} movable joints`;
  empty.style.display = 'none';
  renderer.domElement.style.display = 'block';
  status.style.display = 'block';
  resize();
  fitCamera();
  setStatus('loaded');
  setTimeout(() => { status.style.opacity = '0.5'; }, 1200);
}

function fitCamera() {
  if (!robot) return;
  const box = new THREE.Box3().setFromObject(robot);
  if (!isFinite(box.min.x)) return;
  const size = new THREE.Vector3();
  const ctr  = new THREE.Vector3();
  box.getSize(size); box.getCenter(ctr);
  const radius = Math.max(size.length() * 0.6, 0.3) + 0.2;
  controls.target.copy(ctr);
  camera.position.copy(ctr).add(new THREE.Vector3(radius, radius * 0.9, radius * 0.9));
  camera.near = Math.max(radius / 1000, 0.001);
  camera.far  = Math.max(radius * 100, 50);
  camera.updateProjectionMatrix();
  controls.update();
}

function setStatus(text) {
  status.textContent = text;
  status.style.color = '';
  status.style.opacity = '1';
}

function showError(text) {
  wsErr.innerHTML = `<div class="err">${text}</div>`;
  setStatus(text);
  status.style.color = 'var(--axis-x)';
  console.error(text);
}

// ── Animation loop ───────────────────────────────────────────────
const tmp = new THREE.Vector3();
function tick() {
  controls.update();
  renderer.render(scene, camera);

  // Draw the world-frame gizmo as a viewport overlay in the top-right corner.
  // Mirrors the main camera's orientation but stays anchored — pan/zoom
  // does not move it. Uses autoClear=false + scissor to keep the rest of
  // the viewport intact.
  const w = renderer.domElement.width  || renderer.domElement.clientWidth;
  const h = renderer.domElement.height || renderer.domElement.clientHeight;
  const dpr = renderer.getPixelRatio();
  const px = gizmoSize * dpr;
  gizmoCam.position.copy(camera.position)
    .sub(controls.target).normalize().multiplyScalar(3);
  gizmoCam.up.copy(camera.up);
  gizmoCam.lookAt(0, 0, 0);
  const prevAutoClear = renderer.autoClear;
  renderer.autoClear = false;
  renderer.clearDepth();
  renderer.setScissorTest(true);
  renderer.setScissor(w - px - 4 * dpr, h - px - 4 * dpr, px, px);
  renderer.setViewport(w - px - 4 * dpr, h - px - 4 * dpr, px, px);
  renderer.render(gizmoScene, gizmoCam);
  renderer.setScissorTest(false);
  renderer.setViewport(0, 0, w, h);
  renderer.autoClear = prevAutoClear;

  for (const { link, el } of linkLabels) {
    link.getWorldPosition(tmp);
    tmp.project(camera);
    const x = (tmp.x * 0.5 + 0.5) * stage.clientWidth;
    const y = (1 - (tmp.y * 0.5 + 0.5)) * stage.clientHeight;
    const inFront = tmp.z < 1;
    el.style.transform = `translate(${x}px, ${y - 14}px) translate(-50%, -100%)`;
    el.style.opacity   = inFront ? '0.9' : '0';
  }
  requestAnimationFrame(tick);
}
tick();

// ── Workspace picker ─────────────────────────────────────────────
async function pickWorkspace() {
  wsErr.innerHTML = '';
  if (!window.showDirectoryPicker) {
    showError('This browser does not support the File System Access API. Use Edge or Chrome.');
    return;
  }
  let root;
  try {
    root = await window.showDirectoryPicker({ id: 'sw2gz-ws', mode: 'read' });
  } catch (e) {
    // User cancelled — silent.
    return;
  }
  setStatus('scanning workspace…');
  fileMap.clear();
  try {
    await walkDir(root, '');
  } catch (e) {
    showError(`Walk failed: ${e.message}`);
    return;
  }
  const pkgs = findPackages();
  if (pkgs.length === 0) {
    showError(`No urdf/*.urdf.xacro found under "${root.name}".`);
    return;
  }
  if (pkgs.length === 1) {
    pkgSelect.style.display = 'none';
    await loadPackage(pkgs[0]);
    return;
  }
  // Multiple packages — present dropdown.
  pkgSelect.innerHTML = '';
  for (const p of pkgs) {
    const opt = document.createElement('option');
    opt.value = p.root;
    opt.textContent = `${p.name}  (${p.root})`;
    pkgSelect.appendChild(opt);
  }
  pkgSelect.style.display = 'block';
  pkgSelect.onchange = () => {
    const chosen = pkgs.find(p => p.root === pkgSelect.value);
    if (chosen) loadPackage(chosen);
  };
  await loadPackage(pkgs[0]);
  pkgSelect.value = pkgs[0].root;
}

openBtn.addEventListener('click', pickWorkspace);
openBtn2.addEventListener('click', pickWorkspace);

// HTTP-fallback load — for headless/preview-server usage where no user gesture
// is available to invoke showDirectoryPicker. Synthesizes fileMap entries that
// fetch from the standalone serve.ps1 HTTP server (routes /urdf/* and /meshes/*
// are exposed there). Triggered by ?serve=<pkgName> in the URL.
async function loadFromServer(pkgName) {
  wsErr.innerHTML = '';
  const origGet = fileMap.get.bind(fileMap);
  fileMap.get = (key) => {
    const direct = origGet(key);
    if (direct) return direct;
    // Handle both single-slash ("pkg/meshes/foo") and double-slash ("pkg//meshes/foo")
    // — URDFLoader concatenates packageBase + relPath which can produce either.
    const m = String(key).match(/^[^/]*\/+(meshes|urdf|worlds|config|launch)\/(.+)$/);
    if (!m) return undefined;
    return {
      getFile: async () => {
        const r = await fetch(`/${m[1]}/${m[2]}`);
        if (!r.ok) throw new Error(`fetch ${r.url} -> ${r.status}`);
        const b = await r.blob();
        return new File([b], m[2]);
      },
    };
  };
  await loadPackage({
    name: pkgName,
    root: pkgName,
    xacro: `${pkgName}/urdf/${pkgName}.urdf.xacro`,
  });
}
window.sw2gzLoadFromServer = loadFromServer;
const _serveParam = new URLSearchParams(window.location.search).get('serve');
if (_serveParam) {
  loadFromServer(_serveParam).catch(e => showError(`Server load failed: ${e.message}`));
}
