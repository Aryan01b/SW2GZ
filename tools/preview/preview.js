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
function xacroToUrdf(text) {
  // self-closing xacro tags
  text = text.replace(/<xacro:[a-zA-Z_:]+\b[^/>]*\/>/g, '');
  // paired xacro blocks (DOTALL via [\s\S])
  text = text.replace(/<xacro:([a-zA-Z_:]+)\b[^>]*>[\s\S]*?<\/xacro:\1>/g, '');
  // xmlns:xacro on root
  text = text.replace(/\s+xmlns:xacro="[^"]*"/g, '');
  return text;
}

// ── Mesh resolution ─────────────────────────────────────────────
// urdf-loader's loadMeshCb takes a URL. We're given URLs like
// "package://full_arm/meshes/foo.dae" — strip "package://<pkg>/" and look
// the rest up in fileMap under <pkgRoot>/.
async function blobForMesh(packageRoot, urdfPath) {
  // urdfPath looks like package://full_arm/meshes/foo.dae (URDFLoader has
  // already resolved it against loader.packages — when we set packages to
  // {} it passes through). Normalize.
  let rel = urdfPath;
  rel = rel.replace(/^package:\/\/[^/]+\//, '');
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

  // Read + strip xacro.
  const xacroFile  = await fileMap.get(pkg.xacro).getFile();
  const xacroText  = await xacroFile.text();
  const urdfText   = xacroToUrdf(xacroText);

  // Configure URDFLoader.
  const loader = new URDFLoader();
  loader.packages = {};   // no remapping; mesh URLs come in as package://...
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

  // Joint sliders.
  const moveTypes = new Set(['revolute', 'continuous', 'prismatic']);
  const jointEntries = Object.entries(robot.joints).filter(([, j]) => moveTypes.has(j.jointType));
  for (const [name, joint] of jointEntries) {
    const lower = Number.isFinite(joint.limit?.lower) ? joint.limit.lower : -Math.PI;
    const upper = Number.isFinite(joint.limit?.upper) ? joint.limit.upper :  Math.PI;
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
