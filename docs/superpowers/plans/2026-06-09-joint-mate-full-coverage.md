# Joint–Mate full coverage — implementation plan

**Branch:** `v2.1.0` (open follow-up after preview redesign lands).
**Owner:** Aryan Arlikar.
**Status:** plan only, no code yet.

## Why this matters

`AutoJointResolver.Resolve` today handles four SW mate types cleanly
(Concentric, LimitAngle, LimitDistance, Lock), and silently demotes
everything else to `Fixed`. That covers the `full_arm` test asset, but
real CAD assemblies routinely use Slot, Gear, Screw, Universal,
Symmetric, Tangent, and the combinations of these against Concentric +
Coincident that SW operators use to compose a joint from two or three
mates picked together.

Without coverage:
- Robots with **slotted prismatics** (e.g. linear slides) export as
  rigid — they look right in the viewer but won't move in Gz.
- **Gear / screw / rack-and-pinion** mechanisms lose the coupling
  relationship; each joint becomes independent, breaking the kinematics.
- **Universal / ball / spherical** joints become rigid welds.
- **Combinations** (Concentric + Coincident, Distance + Parallel, etc.)
  that the user authored as a single joint get treated as two
  unrelated mates, polluting the joint list.

The Joints step in Create Robot guides the user through manual mate
assignment, but the auto-detect path is what makes a healthy user
demo. Closing this gap brings parity with `sw_urdf_exporter` upstream
and unlocks the long tail of real-world assemblies.

## Source-of-truth tables

- `docs/ui-mockups/preview/joint-mate-reference.html` — visible table
  with status badges. Updated in lockstep with each phase of this plan.
- `SW2GZ/SwSurface/AutoJointResolver.cs` — the switch statement that
  has to grow to cover the matrix below.
- `SW2GZ/Build/JointBuilder.cs` — picks up the resolver's output and
  emits the URDF `<joint>` element; needs new arms for mimic / screw /
  ball.

## Mate inventory — single mates

| # | SW Mate (swMateType_e) | Combinations? | URDF target | SDF/Gz target | Phase |
|---|---|---|---|---|---|
|  1 | `swMateCONCENTRIC` (cyl + cyl) | – | `revolute`/`continuous`/`prismatic` | `revolute`/`prismatic` | **0 (done)** |
|  2 | `swMateANGLE` / `LimitAngle` (planar+planar) | – | `revolute` | `revolute` | **0 (done)** |
|  3 | `swMateDISTANCE` / `LimitDistance` (planar+planar) | – | `prismatic` | `prismatic` | **0 (done)** |
|  4 | `swMateLOCK` | – | `fixed` | `fixed` | **0 (done)** |
|  5 | `swMateCOINCIDENT` (planar+planar, point+plane, …) | composes with #1, #2, #3 | `fixed` alone, contributes to others | `fixed` | 1 |
|  6 | `swMateSLOT` | – | `prismatic` (slot length = limits) | `prismatic` | 1 |
|  7 | `swMatePARALLEL` | composes with #1 (drops cyl axis), #6 | `fixed` alone | `fixed` | 1 |
|  8 | `swMatePERPENDICULAR` | composes with #1 (drops rot DOF) | `fixed` alone | `fixed` | 1 |
|  9 | `swMateTANGENT` (curve + plane/cyl) | composes with #1, #6 | `fixed` alone, contributes | `fixed` | 1 |
| 10 | `swMateSYMMETRIC` | composes with #1, #5 | `fixed` (positional aid) | `fixed` | 1 |
| 11 | `swMateWIDTH` | – | `fixed` (positional aid) | `fixed` | 1 |
| 12 | `swMatePATH` (vertex on curve) | – | unsupported | unsupported | 4 |
| 13 | `swMateLINEARCOUPLER` (lin↔lin ratio) | – | `prismatic` + `<mimic>` | coupled prismatics | 2 |
| 14 | `swMateGEAR` (rot↔rot ratio) | – | `revolute` + `<mimic>` | `gearbox` | 2 |
| 15 | `swMateRACKANDPINION` (rot↔lin ratio) | – | rev + prismatic + `<mimic>` | `screw` (degenerate) | 2 |
| 16 | `swMateSCREW` (rot+lin coupled by pitch) | – | rev + prismatic + `<mimic>` | `screw` | 2 |
| 17 | `swMateUNIVERSAL` (2-axis rot) | – | 2× `revolute` decomposed | `universal` | 3 |
| 18 | `swMateHINGE` (composite) | already covered as Concentric+Coincident | `revolute` | `revolute` | 0 |
| 19 | `swMateCAMFOLLOWER` (cam profile) | – | unsupported | unsupported | 4 |
| 20 | `swMateBALL` (sphere + sphere) | – | not in URDF → `fixed` + warn | `ball` | 3 |

