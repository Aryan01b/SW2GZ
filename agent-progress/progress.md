# Progress

Current: world mode + asset mode shipped on `main` (trunk). Tests: **785 green**.
Branch model reconciled: `main` is trunk, `v2.1.0`/`v2.2.0` are tags.
Addin compiles clean (SW closed for MSBuild; regasm MSB3216 is non-fatal).

## Done (World Settings panel — scene/environment prefs) — v2.5.0

New "World Settings" ribbon button (World tab) → modal WinForms dialog editing
all scene/environment knobs; persisted per-doc; emitted by the world writer.
Groups: View (camera iso/top/front + grid), Lighting (sun az/el/intensity +
shadows), Sky & fog (+ background RGB), Environment (gravity + wind), Geo
(spherical coords). Pure-writer + config; UI is a plain Form (no PMP re-entrancy).

- `SdfSceneSettings` (pure record, `Gz/`) + `SdfPhysicsBlock.Sun(az,el,int,shadows)`;
  `SdfWorldWriter.WriteScene` gained `Settings` param → emits `<gravity>/<wind>/
  <spherical_coordinates>`, `<scene>` grid/shadows/sky/fog/background, parametric
  sun. **Null Settings = legacy byte-identical** (robot path + old tests safe).
- `Sw2gzWorldSceneConfig` ([DataContract], `URDFExport/`) holds the persisted
  knobs + `InitialView`; lives on `Sw2gzDoc.World.Scene` (POCO DataContract
  round-trips it; `OnDeserializing` reseeds so legacy docs never load null).
  `Bridge` copies it to `cfg.WorldScene` + `cfg.WorldInitialView`; exporter maps
  `ToSceneSettings()` into the scene.
- `WorldSettingsDialog` (WinForms, `#if SW_INTEROP`) seeded from / `ApplyTo` the
  scene config. Ribbon: repurposed the unplaced `WorldScene` cmd (id 33) →
  label "World Settings", callback `OpenWorldSettings`, added to
  `WorldClusterUserIds` so it shows only in World mode. `SwAddin.OpenWorldSettings`
  loads doc → dialog → `PersistDoc` on Save.
- +9 tests (793 green): scene-settings emit, parametric sun, doc round-trip,
  legacy-doc defaults. Dialog is theme-aware (dark/light, dark title bar via
  DWMWA_USE_IMMERSIVE_DARK_MODE). Shipped: commit a438240, **tag v2.5.0**,
  pushed to origin/main.

## Next (planned, not started)

- **World sensors & actuators** — phased roadmap written:
  [`docs/superpowers/plans/2026-06-29-world-sensors-actuators.md`](../docs/superpowers/plans/2026-06-29-world-sensors-actuators.md).
  S1 (sensors on world models) + A0 (dynamic props) are the low-risk entry
  points; A1+ (articulated props → joint control → ros2_control/bridge) needs a
  product decision to move World mode beyond static review-only, plus a
  robot/world model-builder unification checkpoint first. Most writers already
  exist (sensors, ros2_control, RosGzBridgeYaml, LaunchPyWriter).

## Done (world Phase 1 — runnable, framed world)

Gz-Harmonic world-feature roadmap (4 phases) planned from the SDF-worlds docs.
Phase 1 = emit a `<gui>` block + an auto-framed initial camera so `gz sim`
opens looking at the assets (today: default origin view → scene often off-
screen). System plugins were already emitted; this fills the real gap.

- **`SdfGuiBlock.Default(SdfCamera)`** (NEW pure writer, `Gz/`): standard
  Harmonic panels — MinimalScene(+`<camera_pose>`) · GzSceneManager ·
  InteractiveViewControl · WorldControl(start_paused) · WorldStats · EntityTree.
- **`SdfCamera` record** + `SdfSceneInput.Camera` (null → no `<gui>`, keeps
  robot/asset goldens intact). `WriteScene` emits the gui after `<scene>`.
- **`Sw2gzWorldExporter.FramingCamera`** — reuses the scene AABB (refactored
  `ComputeBounds`); frames target at mid-height above the reframed XY origin,
  stand-off ∝ scene size. `WorldInitialView` config (`iso`|`top`|`front`,
  default iso) picks the direction. Camera emitted in ROS (Z-up) world frame.
