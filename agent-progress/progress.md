# Progress

Current: `feat/robot-mode-enhancements` fast-forward merged into `main`
(2026-07-05) — Robot mode v3 (link/joint modeling, SW→ROS/Gz frame
conversion, real COM in `<inertial><origin>`, read-only axis display) is
now on trunk. Both branches point at `a314319`. Tests: 501 green on
`main`. Not yet re-tagged past v2.7.0 (v2.7.0 was cut before this merge's
last two commits — inertia fix + axis-display cleanup).
Branch model: `main` is trunk; tags v2.1.0/v2.2.0/v2.3.1/v2.4.0/v2.5.0/v2.6.0/v2.7.0
all on `main` now. `feat/robot-mode-enhancements` kept alive on origin (not
deleted) in case work continues there — currently identical to `main`.
Addin compiles clean (SW closed for MSBuild; regasm MSB3216 is non-fatal).

## Done — Robot mode: SW→ROS/Gz frame conversion always applied (branch `feat/robot-mode-enhancements`)

**Real bug fixed:** Robot/URDF exports shipped in raw SolidWorks frame by
default — `Sw2gzExportConfig.EmitWorldLink` defaulted `false` with no UI to
flip it, so the already-computed `SwToRosRotation` was silently discarded.
World mode (per-model `<pose>`) and Asset mode (baked into mesh verts) were
already unconditional and correct; only Robot mode had this gap.

**First attempt was wrong, caught by user live-checking the preview
(commit reverted, see reflog / commit `0a2ccac`):** tried baking the
rotation directly into `base_link`'s own mesh reference (`Matrix3.Identity`
→ `swToRos.Transpose()`), reasoning from a true-but-misapplied proof that
every OTHER per-link quantity is invariant to a uniform global rotation.
The bug: rotating a link's MESH doesn't rotate its FRAME — and every joint
downstream is computed relative to that (unrotated) frame. Result: only
`base_link`'s body visually reoriented; `mid_link`/`end_link` stayed in
native SW orientation and didn't follow along — an inconsistent robot.
User caught this by generating a real export and looking at it, exactly
the "live-test before trusting a coordinate-math change" lesson this
codebase has already paid for twice (`sw-mathtransform-column-major`,
the mate-classification bug).

**Correct fix:** the pre-existing `world_to_<root>` wrapper-joint mechanism
was right all along — a fixed joint's rpy actually rotates `base_link`'s
real FRAME relative to world, so every descendant inherits the rotation
through ordinary FK composition. It just needed to be unconditional instead
of gated behind `EmitWorldLink` (which had no UI and defaulted off). Removed
`EmitWorldLink` + its clone helper entirely; the world link/joint is now
always emitted, `Sw2gzModelPreviewer.RunPreview` needs no override anymore.
**500 green** (505 baseline − 5 clone tests for the removed helper), addin
compiles clean, DLL + preview assets deployed.

**Verified via a synthetic export** (3-link arm, real `Sw2gzRobotExporter`
code path, throwaway test-generated fixture — no live SW available this
session): `world_to_base_link` now carries `rpy="1.570796 0 1.570796"`
(matches `SwToRosRotation.Build(PlusY, PlusZ)`), and the whole chain hangs
correctly off it as one rigid rotation. **Still needs a real live SW
re-test** on an actual assembly (FULL_ARM or similar) before trusting this
fully — the synthetic check proves the mechanism, not the live SW→ROS
axis mapping against a real assembly's up/forward convention.

## Done — Robot mode: real COM written to `<inertial><origin>` (branch `feat/robot-mode-enhancements`, 2026-07-05)

**Real bug fixed:** `WriteUrdf` hardcoded every link's `<inertial><origin>`
to `"0 0 0"` even though the mass pipeline (`SolidWorksMassProperties` →
`InertialAggregator.Combine`) already computed a real, correctly-rebased
COM in the link's own local frame — it was just discarded at the last step
(`Sw2gzRobotExporter.cs`, was ~line 409-417). The inertia tensor itself was
always correct (computed about the true COM); pinning the paired `<origin>`
to zero made physics engines read it as "about the link origin" instead —
wrong dynamics for any off-center mass.

**Fix:** write `mp.ComLocal` instead of the hardcoded zero. No pipeline to
build — `IMassProperties`/`InertialAggregator` were already correct and
already wired in; this was purely a discarded-value bug. Visual/collision
`<origin>` (mesh placement) is untouched — only the `<inertial>` block
changes. Added `Export_WritesRealComOffset_NotHardcodedToOrigin` asserting
an off-center mass produces a non-zero URDF origin and that mesh placement
doesn't move. **501 green.** Verified against a regenerated synthetic demo
export (3-link arm, off-center COM per link) — `<inertial><origin>` now
shows the real offsets (`0.05 0 0`, `0.2 0 0`, `0.15 0 0`), visual/collision
origins stay `0 0 0`, joint origins unaffected.

**Left out of scope (flagged, not fixed):** `SetCoordinateSystem`/
`AddBodies` (legacy per-body-subset mass API, marked [legacy] in
`docs/reference/solidworks-api.md`) — only matters if a link is ever
defined as a subset of bodies within one multi-body part rather than whole
components, which Robot mode doesn't currently do.