## Combinations matrix (what SW operators actually do)

| Combination | Picked entities | Today | After Phase 1 | After Phase 2 |
|---|---|---|---|---|
| Concentric + Coincident | 2 cyl + 2 planes | `revolute` from concentric; coincident dropped | `revolute` with rpy from coincident | same |
| Concentric + LimitAngle | 2 cyl + angle range | `revolute` with limits | same | same |
| Concentric + LimitDistance | 2 cyl + dist range | `prismatic` with limits | same | same |
| Concentric + Coincident + LimitAngle | 2 cyl + plane + range | `revolute` with limits | `revolute` w/ exact origin + limits | same |
| Slot + Coincident | slot + plane | `fixed` | `prismatic` (slot length) | same |
| Concentric + Parallel | 2 cyl + axes | `revolute` (Parallel ignored) | `revolute` (Parallel used as plane-of-revolution check) | same |
| Distance + Parallel | 2 features + dirs | `fixed` | `fixed` + offset baked into rpy | same |
| Tangent + Coincident | curve + plane | `fixed` | `fixed` | `prismatic` along curve (Phase 4 stretch) |
| Gear (2 cylinders + ratio) | 2 cyl + ratio | one independent `revolute`, second link orphaned | one `revolute`, second link orphaned + warn | URDF: 2× `revolute` + `<mimic ratio=N>`. SDF: `gearbox`. |
| Rack-and-pinion | 1 cyl + 1 line + pitch | `fixed` | `fixed` + warn | URDF: revolute + prismatic + `<mimic>`. SDF: `screw`. |
| Screw (helical) | 2 cyl + pitch | `fixed` | `fixed` + warn | URDF: rev + prismatic + `<mimic ratio=2π/pitch>`. SDF: `screw`. |
| Universal | 2 perpendicular axes | `fixed` | `fixed` + warn | URDF: parent rev → middle dummy link → child rev. SDF: `universal`. |
| Ball | 2 spheres | `fixed` | `fixed` + warn | URDF: `fixed` with `<sw2gz:ball/>` annotation. SDF: `ball`. |

## Phase plan

### Phase 0 — checkpoint (done)
- Concentric (with / without limits), LimitAngle, LimitDistance, Lock.
- 542 → 724 tests green at v2.1.0.
- `AutoJointResolver.cs` shipped.

### Phase 1 — single-mate full coverage + combination origins
**Scope:** every single SW mate has a deterministic outcome (joint or
documented `fixed`-with-rationale), and the **combinations** matrix
above moves to "exact origin + limits".

**Deliverables**
1. `AutoJointResolver.Resolve` switch arms for Slot, Tangent, Symmetric,
   Width, Parallel, Perpendicular, Path (→ unsupported), CamFollower (→
   unsupported).
2. `SlotFeatureReader` — reads `Feature::IFeatureData2::SlotFeatureData`
   to extract slot endpoints → prismatic lower/upper limits in metres.
3. `MateCombinationResolver` (new) — given the full mate set per link
   pair, recognises the SW idioms (Concentric+Coincident,
   Concentric+LimitAngle+Coincident, …) and produces ONE joint with
   correct origin instead of N parallel mates each emitting their own.
