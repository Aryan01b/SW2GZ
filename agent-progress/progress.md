# Progress

Current: **v2.1.0 UI-shell** shipped on `v2.1.0` branch. Backend wiring in next plan.

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

Test count: 688 → 700.

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