- Backend only this cut — auto-iso works with ZERO new UI. The wizard
  *Initial view* combo (Scene step) is the only remaining UI piece, DEFERRED.
- +6 tests (785 green). Addin compiles clean. **Not yet deployed / live-tested.**

## Done (asset mode in PART documents)
Asset mode now works on a standalone `.SLDPRT` (not just components in an
assembly). Sub-assembly/part component picks in an assembly already worked
(tessellator recursion).
- `SolidWorksMeshTessellator(swApp, PartDoc)` ctor + `TessellatePart()` — unions
  the part's own solid bodies (no component/assembly transform), colour from the
  part material.
- `RunCore` + `RunAssetPreview` detect `swDocPART` → build the part tessellator,
  force Asset, route to `Sw2gzAssetExporter`.
- `Sw2gzCreateAssetPmp` whole-part mode (`wholePartName` ctor arg): Part step
  becomes an info label (no picker); BodyPart preset.
- SwAddin: `ActivePartOrAssembly` + `TryGetActiveModelDoc` (popup-free enable
  `AssetCreateEnable`; Preview/Export enables allow parts). `OpenCreatePmp`
  forces Asset + whole-part wizard for part docs. `LaunchPreview`/`LaunchExport`
  accept parts.
- Ribbon: `BuildPartTab` adds a `swDocPART` tab = [Create Asset · Preview ·
  Export] (no pills/clusters — Robot/World are assembly-only).
- 779 green (COM part path not unit-covered). **Re-test live; may need a full SW
  restart for the new part-doc tab to register.**

## Done (asset mode — single part → reusable Gz model)
Export one part with its SW colour as a drop-in Gz model (`model://`). Mirrors
the proven world pattern (no glitches).
- `SdfAssetModelWriter` (pure): standalone `<sdf><model><static><link>` w/ mesh
  visual+`<material>`(part colour) + collision+friction; inertial only if dynamic.
- `Sw2gzAssetExporter` (COM-free): tessellate part → bake SW→ROS rotation (Z-up)
  → centre XY + floor z=0 → write `<name>/{model.config, model.sdf, meshes/
  <name>.dae}` (smooth normals + colour). Wired via `Sw2gzExportConfig.Asset*` +
  `Bridge` + `RunCore` branch on `ExportMode.SdfModel`.
- `Sw2gzCreateAssetPmp` rebuilt on the WinForms nav-bar pattern (Part → Surface
  → Review), no PMP-button re-entrancy. Asset preview via
  `Sw2gzModelPreviewer.RunAssetPreview`. Asset cluster buttons removed.
- **Re-test live in SW.**

## Done (world mode — 2nd attempt, this session)

Re-implemented world mode (assembly → Gz Harmonic world) after the 1st attempt
was reset. Scope (user-locked): pick ground (room/floor; none → default flat
ground plane) → auto-locate every other top-level component as a **static**
asset, positioned same as SW. Export → one self-contained `<pkg>/<pkg>.sdf` +
`<pkg>/meshes/*.dae`. No joints/actuation/launch/ament. Review-only.

- **Revised SDF structure** — `SdfWorldWriter.WriteScene(SdfSceneInput)`: inlined
  `<model><static>true</static>` per component, `<visual>`+`<collision>` share
  the same `meshes/<name>.dae`. Default `ground_plane` ONLY when no ground
  picked. Whole-scene SW→ROS rpy rides each model's `<pose>` (placement baked
  into mesh verts → position 0 0 0). Replaces the old `<include>model://` +
  unconditional ground_plane shape. `SdfPhysicsBlock.Default(engine,step,rtf)`
  overload (old no-arg kept byte-identical for robot golden).
- **Config threading** (the seam the 1st attempt failed at): `Sw2gzExportConfig`
  gains flat `WorldGround/WorldAssets/WorldPhysicsEngine/WorldMaxStepSize/
  WorldRealTimeFactor` DataMembers + OnDeserializing defaults + clone copy;
  `Sw2gzDocToExportConfig.Bridge` now copies `doc.World` through.