4. Warning emitter — every mate that resolves to `fixed` for a
   *non-fixed* reason (e.g. "Ball mate → fixed because URDF lacks ball
   joint, ball coordinates lost") writes a `WARN` line into the export
   report so the preview's validation pane can surface it.

**Tests (new, in `Test/Build/`)**
- `AutoJointResolverSlotTests` — slot + coincident → prismatic with
  correct limits.
- `AutoJointResolverCombinationTests` — Concentric + Coincident +
  LimitAngle produces ONE revolute with origin from the coincident.
- `AutoJointResolverWarningTests` — every "fixed by demotion" path
  produces a structured warning, not a silent drop.

**Estimated diff:** ~600 LOC src, ~400 LOC test. Two days.

### Phase 2 — mimic + screw + gear (URDF mimic, SDF native)
**Scope:** mechanical mates that couple two DOFs. Largest behavioural
change since the URDF emitter has never produced `<mimic>` before.

**Deliverables**
1. `JointDef.Mimic { string Source, double Multiplier, double Offset }`
   model extension.
2. `JointBuilder` arm for Gear, Screw, Rack-and-pinion, Linear-coupler
   — emits the dependent joint as type=`revolute`/`prismatic` and adds
   `<mimic joint="<src>" multiplier="N" offset="0"/>`.
3. SDF emitter (`SdfBuilder.cs`) — emits Gz-native `<joint
   type="gearbox">` or `<joint type="screw">` instead of mimic.
4. `MimicCycleDetector` — catches user errors where mate A mimics B
   mimics A (would crash robot_state_publisher); writes ERR, drops the
   second joint.

**Tests**
- Gear with ratio 2:1 → child revolute multiplier = 0.5.
- Screw with pitch 4 mm/rev → prismatic multiplier = 0.004 / (2π).
- Cycle detector — A↔B → one resolves, second drops with ERR.
- SDF mode emits `gearbox`, URDF mode emits `mimic`. Same input.

**Estimated diff:** ~800 LOC src, ~500 LOC test. Three days.

### Phase 3 — universal + ball
**Scope:** the two SW mates with no clean URDF type. Need decomposition
(universal) and graceful degradation (ball).

**Deliverables**
1. `UniversalJointDecomposer` — given two perpendicular axes, emit a
   parent link → revolute around axis-A → intermediate dummy link →
   revolute around axis-B → child link. Dummy link gets a near-zero
   inertial so it doesn't show up visually.
2. SDF mode — emit `<joint type="universal">` directly with both axes,
   no dummy link needed.
3. Ball mate in URDF mode → emit `<joint type="fixed">` with a
   structured comment block explaining why and a WARN. SDF mode emits
   `<joint type="ball">`.

**Tests**
- Universal → URDF round-trip yields a 6-element link list (3 user +
  2 dummies + 1 base) for a 2-joint universal.
- Universal → SDF round-trip yields a 4-element link list.
- Ball → URDF emits comment + WARN; SDF emits ball.

**Estimated diff:** ~400 LOC src, ~350 LOC test. Two days.

### Phase 4 — Path + CamFollower (decision)
**Scope:** the two mates that have **no** clean URDF/SDF mapping.

**Options under consideration**
- Skip them entirely with a hard ERR ("unsupported by URDF/SDF
  semantics; consider a `<plugin>` or external controller").
- For Path: approximate as a chain of prismatics along curve segments.
  Geometry-heavy, brittle, probably not worth shipping.
- For CamFollower: same, plus the cam profile would need closed-loop
  control — outside the preview's scope.

**Decision needed before starting this phase.** My recommendation:
ship Phase 4 as "documented unsupported, emit ERR with link to a
workaround note". Real CAM-driven mechanisms belong in a controller,
not a URDF.

## Tooling supporting the plan

- **Mate-name dictionary**: SW returns `swMateType_e` as an integer.
  Centralise a `MateTypeNames` lookup table (currently inline in
  several places) so the resolver, the JointStep wizard, and the
  preview's mates panel all show the same display name. Adds `~150
  LOC` and removes duplication.
- **Geometry probes**: `TryExtractCylinder`, `TryExtractPlane` exist
  already. Phase 1 adds `TryExtractSlot`, Phase 2 adds
  `TryExtractGearRatio` and `TryExtractScrewPitch`. All return the same
  `(bool ok, T data)` tuple shape for consistency.
- **Warning carrier**: `ValidationReport` already exists. Resolver
  pushes structured warnings instead of `logger.Warn` so the preview's
  validation HUD can surface them with the right joint name + mate name
  context.

## Preview integration

When the joints panel of the redesigned preview (`option-d-minimal`
direction, now live) opens, each joint entry needs a new badge:

- `auto` (silver) — `AutoJointResolver` mapped this mate confidently.
- `manual` (blue) — user picked the joint type in the Joints step.
- `fixed-fallback` (orange) — resolver demoted to fixed; original mate
  type shown in the detail panel.
- `unsupported` (red) — Path / CamFollower / etc. Detail panel links
  to the workaround note.

The mate-name attribute on each `<joint>` (already emitted into the
URDF as an `xml:comment` placeholder today) needs to become an actual
attribute the preview JS can read.

## Sequence

1. Phase 1 (single mates + combination origins) — biggest UX gain per
   line of code. Unblocks the slot-prismatic story which appears in
   every actuator.
2. Phase 2 (mimic + gear + screw) — unblocks geared / belted / screw
   actuators which are the next-most-common mechanism after pure
   revolutes.
3. Phase 3 (universal + ball) — covers the long tail.
4. Phase 4 (decision on Path/CamFollower) — documentation pass + ERR
   wiring.

## What I'd ship first

Phase 1 alone takes the v2.1.0 coverage from ~30% of real assemblies
to ~75%. Phase 2 closes most of the remaining gap. Phases 3-4 are
quality-of-life follow-ups.

Open this plan when picking up the work, update phase tickboxes as
each lands, and keep `joint-mate-reference.html` in sync with the
implemented state so the preview's reference page never lies.