## Done — Robot mode v3: Joints step (type/axis/limit) shipped + live-tested, MILESTONE (branch `feat/robot-mode-v3`, 2026-07-04)

**User-confirmed working on FULL_ARM.SLDASM: "link is modeled properly."**
Both link positioning (base_link/link_1/end_link chain, correct relative
pose down the whole tree) and joint pivot (axis + rotation point) are
accurate. Two-phase build this session, phase 2 pivoted architecture mid-
stream after 3 straight live-test failures on the automatic approach:

**Phase 1 (shipped, stable):** manual Type/Axis/Limit editing in a new
Joints step — one row per non-root link, `JointDefReconciler` merge-
preserves user edits across Links-step tree mutations. Spec/plan:
`docs/superpowers/specs/2026-07-03-robot-joint-type-panel-design.md`.

**Phase 2, attempt A (reverted in spirit, not code-deleted — superseded):**
mate-based auto-suggestion (`SwMateJointResolver` walking `MateGroup`,
`MateJointClassification` pure classifier). Three live-test rounds, each
fixing one real bug and surfacing another against FULL_ARM's real mates:
1. Mesh not rebased with the mate-pivot override → mesh/joint frame at two
   different points → fixed (`frameOrigin` single source of truth for
   mesh/mass/joint-origin).
2. Pivot axis direction right, position off → traced to trusting one
   cylinder side blindly → added (then later removed) a dual-cylinder
   cross-check.
3. `ChooseBest` picked a limited Angle mate wholesale for its limit,
   discarding a co-existing Concentric mate's exact cylinder axis for its
   approximate plane-cross-product one → fixed via a graft (then later
   removed with the rest of the geometry-guessing code).

**Phase 2, attempt B (what actually shipped):** per systematic-debugging's
"3+ fixes on one mechanism → question the architecture" — replaced mate-
geometry-guessing entirely with a **manual axis/pivot pick**. Spec:
`docs/superpowers/specs/2026-07-03-manual-axis-pivot-pick-design.md`.
- Type + Limit **stay automatic** from mate classification (never the
  source of any of the 3 bugs above) — `MateJointClassification` shrunk to
  a pure `(mateType, limit) → (Type, Limit)` mapping, no geometry at all.
- Axis + pivot are now a **Selectionbox pick** in the Joints step: click a
  cylindrical face (axis = cylinder axis) or straight edge (axis =
  endpoint direction) — `SwMateJointResolver.TryExtractAxisFromSelection`,
  owning component resolved via `ISelectionMgr.GetSelectedObjectsComponent3`.
  Accepts up to 2 picks (parent's + child's hole) for a revolute/
  continuous joint, cross-checking the two axes actually coincide.
- Net -653 lines in the mate-geometry commit alone (deleted CylinderPair/
  PlanePair/agreement-check/ChooseBest-graft/pivot-source-combo).

**Two more real bugs found + fixed during this same live-test cycle:**
- **Axis lost when switching joint rows.** `CommitSelectedJointFromControls`
  re-read the axis numberboxes at listbox-switch time; that read proved
  unreliable across switches (PMP control-timing quirk). Fixed: axis now
  writes straight to the `JointDef` the instant it changes
  (`OnNumberboxChanged` → `HandleAxisNumberboxChanged`), not deferred.
- **Grandchild link mispositioned** (`end_link` rendered on top of the
  `base_link` joint instead of its own location). Root cause:
  `Sw2gzRobotExporter` computed a child's joint origin relative to its
  parent's RAW pose, not the parent's own established FRAME ORIGIN (mate
  pivot if the parent has one) — those differ whenever the parent itself
  has a mate-derived pivot. Fixed: `frameOrigins` dict precomputed for
  every link up front (alongside `linkPoses`), children read the parent's
  frame origin. Regression test:
  `Export_GrandchildOrigin_IsRelativeToParentsFrameOrigin_NotParentsRawPose`.

**Also fixed, smaller UX bugs found live:**
- Mesh selection/highlight persisting across Links/Joints wizard steps.
- Ribbon Create/Edit label bug resurfaced from branch divergence (`main`
  had the fix, `feat/robot-mode-v3` branched before it existed) — cherry-
  picked, then merged `main` into this branch to stop future drift.
- Clicking empty viewport space in Links step didn't clear the tree's
  remembered selection or the Mesh box.
- Renaming an existing link silently did nothing (the "Link name" box was
  only ever wired to name the NEXT Add, never the selected link) — added
  an explicit Rename button.

**503 tests green**, addin compiles clean, deployed + live-tested
repeatedly this session (final deploy confirmed working by user).
**Pending, not yet built:** Phase2 Task 7 full manual verification
checklist walkthrough (mostly superseded by the live milestone above,
but not formally closed out as a task).

## Done — Robot mode v3: joint/link relative pose + multi-mesh links, live-tested (branch `feat/robot-mode-v3`)

**2026-07-02, closing out this session's robot-mode work.** Two pieces,
both live-tested working on `FULL_ARM.SLDASM`:

**1. Links step UI rebuilt** (`Sw2gzCreateRobotPmp.cs` + `SW2GZ.UI.LinkTreeView`):
manual mesh-picker + name box + drag-to-reparent tree (reused, not
rewritten — `LinkTreeView`/`LinkHierarchy` were pre-existing, previously
unwired). No more auto-seed; user builds the link tree explicitly, first
Add is always forced to `base_link`, every later Add's parent is whichever
tree node is currently selected. First mesh component picked for a link is
its "primary" (drives the link's whole frame) — marked `(primary)` in the
selected-info label and the tree's hover tooltip.

**2. Exporter math fixed to match: parent-relative joints + multi-mesh
union** (`Sw2gzRobotExporter.cs`, `InertialAggregator.cs`) — spec/plan:
[`docs/superpowers/specs/2026-07-02-robot-joint-relative-pose-design.md`](../docs/superpowers/specs/2026-07-02-robot-joint-relative-pose-design.md) /
[`docs/superpowers/plans/2026-07-02-robot-joint-relative-pose-plan.md`](../docs/superpowers/plans/2026-07-02-robot-joint-relative-pose-plan.md).
Built via subagent-driven-development, 6 tasks, each independently
implemented + spec-compliance reviewed + code-quality reviewed (two tasks
needed a fix-and-reverify round: Task 2 got a missing warning for
dangling parent references, Task 3 hit a real `System.Drawing.Color` vs
`SW2GZ.URDF.Color` namespace collision that broke the real add-in build
even though the test project stayed green — caught only because a
reviewer built the actual `SW2GZ.csproj`, not just `dotnet test`). A final
holistic review across all 6 tasks combined traced the "first component
defines the link's frame" invariant by hand across mesh union / joint
origin / mass rebase / UI labels and found it consistent everywhere.
- Joint origin is now relative to each link's own `ParentName`, not always
  root (`Sw2gzRobotExporter.cs` two-pass: pass 1 reads every link's own
  reference pose by name, pass 2 looks up the declared parent's pose —
  needed since a child can sit before its parent in `Robot.Links` after a
  drag-drop reparent).
- Root detected via `LinkHierarchy.Roots` (tree structure), not
  `links[0]` (list position) — needed because "Set as base link" re-roots
  by flipping `ParentName` pointers without reordering the list.
- Mesh and mass are now a union of every `ComponentIds` entry on a link
  (was silently first-component-only), both combined into the link's
  shared reference frame — `UnionMeshInLocalFrame` new helper for mesh;
  `CombineMass` + `InertialAggregator`'s new `Matrix3`-parameterized
  `Combine` overload for mass (added instead of writing a
  `Matrix3`→`Quaternion` converter — that category of new coordinate-
  conversion code has produced two real bugs in this codebase already;
  see memory `sw-mathtransform-column-major`, `robot-mode-dev`).
- Root link keeps its identity-orientation mesh convention (mesh only
  translated, not un-rotated); mass combination does NOT get the same
  treatment (rebases into the root's real pose) — documented asymmetry,
  only matters for a multi-component root with non-identity native
  rotation, not yet exercised.
- Joint TYPE still hardcoded Fixed (unchanged, separate future increment).
- 481 tests green (was 470 at session start), both `SW2GZ.csproj` and
  `Test/SW2GZ.Writers.Test.csproj` build clean, deployed, **live-tested
  by the user 2026-07-02: 3-level chain (grandchild joint relative to its
  real non-root parent, not root) + multi-mesh link (both parts positioned
  correctly, not just the first) — confirmed working.**

## Prior — Robot mode v3: Links step now uses LinkTreeView, high-risk-flagged before live test

**Swapped the flat listbox for `LinkTreeView` (2026-07-02, 4 more user fixes).**
`Sw2gzCreateRobotPmp.cs`'s Links step now embeds the pre-existing
`SW2GZ.UI.LinkTreeView` (WinForms `TreeView`, `WindowFromHandle`-embedded
like the nav/button bars) instead of the flat `PropertyManagerPageListbox` +
hand-rolled DFS renderer from the last two entries. Reused, not rewritten:
`LinkHierarchy.cs` (pure, already unit-tested) for roots/children/cycle
checks, `LinkTreeView.cs` (WinForms, already had drag-to-reparent +
F2-rename + right-click "Set as base") for the widget itself — both were
sitting unwired since the 2026-07-02 revert (see memory `robot-mode-dev`).
Changes: drag a node onto another to reparent (cycle-guarded); clicking a
node is now how you pick the next Add's parent (`_linkTree.ActiveLink`,
replacing last session's click-a-listbox-row approach) and drives a new
"Selected: name   Mesh: a, b" info label; `_linkTree.LinksChanged` triggers
`RebuildJoints()` so drag/rename/reroot edits stay joint-consistent same as
Add/Remove. Link-name box got a fake placeholder ("e.g. wheel_link",
swapped for real empty text on focus/blur via `OnGainedFocus`/
`OnLostFocus`) since PMP-native Textbox has no real cue banner. 470 green,
clean build, deployed 09:55:00.

**⚠ Explicit risk flag, not just the usual boilerplate warning:** this is
literally the same `LinkTreeView` drag-and-drop widget from the **2026-07-02
FULLY REVERTED** link-hierarchy attempt earlier this same day — it passed
every automated gate then too and still broke live in SW with no captured
symptom. `LinkTreeView.cs`/`LinkHierarchy.cs` themselves were untouched by
that attempt (only called into, never modified), so they're unchanged from
what's in git history either way — but "unchanged" isn't "proven," it was
never isolated and live-tested on its own, only ever shipped bundled with
the mesh-funnel work that also broke. This is the **smallest possible
wiring step** for that widget (just swap the list for the tree, nothing
else piled on) — do the live check on THIS step alone before adding
anything more to the Links UI. Test: open Create Robot, add base_link, add
a child, drag the child onto nothing/itself (should reject), drag a second
child onto the first child (should reparent + tree redraws), remove
base_link while it has children (should block, same as before).

**Polish pass on the manual link builder (2026-07-02, after user screenshot).**
7 UI fixes on `Sw2gzCreateRobotPmp.cs`: dropped the paragraph description;
added real `Label` controls above Mesh/Link-name/Hierarchy (captions on
Selectionbox/Textbox don't render visibly in this PMP); link name
auto-fills from a single-part mesh pick (blank for sub-assembly or
multi-pick, `OnSelectionboxListChanged` on the mesh box); tips repurposed
as hover placeholders (skipped: real WinForms cue-banner placeholder —
these are native PMP controls, not WinForms, converting them is a bigger
diff than the ask); **removed the Parent combobox** — clicking a row in the
hierarchy list now sets the parent for the next Add (`_linkRows` maps
display row -> `LinkDef`, since the tree is rendered depth-first from root
and no longer matches `Robot.Links` storage order); hierarchy list is a
real indented tree (`AppendLinkRow` recursive DFS) instead of a flat
one-level "-> parent" line. Native PMP label/control fonts/colors are
SW-theme-driven, not stylable beyond what's already dark-themed on the
WinForms button bar. 470 green, clean build, deployed 09:42:17. **Still
needs the same live SW check as the previous entry** — not yet done.

**Links step UI rework (2026-07-02).** Old flat model auto-seeded one link
per top-level component, all Fixed to link[0]. New model, per user spec:
no auto-seed; user picks mesh component(s) (parts/sub-assemblies, native
live `Selectionbox`) → names the link → picks parent from a combo of
existing links → **Add link**. First Add is always forced to root/
`base_link` (REP-105), every later link needs an explicit parent — no more
implicit flat-to-base. `RebuildJoints()` derives Joints (still hardcoded
Fixed — joint-type refinement is a separate future increment) straight
from each `LinkDef.ParentName`, so Links/Joints can't drift. Remove blocks
a link that still has children (must remove leaves first). `Sw2gzCreateRobotPmp.cs`
only; backend (`LinkDef.ParentName`, exporter, `Sw2gzDocLinkTreeRoundTripTests`)
already supported a real tree, unchanged. 470 green, clean build, deployed
09:21:57. **Needs a live SW check** — this is exactly the class of change
(PMP/COM wiring) that has silently broken live before despite green tests;
open Create Robot on FULL_ARM.SLDASM and walk: pick a part → Add link (becomes
base_link) → pick another part → name it → parent=base_link → Add → remove
base_link (should be blocked, has a child) → remove the child → remove
base_link (should now work).

## Done — Robot mode v3: minimal Create Robot wizard + exporter live (branch `feat/robot-mode-v3`, pushed, commit 185bd84)

**Re-applied (2026-07-02) — PillUpdate Create/Edit label-sync fix, isolated.**
The 2026-07-02 full revert (`git reset --hard af33ca2`, see below) dropped a
sound, independent fix alongside the faulty link-hierarchy work: `PillUpdate`
in `SwAddin.cs` was checking the (possibly stale) cached doc's `IsLocked`
before `HasSaved`, so deleting the saved Robot doc attribute from the
FeatureManager tree never re-ran `MaybeDeferLabelSync` — the ribbon's
Create/Edit label stayed stuck. Re-applied that exact fix only (cherry-picked
by hand from reflog commit `eb18968`, not the tree/mesh-assignment work it
shipped alongside): check `HasSaved` first, always call
`MaybeDeferLabelSync`, and drop+refetch the cached doc via
`Sw2gzDocStore.Reset` if it's stale-locked while nothing is actually saved.
470 green (unchanged), clean Release build, redeployed
(`C:\Program Files\SW2GZ\SW2GZ.dll` 09:02:45). **Needs a live re-check**
(delete the saved Robot attribute from the tree, confirm the ribbon label
flips back to "Create Robot" without a doc reopen) — this bug was never
confirmed fixed live before the revert wiped it out the first time.

**FAILED + FULLY REVERTED (2026-07-02) — link hierarchy tree + manual mesh
assignment.** Attempted: rework the Links step from the flat one-mesh-per-
link list into a drag-to-reparent hierarchy (reusing pre-existing, unwired
`LinkHierarchy`/`LinkTreeView`) + a geometry "pick funnel" for manual multi-
mesh assignment, plus a `PillUpdate` ribbon label-sync bugfix. Spec/plan
written, implemented task-by-task via subagent-driven-development (fresh
implementer + spec-compliance review + code-quality review per task,
including one review→fix→re-review cycle), independently build- and
test-verified (473 green, clean Release build) at every step. **Still
faulty live in SolidWorks** per user report after deploy — no specific
symptom captured before the user called for a full revert, so the failure
mode is NOT diagnosed. `git reset --hard` back to `af33ca2` (this session's
8 commits were local-only, never pushed, so the reset was clean); DLL
rebuilt from the reverted tree and redeployed over the faulty one. Test
suite back to the pre-session baseline (470 green). Spec/plan docs left on
disk for reference (`docs/superpowers/specs/2026-07-01-robot-wizard-link-
hierarchy-design.md`, `docs/superpowers/plans/2026-07-01-robot-wizard-link-
hierarchy.md`) but treat both as **abandoned, not pending** — the code they
describe no longer exists on this branch. **Lesson for next attempt:** this
is the second time in a row (see the mate-driven-detection postmortem right
below) that a robot-wizard change passed every automated gate (build, full
test suite, multi-stage code review) and still broke live in SW — the gap
is entirely in COM/PMP-UI behavior that isn't and can't be exercised by
`dotnet test`. Next attempt should get an early live checkpoint (open the
wizard in SW after the *first* small wiring step, before piling on 3 more
tasks on top) rather than building the whole thing then discovering it's
broken at the end. `LinkHierarchy`/`LinkTreeView` themselves were NOT
touched by this attempt (only called into) and remain exactly as they were
— still inert, unwired, still a plausible starting point for a retry, just
proven not sufficient on their own to make the wizard work correctly.

**Working now, live-tested against FULL_ARM.SLDASM:** Create Robot → Links
step (seeded from top-level components, first = base_link, rest Fixed to
it) → Finish → Preview / Export both produce a real URDF package with
correct mesh geometry, orientation, and per-link placement. Joint TYPE is
hardcoded Fixed for every link (mate-driven type detection was attempted and
reverted — see below); the joint origin/rpy is real relative pose math, not
a placeholder.

**Big finding this session — a real, pre-existing, live bug, not a Robot-only
issue:** `Component2.Transform2.ArrayData`'s 3x3 rotation block is
**column-major**, not row-major as every existing call site assumed
(verified empirically against `Component2.GetBox`, since a from-VBA
`IMathPoint.MultiplyTransform` check turned out to silently no-op instead of
throwing — see memory `sw-mathtransform-column-major`). This was silently
inverting rotation for any non-identity-rotated component in
`SolidWorksMeshTessellator` (mesh baking — used by **World and Asset export
too**) and `SolidWorksAssemblyWalker` (`TransformByComponent`/
`RotateByComponent`). **Both fixed.** Worth a live re-check of World/Asset
exports containing rotated components next time either is touched, since
this shipped wrong for an unknown amount of time before today.

**New files:** `SW2GZ/UI/Pmp/Sw2gzCreateRobotPmp.cs` (wizard, mirrors
`Sw2gzCreateWorldPmp`'s WinForms-nav-in-PMP chrome), `SW2GZ/URDFExport/
Sw2gzRobotExporter.cs` (writes `<pkg>_ws/src/<pkg>/urdf/<pkg>.urdf.xacro` +
`meshes/*.dae`, reuses `SolidWorksMeshTessellator`/`SolidWorksMassProperties`),
`SW2GZ/SwSurface/Abstractions/IComponentPoses.cs` +
`SolidWorksComponentPoses.cs` (exact per-component rotation+translation —
lets the exporter un-bake each link's mesh into its own native part-local
frame instead of an AABB-center approximation, so joint origin/rpy carry the
real relative pose between parent and child).

**Tried and reverted — mate-driven joint type/axis/limit auto-detection.**
Built `SolidWorksMateJointDetector` (ported the pre-gut `AutoJointResolver`'s
model from git history), wired into the wizard + exporter. Live-tested by
the user: still showed Fixed-only / wrong behavior even after two fix
passes, so fully reverted (file deleted, wiring removed) rather than ship
something unverified. Data model (`JointDef.Type`/`AxisX-Z`/
`LimitLower/Upper`) still holds the fields for whenever this is retried —
full postmortem + what to do differently next time (build against a live
SW test loop from the start, don't port old logic blind) is in memory
`robot-mode-dev`.

**Also unresolved, not root-caused:** user reported a stale Create/Edit
ribbon label after deleting the saved Robot doc attribute — same symptom
class as an old, already-fixed World-mode bug. Code audit of the whole sync
chain (`SyncRibbonToActiveDoc`/`PillUpdate`/`RefreshTabForMode`) found it
fully mode-generic with no Robot-specific gap; could not reproduce or
isolate a concrete defect from static reading. Needs a live repro with the
exact symptom (button text stuck, wrong wizard data, or a crash) before
attempting a fix.

**Test count:** 464 (this branch's baseline) → 470 green.

## Done — Robot mode GUTTED for clean rebuild (branch `feat/robot-mode-v2`, pushed)

Robot mode's inherited implementation was buggy (coordinate tilt the Option-A
fix on `feat/robot-mode` didn't resolve live — that branch was reset, work in
reflog). Decision: SCRAP robot mode, rebuild clean. World + Asset modes kept
fully working; add-in compiles clean + deployed (DLL ~704 KB, was 872 KB).
See memory `robot-mode-dev`.

**REMOVED (~8000 lines, each a green commit on `feat/robot-mode-v2`):**
- Robot export engine: `Sw2gzPipeline`, `Ros2Package`, `XacroGenerator`,
  `XacroWriter`, `Ros2ControlWriter`, `ControllersYaml`, `GzPluginTags`.
- Robot Create wizard PMP (`Sw2gzCreateRobotPmp`); `OpenCreateRobot` → no-op
  "Robot mode not implemented yet" message. Mode pills + Create/Edit Robot
  button stay.
- Robot ribbon cluster (Inertia/Sensors/Actuation/Stack) + ids + handlers.
  World Sensors/Settings + Asset buttons untouched.
- Robot wizard MVVM view-models (Links/Joints/Collision/Materials/Sensors/
  Controllers/Targets/Review steps + DTOs + edit VMs + WizardViewModel/
  WizardStepPlan/WizardModelComposer).
- Robot builders/walkers/writers: JointGraphBuilder, JointBuilder/Seeder/
  DefConverter/DefValidator, LinkBuilder/DefValidator, RobotModelBuilder,
  JointOriginResolver, LinkAnchorMap, MeshRebase, RobotModelValidator,
  WizardAssemblyWalker, AutoJointResolver(+Resolved), SwRefAxisCreator,
  SwRefGeometryEnumerator, SwJointPoseReader, PackageXmlV3Writer,
  AmentCMakeWriter, LaunchPyWriter, RvizConfigWriter, ReadmeWriter,
  RosGzBridgeYaml, Sw2gzPipelineExportRunner. Two pipeline-construction sites
  (ExportHelper, runner) stubbed to throw NotSupported.

**ALSO DONE (this session):**
- **Shared export config is robot-free.** Stripped `Links`/`Joints`/`Stacks`
  from `Sw2gzExportConfig` + fixed every reader (`Sw2gzDocToExportConfig`,
  `Sw2gzModelPreviewer` live-joint sampler, `ExportDialog`, SwAddin/serialization
  logs, self-clone); deleted dead `StackRibbonGate`.
- **Test project GREEN again** — removed ~50 robot test files + 44 dangling
  source-link csproj entries; pruned invalid config tests. `dotnet test` = **464
  passed**. Add-in compiles clean; deployed (~704 KB).

**STILL PRESENT (inert robot DATA TYPES — the v2 rebuild foundation, intentionally
kept):** `RobotModel`/`RobotMeta`/`ModelLink`/`LinkDef`/`JointDef`/`UrdfLink`/
`UrdfJoint`/`ControlSpec`/`StackProfile`/`MateSpec`/`GazeboLinkProps`/
`GeometryAssignment`/`LinkHierarchy`/`Sw2gzExportScopePlanner`/`LinkTreeView`/
`Sw2gzExportWizardForm`/`SwJointStateSampler`/`SW2GZ\URDF\*`. Still referenced by
`Sw2gzDoc.Robot` + the shared export form/helpers; they're plain data with no
robot logic, so they're the natural data model to build v2 on rather than delete
+ recreate. (ModeStep/OutputStep VMs + StepViewModelBase also kept.)

**Next for the v2 rebuild:** with a clean slate, rebuild robot export fixing
coordinates on the path the user actually exports (not a side path) and verify
upright in jsp_gui/RViz BEFORE building anything on top.

## Done — Mode×feature gap fill (branch `feat/world-sensors`)

Autonomous /loop (hourly, session cron `5734e4b3`): work the World/Asset ➕ items
from [`docs/mode-feature-matrix.md`](../docs/mode-feature-matrix.md), commit each
green increment as a safe retreat. Sequenced plan: **W1 ✔ → W2 → W3 → A1 → A2 →
A3** (see matrix "Summary — what to add").

**W1+W2+W3 DONE (this session). 824 tests green, add-in compiles clean.**
Spec (W1): [`docs/superpowers/specs/2026-06-30-world-launch-bridge-design.md`](../docs/superpowers/specs/2026-06-30-world-launch-bridge-design.md).

- **W1 — World launch + ros_gz bridge (standalone, no-ament).** Export writes
  `<pkg>/launch_world.py` + `<pkg>/ros_gz_bridge.yaml` → run via
  `ros2 launch <pkg>/launch_world.py` (no colcon). Paths resolve relative to the
  launch file (`__file__`). Bridge = `/clock` always + `/cmd_vel` (ROS→GZ) when
  teleop on. NEW `Ros2/WorldLaunchPyWriter.cs`, `Gz/WorldBridgeYaml.cs` (pure).
- **W2 — collision friction.** Every world model `<collision>` gets
  `<surface><friction><ode>` (mu=mu2). Tunable `Sw2gzExportConfig.WorldFriction`
  (default 1.0, persisted+cloned). `SdfSceneInput.FrictionMu` opt-in (null =
  byte-identical); exporter always passes it.
- **W3 — extra fill lights (writer block).** NEW `SdfLight` record +
  `SdfPhysicsBlock.Light()` (point/spot/directional) + `SdfSceneInput.ExtraLights`
  (null/empty = byte-identical, only the sun). Writer-first, like SdfCamera was.
- All three are **value-by-default for W1/W2** (every world export gains
  launch+bridge+friction). W3 is the writer building block; the lights LIST
  config + Settings-panel UI is **deferred** (no UI knob yet → exporter passes
  none, worlds unchanged).
- NOT deployed (pure-writer/config cut, no COM-surface change → live deploy not
  required for correctness; world-sensors UI still needs the user's live test
  before a v2.6.0 tag).

**A1 DONE — articulated asset (1-DOF joint to world).** Spec:
[`docs/superpowers/specs/2026-06-30-articulated-asset-design.md`](../docs/superpowers/specs/2026-06-30-articulated-asset-design.md).
An asset can now anchor its link to the `world` frame via one joint
(none|fixed|revolute|continuous|prismatic) → door/lift/wheel/lever props.
- `SdfAssetModelInput` gained `JointType`/`JointAxisX-Z`/`JointLower`/`JointUpper`;
  writer emits `<joint><parent>world</parent><child>link</child>…` after `</link>`.
  continuous = revolute w/ no `<limit>`; fixed = no axis. `none` (default) =
  byte-identical.
- `Sw2gzExportConfig.AssetJoint*` (DataMember + OnDeserializing + clone).
- `Sw2gzAssetExporter`: a joint forces the model dynamic (joint-to-world is
  invalid on a static model) + placeholder inertial.
- +7 tests (**831 green**); add-in compiles clean. Doc/codec persistence + Asset
  wizard "Articulation" step **deferred** (like W3 lights) — config-driven for now.

**A2 DONE — sensor-bearing asset.** Optional `<sensor>` on the asset link
(camera/gpu_lidar/imu) reusing robot-side `SdfSensorBlocks.Write`.
- `SdfAssetModelInput.Sensor` (SensorDef, null = none); writer splices the block
  inside `<link>` (indent 6) before `</link>`.
- `Sw2gzExportConfig.AssetSensorKind`/`AssetSensorTopic` (+deser+clone);
  `Sw2gzAssetExporter.BuildSensor` builds a default-parameterised camera/lidar/imu
  on "link" at origin. Host world supplies the sensor system (World "Sensors"
  panel) — nice synergy with the world track.
- Combines with A1 (e.g. revolute door + camera). +6 tests (**837 green**);
  add-in compiles clean. Doc/codec + Asset-wizard sensor step deferred.

**A3 DONE — primitive collision override.** Asset collision can be a primitive
(box/sphere/cylinder) fit to the mesh AABB instead of the full mesh; visual
always stays mesh. `Sw2gzExportConfig.AssetCollision` ("mesh" default).
`SdfAssetModelInput` gained `CollisionShape` + AABB centre/size; the exporter
computes the AABB of the grounded mesh and passes it. Zero AABB → falls back to
mesh (no zero-size box). +5 tests (**842 green**); add-in compiles clean.

### ✅ UI + persistence COMPLETE — Asset wizard live-tested; World Settings WP1+WP2 done
- **Asset wizard UI (A1-A3):** Create-Asset Surface step has collision/joint+axis+
  limits/sensor+topic controls; doc-persisted; **live-tested OK** (commit 4ddb666).
- **WP1 — World friction knob:** `Sw2gzWorldSceneConfig.Friction` + Bridge →
  `cfg.WorldFriction`; World Settings PMP "Ground friction μ" (Environment).
- **WP2 — World extra lights:** `Sw2gzLightConfig` + `Scene.Lights` (deep-clone) +
  `ToExtraLights()`; exporter passes `ExtraLights`; World Settings PMP "Lights"
  section, 2 fill-light slots. **849 tests green.**
- Deployed `C:\Program Files\SW2GZ\SW2GZ.dll` **07:28:45** (fresh).
- **WP3 DONE — live-tested in SW 2025 (ASM_MAIN).** Settings PMP friction + 2
  light slots render/edit/persist, no crash; Sensors PMP toggles OK. Exported
  World → verified on disk: `asm_main.sdf` has `<light name="light1">` (Z=3,
  diffuse 1.2 = 0.8×1.5 intensity) + `gz-sim-imu-system` + KeyPublisher GUI;
  `launch_world.py` (standalone) + `ros_gz_bridge.yaml` (/clock + /cmd_vel).
  Friction not in that export (no ground/asset picked → default ground_plane
  only; backend-tested). Found + FIXED stale export-dialog text (was showing
  Robot `_ws`/colcon for World). Deployed DLL **08:21:26**.
- **Closed open issue:** default `ground_plane` collision had no friction, so the
  μ knob was inert when no ground asset was picked (the live-export case). Added
  `SdfPhysicsBlock.GroundPlane(mu)`; `WriteScene` passes `FrictionMu` to it.
  no-arg byte-identical (robot goldens). +4 tests.
- **✅ TAGGED v2.6.0** (now **853 green**, live-tested, deployed **08:37:24**).
  Tag moved to include the ground-friction fix (unpushed → safe). NOT pushed.
- KeyPublisher/TriggeredPublisher live-toggle = UI hit-precision, not a code bug.

### ✅ World+Asset ➕ matrix set BACKEND-COMPLETE (this session, branch `feat/world-sensors`)
W1 launch+bridge · W2 friction · W3 lights(writer) · A1 articulated asset ·
A2 sensor asset · A3 primitive collision. 801→842 tests. Every feature is
opt-in/default-safe (unset = byte-identical) and committed as its own green
safe-retreat point.

**Remaining = deferred-UI backlog + the tag gate (NOT backend):**
- Wizard/UI: World Settings "Lights" panel (W3 list), Asset-wizard
  Articulation+Sensor+Collision steps (A1/A2/A3 config knobs), doc/codec
  persistence for the new Asset/World config fields (currently config-level only;
  set programmatically, not yet round-tripped through `Sw2gzDoc`).
- **Tag gate (user):** world-sensors UI still needs a live SOLIDWORKS test before
  tagging **v2.6.0**; the W1-W3/A1-A3 cuts are pure writer/config (no COM-surface
  change) so they don't need redeploy to be correct, but they ride the same tag.

## Done — World sensors = plugin toggles + left-dock PMPs (branch `feat/world-sensors`)

**State:** code COMPLETE + green (**801 tests**), add-in compiles clean, NOT
deployed, NOT committed. Pivoted away from the earlier per-model sensor S1.

**The pivot (user-directed 2026-06-30):** World mode does NOT place sensors on
its models. The "Sensors" panel just ENABLES world-level Gz system/GUI plugins
so spawned models can use them (sensor families + keyboard teleop). Both
"Settings" and "Sensors" moved from floating WinForms dialogs → native left-dock
PMPs. Ribbon labels dropped "World" → just **Settings** / **Sensors**.

- **BLOCKER — not deployed.** Elevated copy keeps auto-cancelling at UAC
  (non-interactive harness). Fresh DLL `bin/Release/SW2GZ.dll` **04:26:22**;
  installed still **18:04:58** (old). **Deploy manually** (SW closed):
  `Copy-Item 'C:\aryan\SW2GZ\SW2GZ\bin\Release\SW2GZ.dll' 'C:\Program Files\SW2GZ\SW2GZ.dll' -Force`

**Next steps:** (1) confirm manual deploy, (2) live-test (World mode →
**Settings** PMP edits scene/env; **Sensors** PMP toggles → Export → `.sdf` has
the `gz-sim-*-system` / KeyPublisher / triggered-publisher plugins, NO `<sensor>`
blocks), (3) commit + merge to main + tag (**v2.6.0**).

**Changes (all on the branch, uncommitted):**
- NEW `URDFExport/Sw2gzWorldSensorsConfig.cs` — [DataContract] bool toggles
  (Sensors/Imu/Contact/ForceTorque/Navsat + UserCommands/SceneBroadcaster
  default-on + KeyPublisher/TriggeredPublisher). Lives on
  `Sw2gzDoc.World.SensorPlugins`; OnDeserializing reseeds baseline-on.
- NEW `Gz/SdfWorldPluginsWriter.cs` — pure `SdfWorldPlugins` record +
  `WriteWorldPlugins` (world `<plugin>` lines) + `WriteGuiKeyPublisher` (GUI
  line). Teleop = 4 triggered-publisher blocks mapping arrow Qt keycodes →
  Twist on `/cmd_vel`.
- `Gz/SdfWorldWriter.cs` — `SdfSceneModel` lost `Sensors`; `SdfSceneInput` gained
  `Plugins`. WriteScene emits world plugins from flags (null Plugins =
  byte-identical baseline); per-model `<sensor>` emission removed. GUI block now
  also emitted when KeyPublisher on; `SdfGuiBlock.Default(cam, keyPublisher)`.
- `Sw2gzWorldExporter.ToWorldPlugins(config.WorldSensorPlugins)` → SdfSceneInput.
  Old sensorsByModel/mapper/modelByComp gone.
- `Sw2gzExportConfig.WorldSensorPlugins` (replaces WorldSensors list) + `Bridge`
  copies `world.SensorPlugins`. `Sw2gzDocSnapshot.CloneWorld` now deep-clones
  Scene + SensorPlugins (was dropping Scene → fixed so PMP cancel restores).
- NEW `UI/Pmp/Sw2gzWorldSettingsPmp.cs` + `UI/Pmp/Sw2gzWorldSensorsPmp.cs` —
  native PMPs (Okay/Cancel, snapshot rollback). `SwAddin.OpenWorldSettings/
  OpenWorldSensors` build+Show them (fields `_worldSettingsPmp`/`_worldSensorsPmp`
  root the COM handler). Sensors no longer gated on ground/assets.
- DELETED: `Sw2gzWorldSensorConfig.cs`, `WorldSensorMapper.cs`,
  `WorldSensorsDialog.cs`, `WorldSettingsDialog.cs`, `Sw2gzDarkTheme.cs` (the
  last 3 went dead with the dialog removal). csproj entries updated in both.
- Robot-side `SdfSensorPlugins`/`SdfSensorBlocks` UNTOUCHED (still per-sensor).

**Gotchas burned in this session:**
- Manual elevated deploy required (UAC non-interactive). SW must be CLOSED.
- **Native PMP checkboxes AV-crash SW on toggle** (empty OnCheckboxCheck, doesn't
  matter). Both PMPs now host their controls in a WinForms panel via
  swControlType_WindowFromHandle (the Create-wizard pattern) — WinForms controls
  bypass SW's native-control event path. Read values on Okay. Installed 04:40:07.
- Commit messages: no double-quotes in the `git commit -m @'...'@` here-string
  (breaks PowerShell parsing) and NO AI attribution.

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