- **`Sw2gzWorldExporter`** (COM-free, takes `IMeshTessellator`): tessellate each
  pick (assembly-frame, baked) → `meshes/<name>.dae` → `WriteScene`. Per-
  component try/catch skips an un-tessellatable comp with a Warning (sub-asm
  bodies = known tessellator ceiling). `Sw2gzModelExporter.RunCore` branches to
  it on `ExportMode.SdfWorld` before the robot pipeline.
- **Wizard UI** (`Sw2gzCreateWorldPmp`): setting ground auto-seeds Assets =
  top-level comps minus ground (editable list, only seeds when empty). Step
  descriptions updated.
- Tests: +14 (9 `WriteScene` + 5 `Sw2gzWorldExporterTests` via fake tessellator).
- Deployed to `C:\Program Files\SW2GZ\`. Open assumption: Gz resolves the
  relative `meshes/<name>.dae` URI vs the .sdf dir (unverified in real Gz).

### Visual-quality improvements (4th live round — "make it smooth")
- **Smooth shading** — new pure `MeshNormals.ComputeSmooth(mesh, creaseDeg=35)`:
  welds vertex normals across coincident positions within a crease angle, so
  curved CAD surfaces shade smoothly while hard edges stay sharp. `DaeWriter`
  uses it on the `withNormals` (world) path; robot path (off) byte-identical.
  Flows to Preview too (viewer keeps DAE normals). Tested (MeshNormalsTests).
- **Multi-body parts** — `SolidWorksMeshTessellator` now tessellates + unions
  EVERY solid body of a component (was `bodyObjs[0]` only → silently dropped
  bodies 2+).
- **Component-level color** — tessellator prefers `Component2.GetMaterialProperty
  Values2` (assembly instance appearance override) over the part's own material,
  with safe fallback. Matches how users colour an environment in-assembly.
- **Sub-assembly recursion** — `SolidWorksMeshTessellator.CollectBodyComponents`
  walks `Component2.GetChildren()` recursively: a sub-assembly asset now
  tessellates its descendant part leaves (each baking its own assembly-frame
  `Transform2`) instead of being skipped. Color fallback reads the first leaf's
  part material. Defensive try/catch per branch so one bad child can't sink it.
  ASSUMPTION (verify live): nested `Component2.Transform2` is top-assembly-frame
  (global) — if a sub-asm asset lands mis-placed, that assumption is wrong and
  transforms need composing down the chain.
- 773 tests green (COM tessellator path is not unit-covered — live-test it).
  **Still DEFERRED:** per-body/per-face colors (multi-submesh), textures/UV/PBR.

### Ribbon/doc state-sync fixes (3rd live round)
Reported: reopen a saved World assembly → ribbon shows "Create Robot" + pills
disabled (mismatch). Root: `_activeMode` defaults Robot and nothing synced it
from the persisted attribute; the in-memory store is blank on fresh launch.
- **Sync ribbon to active doc** — `SwAddin.SyncRibbonToActiveDoc` (called from
  `OnDocChange` + `FileOpenPostNotify`) loads the persisted doc, seeds the store
  (`Sw2gzDocStore.Put`), and `RefreshTabForMode(mode, saved)`. Registrar gained
  `ActiveMode`/`ActiveSaved` getters + `RefreshTabForMode(mode, saved)`.
- **Create ↔ Edit label** — 3 new commands `ModeEdit{Robot,World,Asset}` (17/18/
  19); `BuildModeStartBox` picks Create vs Edit by saved-state. `PersistDoc`
  flips to "Edit <Mode>" after Finish; `SetMode` stays "Create".
- **`OpenCreatePmp` loads saved doc** — was reading the blank store → reopened
  Robot wizard for a saved World. Now Load-first + `Put` when `HasSaved`.
- **Mode-specific attribute name** — `Sw2gzDocSerialization` saves
  `SW2GZ <Mode> (v1)` (tree shows the mode); HasSaved/Load/Delete scan all known
  names incl. legacy `SW2GZ Doc (v1)`. Re-saving migrates an old doc's name.
- Compiles clean, 767 tests green. **Re-test live in SW.**
  Remaining mismatch candidates (not yet touched): World/Asset cluster ribbon
  buttons (Ground/Assets/Physics/Scene, Body/Surface) still open stub PMPs;
  `DerivePackageNameFromAssembly` default is "robot_preview" even in World mode.

### World export + preview fixes (2nd live round)
- **Plain white / unlit in Gz** — DAE had diffuse color but NO normals
  (`NeedVertexNormal=false`) → Gz can't light it. Added opt-in `DaeWriter.Write(
  mesh, path, withNormals)` (area-weighted vertex normals); default false keeps
  robot goldens byte-identical, world exporter passes true.
- **World off-camera in Gz** — assembly modeled far from origin → baked verts
  land the scene off the default camera. `Sw2gzWorldExporter` now recenters all
  meshes about their combined AABB center before writing (rotation rides the
  `<pose>` rpy about origin, so centered stays centered).
- **No world preview** — `Sw2gzModelPreviewer.RunWorldPreview` runs the world
  export to temp + synthesizes a throwaway URDF (base_link + one fixed link per
  mesh) so the EXISTING robot three.js viewer renders it unchanged (viewer
  neutralizes materials anyway). `SwAddin.LaunchPreview` branches on
  `doc.Mode==World` (gate: ground or assets, not robot links). Tests: 767 green.

### World wizard fixes (post-deploy live bugs)
- **Create World opened Robot wizard** — `SwAddin.OpenCreatePmp` reset the doc
  to `Mode=Robot` whenever nothing was saved yet (the mode pills only mutate the
  in-memory doc, never persist), wiping the World pick. Fix: preserve `doc.Mode`
  across the `Sw2gzDocStore.Reset`.
- **Buttons vanished on double-click Clear; multi-select Add glitched; nav theme
  off** — root cause: PMP `swControlType_Button` controls + mutating PMP state in
  `OnButtonPress` corrupts SW's PMP renderer. Ported `Sw2gzCreateWorldPmp` to the
  Robot wizard's chrome: WinForms nav bar (Back/Next + step indicator, dark
  theme) deferred via `BeginInvoke`, and WinForms action-button bars (Set ground
  / Clear / Add / Remove / Clear all) via `WindowFromHandle`. No PMP buttons or
  footer group left. **Re-test live in SW.**

## Done (preview frame-migration + UX — latest session)

Goal: "as I see the model in SW (Y-up frame), in preview I see it in ROS2
Z-up frame; joint pose + axis correct." All committed + pushed to
`origin/v2.1.0`, addin reinstalled.

- **SW→ROS rotation now baked into preview URDF** (`137bfb7`). Root cause:
  preview served the on-disk URDF, where the SW→ROS rotation rides on the
  `gz_sim.launch.py` spawn args (REP-105 default, `EmitWorldLink=false`).
  Browser can't run the launch file → a default Y-up assembly rendered
  tilted 90° in the Z-up viewport (joints/axes/positions all looked
  rotated though the model was correct). Fix: `Sw2gzExportConfig.WithEmitWorldLink(bool)`
  shallow-clone helper; `Sw2gzModelPreviewer.RunPreview` forces
  `EmitWorldLink=true` for the **preview temp workspace only** — real
  exports still honour the user's saved setting. Rotation now emits as a
  `world` link + `world_to_<root>` fixed joint the browser renders.
  (`SwToRosRotation.Build` for default Y-up/Z-fwd → rpy=(π/2, 0, π/2),
  verified by hand.)
- **Joint-limit baseline shift** (`c743aac`). Bug: URDF joint origin baked
  the SW *current* pose but limits stayed raw-SW → sliding drove the child
  past its real range, links wouldn't sustain position. Fix: `PoseMath.TwistAngle`
  (swing-twist decomp, signed rot about axis) + `PoseMath.SlideDistance`
  (signed projection); `Sw2gzPipeline` subtracts that `limitShift` from
  lower/upper so URDF joint=0 ≡ SW current pose. Verified full_arm joint-1:
  twist=-1.464562 → URDF lower=0, upper=π.
- **Slider snap-back fixed** (`1b6a791`). Dragging a slider now auto-disables
  the Live toggle (`pollJoints` was re-overwriting the manual pose after the
  grace timeout). HUD shows "manual pose — Live paused".
- **Mesh-centroid markers** (`fec966f`). Explains the URDF link-frame
  convention visually: RGB triad = link frame (joint pivot, what URDF uses);
  new grey ◯ dot = mesh AABB center (where the body actually is); grey line
  triad→dot = the `<visual><origin>` offset. `seedCentroidMarkers()` +
  `recomputeCentroids()` + `◯ mesh` HUD toggle (default on). Answers the
  "tf frame pose not mapped to link mesh" question — the offset is correct,
  now just made visible.
- New tests: `PoseMathTwistAngleTests` (22), `Sw2gzExportConfigCloneTests` (5).
- Mockups/plan added (`866a000`): `docs/ui-mockups/preview/` (5 layout demos
  + joint-mate reference) + `docs/superpowers/plans/2026-06-09-joint-mate-full-coverage.md`
  (4-phase plan, **not yet implemented — deferred**).

### Deferred / offered-but-not-selected (do NOT start without user pick)
- Option B: base_link origin override in the Create-Robot wizard.
- Option C: per-link frame re-anchor.
- Joint-mate full-coverage Phases 1–4 (plan written, "plan to implement").

### Key files (preview)
- `SW2GZ/UI/PreviewWeb/index.html` — canonical preview; copies to
  `bin/.../preview/`, ships via installer. Option-D layout (icon rail +
  drawer + HUD toggle strip).
- `SW2GZ/URDFExport/Sw2gzModelPreviewer.cs` — forces EmitWorldLink for preview.
- `SW2GZ/URDFExport/Sw2gzExportConfig.cs` — `WithEmitWorldLink` clone.
- `SW2GZ/Math/PoseMath.cs` — `TwistAngle` / `SlideDistance`.
- `SW2GZ/URDFExport/Sw2gzPipeline.cs` — limit-shift emission.
- `SW2GZ/SW/SwAddin.cs` — `PreviewEnable`/`LaunchPreview` gate on saved
  doc-v1 (`Sw2gzDocSerialization.HasSaved`, load-from-attribute first).

## Done (post-shell session — preview + joint-type fixes)

- `AutoJointResolver`: per-mate-type dispatch in `Resolve`. ANGLE mates
  derive axis from cross-product of the two picked planar face normals;
  DISTANCE uses the parent face normal; LOCK / unknown stays Fixed. Adds
  `TryExtractPlane` planar-face math mirroring `TryExtractCylinder`. Was
  silently demoting all non-cylindrical mates to Fixed → URDF showed
  every joint as Fixed regardless of physical type.
- `Sw2gzCreateRobotPmp` crash fix: PMP COM `_hdrLabel.Caption =` setter
  was hard-crashing SW (mscorlib AccessViolation) when called from a
  WinForms Next/Back-button click handler — PMP re-entrancy. Replaced
  with an in-`_navBar` WinForms `Label` (`_stepIndicator`); button
  clicks now `BeginInvoke` GoNext/GoBack onto the next message-loop
  tick to escape click-handler reentrancy. Wizard walks all steps
  through to Finish.
- Preview panel (PreviewDialog → PreviewServer → preview/index.html):
  - 320-px sidebar listing every link (mass + COM + inertia) and joint
    (parent → child, type badge, xyz, rpy, axis, limits, effort, vel).
  - Per-link TF triads, per-joint axis arrow, world corner gizmo,
    inertial COM spheres, floating link-name labels.
  - Slider per movable joint pulls/poses live; live SW /joint_states
    poll mirrors the slider when the user isn't dragging.
  - Three runtime fixes burned in: (a) `three/examples/jsm/` import-map
    alias missing → URDFLoader 0.12.x silently failed to load STLLoader;
    (b) SW Collada DAE materials are near-black → override with neutral
    `MeshStandardMaterial` on load; (c) `fitCamera` raced async mesh
    loads → re-fit at 250/800/2000 ms and bound only `isMesh` children.
  - Installer ships `{app}\preview\*` so PreviewServer can find
    `index.html`. No additional setup beyond the addin install — uses
    the default Windows browser (Edge) + .NET `HttpListener`.
  - Fully offline: `scripts/FetchPreviewVendor.ps1` pins three.js
    0.160 + urdf-loader 0.12.7 under `UI/PreviewWeb/vendor/` (~1.4 MB,
    7 files). csproj `<Content Include="UI\PreviewWeb\vendor\**\*">`
    mirrors the tree to `bin/<cfg>/preview/vendor/`; installer's
    `recursesubdirs` ships it. Importmap resolves `three`,
    `three/examples/jsm/`, `urdf-loader` to `./vendor/...` — zero
    network calls at runtime. PreviewServer + standalone serve.ps1
    grew a hardened `/vendor/*` route (path-traversal guarded).

## Done (v2.1.0 UI shell — this plan)

- Sw2gzDoc in-memory tree (Robot/World/Asset subtrees).
- Sw2gzDocSnapshot deep-clone + restore (PMP cancel rollback).
- Sw2gzDocStore per-document in-memory cache.
- ClusterVisibility pure helper (mode → cluster visibility).
- RibbonCommandIds — central layout.
- Sw2gzStubPmp generic shell PMP.
- Sw2gzRibbonRegistrar — 4-cluster ribbon build.
- SwAddin — 18 panel callbacks + mode pills + cluster enable gating.
- Common.Preview routed to existing PreviewDialog.
- Common.Export routed to existing ExportDialog.
- Sw2gzExportPmp linear wizard deleted.
- Mode flyout redesign: face-only "Create [Mode]" button + 3 TextHorizontal pills (active pill grayed) replacing the chevron-based mode picker. Demo Split throwaway removed.
- Ribbon polish (v2.1.0 follow-up):
  - Coord ribbon button removed (advanced coord moves into Create wizard).
  - Create button label is now mode-specific: "Create Robot" / "Create World" / "Create Asset" (3 pre-registered commands, swapped via box rebuild — SW SDK can't rename a command post-Activate).
  - Mode switch no longer steals the active ribbon tab. Replaced full `RemoveCommandTab` + `AddCommandTab` with surgical `CommandTab.RemoveCommandTabBox` + `AddCommandTabBox` per box; the tab itself stays so user keeps their Assembly/Layout/etc. focus.
  - Common cluster split into two adjacent boxes — [Create + pills] | [Preview + Export] — using SW's inter-box gap as the group separator (no AddSeparator API on ICommandTabBox).
- Create-* multi-step PMP wizards (replaces the v2.1.0 generic stub for the Create button):
  - `Sw2gzCreateRobotPmp` — 3 steps Links → Joints → Review. Auto-seeds Robot.Links from top-level components on first open; mate enumeration walks the FeatureManager `MateGroup` sub-features; Add / Remove / Clear / Reseed buttons per list.
  - `Sw2gzCreateWorldPmp` — 4 steps Scene → Assets → Physics → Review. Ground SelectionBox + Assets multi-pick + Engine combo (ode/bullet/dart) + step/RTF numberboxes (defaults 0.001s / 1.0).
  - `Sw2gzCreateAssetPmp` — 3 steps Body → Surface → Review. Body SelectionBox + Static checkbox + Friction μ numberbox (default 0.8).
  - All three share the persistent footer-group Back / Next pattern (Next caption → "Finish" on last step), per-step group.Visible toggle, Cancel → Sw2gzDocSnapshot.Restore.
  - Two gotchas burned in: (a) `internal` ComVisible classes are NOT exposed via CCW — Stub had silently been throwing InvalidCastException at CreatePropertyManagerPage's handler param since June 6; all PMP classes are now `public sealed`. (b) AddGroupBox needs `swGroupBoxOptions_Visible | swGroupBoxOptions_Expanded` — passing 0 renders an empty collapsed shell.
  - Live-verified in SOLIDWORKS 2025 against `FULL_ARM.SLDASM`: all 3 wizards open, walk Back/Next end-to-end, mate enumeration finds the 9 real mates, Cancel rolls back to snapshot.

## Done (inherited from v2.1.1 main work — see CHANGELOG)

- Pipeline preflight + atomic stage/swap + per-run log.
- ExportDialog cross-assembly defaults.
- Per-link anchors + mesh union + REP-105 root.
- Browser-based three.js preview with live SW joint sync.
- SW→ROS coord rotation primitives (used internally; UI moved to advanced-only).

## Done (full-arm joint-frame diagnostic plumbing — this plan)

- Per-export pose dump (`<ws>/sw2gz_pose_dump.dbg.txt`): link anchors,
  joint origins/rpy, raw `Component2.Transform2.ArrayData` for each first
  part, and the joint axis BEFORE + AFTER child-frame re-expression.
- `IComponentRawTransformSource` side channel; implemented by
  `WizardAssemblyWalker`.
- `InertialAggregator.Combine(parts, linkAnchor)` overload: rebases COM +
  inertia into link-local frame so URDF `<inertial>` is correct for
  multi-part links. Pipeline now passes the link anchor; single-part
  case round-trips back to the part-local COM (byte-identical for
  the current 3R_ARM URDF).

## Done (Reference-CS joint-origin port from upstream — this plan)

- D1: Create-Robot PMP no longer auto-seeds Robot.Links on every open;
  loads the saved tree verbatim and only seeds an empty `base_link` on
  truly-fresh docs. Sw2gzDocCodec round-trip test guards A→B→C survival.
- D2: JointDef gains `RefCsName` + `RefAxisName` DataMembers. Legacy
  payloads without the fields deserialize with empty-string defaults
  via [OnDeserialized] hook.
- D3: SwJointPoseReader ports upstream `GetCoordinateSystemTransform` /
  `GetRefAxis` / `LocalizeJoint`. WizardAssemblyWalker.WalkMates resolves
  RefCs on the child component (via `componentModel.Extension.GetCoordinateSystemTransformByName`
  ⨯ `Component2.Transform2`) and localises against the parent joint's
  RefCs. Sw2gzPipeline uses MateSpec.Origin verbatim when non-Identity.
  MathTransformPose helper extracted from WizardAssemblyWalker; pure-C#
  SwJointPoseMath.Localize covered by source-linked tests.
- D4: Create-Robot Joints step gains a per-joint Reference Coord System
  + Reference Axis combobox pair, populated by SwRefGeometryEnumerator
  (FeatureManager walk filtered by GetTypeName2() == "CoordSys"/"RefAxis").
  Mate-driven fallback retained when both fields empty.

Test count: 688 → 700. (now 751 after preview-session tests.)

## Done (AutoJointResolver auto-detect wiring — this plan)

- D1: AutoJointResolver walks the MateGroup, classifies the spanning
  parent/child mate (LOCK/CONCENTRIC/DISTANCE/ANGLE → Fixed/Continuous-
  or-Revolute/Prismatic/Revolute), and extracts the cylinder axis +
  point from the parent-side MateEntity's cylindrical face.
- D2: JointDef gains OriginX/Y/Z + HasOrigin DataMembers populated by
  AutoJointResolver in EnterJointsStep. WizardAssemblyWalker.WalkMates
  rewrites to ride those cached fields (MateSpec.MatePointAssembly =
  origin when HasOrigin, MateSpec.Origin stays Identity so the pipeline
  routes through JointOriginResolver.Compute(..., matePoint)). Legacy
  XMLs without the new fields default to 0/false. SolidWorksMassProperties
  + SwJointStateSampler now match on Component2.Name2 so multi-instance
  parts resolve correctly.
- D3: Joints step UI restructured. Mate listbox + Ref-CS/Ref-Axis combo
  pickers removed; a dark-theme "Re-detect" button bar sits above the
  joints listbox; detail labels show axis + origin + source mate when
  HasOrigin, "NOT DETECTED" + remediation hint otherwise.
- D4: AutoJointResolved unsealed (D1 inheritance fix) so the SW add-in
  builds; D2 legacy-XML test rewritten to synthesize via codec round-
  trip + regex strip. 714 tests green.

## Next (separate plans)

- **Backend wiring** — persist `Sw2gzDoc` into the SW Attribute, replace per-panel stubs with real fields, evolve `Sw2gzExportConfig` schema, full `RobotPipeline`/`WorldPipeline`/`AssetPipeline` split.
- **Coord auto-default** — internalise `SwToRosRotation` so users don't pick.
- **Plug-and-play stack** — fill the ros2_control + bridge + clock + RSP gap per spec §7.1.
- **Cleanup pass** — dead-code list from spec §9 (TfTreeFormatter, emitWorldLink, etc.).

## Conventions reminder

- No AI attribution.
- Legacy csproj — add new .cs files to BOTH `SW2GZ\SW2GZ.csproj` AND `Test\SW2GZ.Writers.Test.csproj` when source-linking pure files.
- Close SolidWorks before rebuild or DLL copy is blocked.
