# Robot-mode Joints step: mate-based type/axis/pivot suggestion (Phase 2)

**Date:** 2026-07-03
**Mode:** Robot (v3)
**Branch:** `feat/robot-mode-v3`
**Status:** Approved via brainstorming dialogue (this doc), ready for
implementation plan.

## Problem

Phase 1 (shipped: `JointDefReconciler`, exporter type/axis/limit wiring,
Joints wizard step) gave the user manual control over each joint's Type,
Axis, and Limit, but the user still has to work out those values by hand —
type them in from scratch, guessing at an axis direction, with no help from
the mates the user already defined in SolidWorks to build the assembly in
the first place. Live-testing Phase 1 also surfaced a real, previously
invisible defect: the joint's rotation **pivot location** is wrong for any
non-Fixed joint, because `<origin>` is derived purely from the child link's
own component pose — not from where the mechanical hinge actually is. This
was flagged in Phase 1's own design doc as an out-of-scope limitation
("`OriginX/Y/Z`/`HasOrigin` fields... defaults to identity for now") and is
exactly what mate geometry can fix: a Concentric mate's cylindrical axis
*is* the real hinge location.

A prior attempt at mate-driven joint detection (`SolidWorksMateJointDetector`,
2026-07-01) was built, had a real classification bug fixed, and was still
**fully reverted** after breaking live in SolidWorks with no captured
symptom (see memory `robot-mode-dev`). This phase reuses that detector's
classification logic (proven correct after the fix, never proven live) but
is explicitly scoped and sequenced to avoid repeating the failure: build
against live mate data from the start (this session used the newly
available `mcp__SolidWorks_MCP__sw_list_mates`/`sw_mate_detail` tools to
verify the classification rules against `FULL_ARM.SLDASM`'s real mates
before writing this doc — see "Live verification" below), and ship in
live-tested increments as Phase 1 did.

## Design decisions (from brainstorming)

- **Trigger: auto-suggest on Joints-step entry**, per joint, only when that
  joint's `IsSuggested` flag (new) is still `false`. No button, no
  per-joint opt-in — happens as part of the existing `RefreshJointsList()`
  refresh.
- **Matching scope: primary component only.** A joint's parent/child mate
  search only considers `LinkDef.ComponentIds[0]` for each side — the same
  "first component defines the link's frame" convention already used
  everywhere else (mesh anchor, joint origin, inertial rebase). Multi-mesh
  links' non-primary components are not considered, consistent with how
  the rest of the pipeline already treats them.
- **Classification (reused from the reverted detector, this time validated
  against live data first):** walk every mate in the assembly, keep the
  ones whose entities' owning components (`IMate2.MateEntity(i).
  ReferenceComponent` — a real COM call, not something the current
  `sw_mate_detail` MCP tool surface exposes; it only returns
  type/limits/flipped/entity-count, not entity ownership) are exactly the
  parent/child primary component pair. Classify every matching mate by its
  own type (no "companion mate" search — each mate is classified
  independently, exactly the bug the reverted attempt's second cut fixed),
  take the highest-priority result:
  1. **Limited Angle mate → Revolute**, `LimitLower`/`LimitUpper` straight
     from the mate's own `MinimumVariation`/`MaximumVariation` (already
     radians, no conversion needed at suggestion time — the Task-5 UI
     degree conversion only applies to what the user types/sees).
  2. **Plain Concentric mate (no accompanying limited Angle) → Continuous.**
  3. **Limited Distance mate → Prismatic**, limits from `MinimumVariation`/
     `MaximumVariation` (meters).
  4. **Nothing recognized → leave Fixed, `IsSuggested` stays `false`** (not
     `true` — so a later SW edit that adds a real mate to this pair can
     still trigger a suggestion on a future Joints-step visit; cheap to
     re-check, no downside).
  - "Limited" test matches the old, proven-correct heuristic exactly:
    `abs(MinimumVariation) > 1e-9 || abs(MaximumVariation) > 1e-9` — NOT
    `upper > lower` (the reverted attempt's first, wrong guess). Confirmed
    against real data today: `FULL_ARM`'s two Concentric mates both read
    `0/0` (unlimited, as expected — a plain Concentric mate never carries
    its own limit), its one Angle mate reads real non-zero values.
  - Width/Coincident/other mate types are pure noise for classification —
    never considered, matching the reverted attempt's lesson ("a real
    hinge is usually Concentric + Coincident... picking the uninformative
    Coincident first → silently Fixed" was the original bug). Confirmed
    against real data: `FULL_ARM` has a `Width1` (4 entities, centering
    constraint) and a `Coincident3` alongside its two Concentric mates —
    exactly this pattern, live.
- **Axis + pivot, scope expansion beyond Phase 1's original plan:** for a
  Concentric-mate-derived suggestion, the mate's cylindrical face gives
  both an axis DIRECTION (`Surface.CylinderParams`, same COM call the old
  detector used) — written to `JointDef.AxisX/Y/Z` in assembly frame,
  already wired through the exporter since Phase 1 — and an axis POINT,
  written to a **new** `JointDef.MatePointX/Y/Z` + `HasMatePoint` (fields
  already present on the model, unused, originally added for the legacy
  wizard pipeline's own never-wired mate-point concept — reused here, not
  duplicated). `Sw2gzRobotExporter` gains one new behavior: when
  `HasMatePoint`, the joint's `<origin>` **position** is computed from that
  point instead of the link-pose-derived position it uses today — **origin
  orientation (`rpy`) is completely untouched**, still the existing
  parent-relative rotation math. This is the only change to the
  Phase-1-proven origin math, deliberately narrow (position only).
  - For an Angle-only or Distance-only suggestion with no accompanying
    Concentric mate for the same pair, axis derives from that mate's own
    reference geometry instead (e.g. `PlaneParams` for a planar reference)
    — no pivot-point override in that case, origin position stays
    link-pose-derived as today.
- **Suggestion state: new `IsSuggested` bool on `JointDef`**, default
  `false`. Any panel edit (Type change, Axis edit, Limit edit, rename) OR
  an explicit accept flips it to `true` — permanently opts that joint out
  of future auto-suggestion, including a user's deliberate choice to leave
  a joint Fixed with no axis (previously indistinguishable from
  "never analyzed").
- **Visual feedback: yellow pivot-axis line in the SW viewport, only for
  the currently-selected joint.** Same "only the active selection
  highlights" pattern already proven in the Links step's `HighlightLinkMesh`
  (selecting a link there highlights only that link's mesh, not every link
  at once). Line renders yellow while `IsSuggested` is pending for that
  joint, a neutral/confirmed style once accepted or edited. Disappears when
  a different joint is selected or the Joints step is left.
  - **The exact SolidWorks rendering mechanism is explicitly NOT decided by
    this design** — it needs implementation-time API research, not a guess
    baked into a spec. Two real candidates surfaced during brainstorming:
    (a) SolidWorks' own native "Temporary Axes" entity on a cylindrical
    face, selected and highlighted with a custom color (no new geometry
    created, closest to how `HighlightLinkMesh` already works via
    selection); (b) a transient 3D sketch line inserted on selection and
    deleted on deselect (heavier, touches the feature tree even if
    temporarily). The implementation plan must resolve this with a small
    live-tested spike before committing to either.
- **Override via reference geometry:** the user can replace any axis value
  (suggested or hand-typed) by picking real SW geometry (an edge, a
  cylindrical face) instead of typing X/Y/Z numbers — reuses the exact
  `Selectionbox` control pattern the Links step's mesh picker already uses,
  not a new interaction paradigm.
- **Sequencing:** this class of change (mate-driven joint detection) has
  already broken live once and was fully reverted despite passing every
  automated gate. Build and live-test in the smallest possible increments
  again, same discipline as Phase 1 — do not land classification +
  axis/pivot + visual feedback + reference-geometry override as one big
  change.

## Live verification (this session, before writing this doc)

Ran `mcp__SolidWorks_MCP__sw_list_mates`/`sw_mate_detail` against
`C:\aryan\CAD\Robots\3R_ARM\FULL_ARM.SLDASM` (open in SolidWorks at design
time) to validate the classification rules against real data before
committing to them in this spec, rather than porting the reverted
detector's logic blind:

| Mate | Type | Min/Max variation | Flipped | Entities |
|---|---|---|---|---|
| Concentric1 | Concentric | 0 / 0 | false | 2 |
| Concentric2 | Concentric | 0 / 0 | false | 2 |
| LimitAngle1 | Angle | -2.2054 / 0.9362 rad | true | 2 |
| Width1 | Width | 0 / 0 | false | 4 |
| Coincident3 | Coincident | 0 / 0 | false | 2 |

Confirms: Concentric mates never carry their own limit (always reads
`0/0`, matching the "is this limited" heuristic's expectation that a plain
Concentric is unlimited-by-definition); the real limited-Angle mate has
genuine non-zero Min/Max; Width/Coincident are present alongside the real
hinges and must be ignored for classification, exactly the noise pattern
the reverted attempt's post-mortem described. This is evidence the
classification rules are sound against this project's actual test
assembly, not just against the reverted attempt's own (also real, but
previously undocumented in this form) assumptions.

**Known gap, not resolved by this exploration:** `sw_mate_detail` does not
expose which components a mate spans — the implementation must use direct
COM (`IMate2.MateEntity(i).ReferenceComponent`) for that, following the
legacy `WizardAssemblyWalker.WalkMates()` and the reverted detector's own
pattern, not anything available through the current MCP tool surface.

## Testing

Same standard as Phase 1 (memory `robot-mode-dev`): green unit tests and a
clean build are necessary but not sufficient for this codebase's wizard/PMP
and mate-detection layers — both have broken live before while every
automated gate passed. The classification logic itself (mate type + limit
→ `UrdfJointType`, axis/pivot extraction) should be pure and unit-testable
against fake mate data, similar to `JointDefReconciler`; the actual
COM-querying glue (which mates exist, which components they span) is not
unit-testable and needs a live SolidWorks check on `FULL_ARM.SLDASM` before
being considered done — build the smallest wired increment, live-test it,
then continue, exactly as Phase 1 was sequenced.
