# Robot-mode Joints step: type / axis / limit panel (Phase 1)

**Date:** 2026-07-03
**Mode:** Robot (v3)
**Branch:** `feat/robot-mode-v3`
**Status:** Approved via brainstorming dialogue (this doc), ready for
implementation plan.

## Problem

Robot mode's wizard (`Sw2gzCreateRobotPmp.cs`) has exactly two steps today —
Links and Review (`StepCount = 2`). There is no UI to define a joint's type,
axis, or motion limits. `RebuildJoints()` auto-derives one `JointDef` per
non-root link (`parent_to_child` naming) but hardcodes `Type = Fixed` and
never sets axis or limits.

Worse, the data model already has the fields (`JointDef.Type`, `AxisX/Y/Z`,
`LimitLower/Upper`) and they're round-tripped through
`Sw2gzExportConfig.RobotJoints` — but `Sw2gzRobotExporter.WriteUrdf` never
reads `config.RobotJoints` at all. It writes `type="fixed"` unconditionally
and emits no `<axis>`/`<limit>` elements, deriving only the origin/rpy
geometrically from link poses. `JointDef.Type`/`AxisX-Z`/`LimitLower/Upper`
are currently dead weight on the wire.

This is Phase 1 of a two-phase build (mate-driven axis/type *suggestion* is
Phase 2, a separate future spec — see "Deferred to Phase 2" below). A prior
attempt at mate-driven joint detection (`SolidWorksMateJointDetector`,
2026-07-01) was built, had its classification logic fixed, and was still
**fully reverted** after breaking live in SolidWorks with no captured
symptom (see memory `robot-mode-dev`). The lesson carried into this design:
ship the smallest wired-up piece, live-test it in SolidWorks, before adding
anything else on top — Phase 1 deliberately excludes any mate-reading logic
so it can be verified live on its own first.

## Design decisions (from brainstorming)

- **Joint origin (position + orientation of the joint frame) stays 100%
  auto-derived**, exactly as `Sw2gzRobotExporter` computes it today
  (parent-relative rotation/translation from real SW component poses). This
  panel never lets the user type an origin override. Keeps the existing,
  already-live-tested-working invariant untouched.
- **Joint list stays strict 1:1 with the link tree** — one `JointDef` per
  non-root link, same as `RebuildJoints()` produces today. No independent
  add/remove-joint UI; URDF itself only allows one parent joint per link, so
  there's no case this would need to cover that the link tree doesn't
  already dictate.
- **Axis input is manual X/Y/Z boxes, entered in assembly (SW global)
  frame** — not a SW reference-geometry picker (that's Phase 2), not
  child-local frame (harder for a user to eyeball against what they see in
  SW). Matches `JointDef`'s own existing doc-comment convention ("cached
  axis direction (assembly frame)"). Only shown when Type ≠ Fixed. Defaults
  to `(0, 0, 1)` the first time a joint's type leaves Fixed.
- **Limit inputs: degrees for Revolute (converted to radians on save),
  meters for Prismatic (no conversion).** Matches SolidWorks' own
  degree-based UI convention so the user isn't doing mental radian math.
  Only shown for Revolute/Prismatic; Continuous and Fixed have no limit UI.
- **Type dropdown offers 4 values: Fixed, Revolute, Continuous, Prismatic.**
  `UrdfJointType` also has Planar/Floating, but neither has a real use case
  in this codebase yet — omitted from the UI (the enum values still exist,
  nothing stops adding them to the dropdown later).
- **`RebuildJoints()` changes from clear-and-rebuild to merge-preserve.**
  Today it calls `_liveDoc.Robot.Joints.Clear()` then creates fresh
  all-Fixed `JointDef`s on every link-tree mutation (add/remove/rename/
  reparent) — harmless today since Type is always Fixed anyway, but would
  silently discard the user's Type/Axis/Limit edits on every subsequent
  link edit once those fields are real and user-editable. Fix: match
  existing `JointDef`s by `(ParentLink, ChildLink)` pair; a pair that still
  exists keeps its `JointDef` (and whatever the user set on it) untouched;
  only genuinely new pairs get a fresh default-`Fixed` `JointDef`; pairs
  that no longer exist (link removed/reparented away) get dropped.
- **Exporter (`Sw2gzRobotExporter.WriteUrdf`) starts consuming
  `config.RobotJoints`.** For each non-root link, look up its `JointDef` by
  `ChildLink` name and:
  - write `type` from `JointDef.Type` (mapped to the URDF string) instead of
    the hardcoded `"fixed"`
  - write `<axis xyz="...">` when `Type != Fixed`, converting the
    assembly-frame `AxisX/Y/Z` into the child link's local joint frame via
    `R_child.Transpose().Mul(axisAssembly)` — the same transpose-multiply
    pattern already proven correct for joint origin (`R_parent.Transpose() *
    ...`) and mesh un-baking, not a new math approach
  - write `<limit lower="..." upper="...">` when `Type` is
    Revolute/Prismatic, using the already-radian-converted
    `LimitLower/Upper` values as-is
  - origin/rpy: **unchanged**, still the existing geometric parent-relative
    calc

## UI layout

New step `StepJoints` inserted between Links and Review:
`StepNames = { "Links", "Joints", "Review" }`, `StepCount = 3`.

List-plus-detail layout, matching the Links step's existing tree +
selected-info pattern:

- **Left: flat list of joints**, one row per non-root link, default name
  `parent_to_child`, renamable (like link renaming already works).
- **Right/below: detail form for the selected joint** —
  - Type dropdown (Fixed/Revolute/Continuous/Prismatic)
  - Axis X/Y/Z number boxes (visible only when Type ≠ Fixed)
  - Limit lower/upper number boxes (visible only when Type is
    Revolute/Prismatic; label reflects degrees for Revolute, meters for
    Prismatic)

## Deferred to Phase 2 (not built in this pass)

Mate-driven suggestion of Type + Axis per joint, rebuilt carefully from the
reverted `SolidWorksMateJointDetector` concept — this time developed against
live SolidWorks mate inspection from the start
(`mcp__SolidWorks_MCP__sw_list_mates` / `sw_mate_detail`, now available)
rather than ported blind from old logic and discovered broken only after a
full implementation. Suggested Type/Axis values would pre-fill the Phase 1
detail form fields, shown with a yellow highlight to mark them as
unconfirmed suggestions; the user can accept by leaving them, or override by
either editing the X/Y/Z boxes directly or picking SW reference geometry
(native `Selectionbox`, same UI pattern as the Links step's mesh picker),
which clears the highlight. This needs its own brainstorming pass before
implementation — not scoped further here.

## Testing

Standard for this codebase's wizard/PMP layer (see memory `robot-mode-dev`):
green unit tests and a clean build are necessary but **not sufficient** —
this class of change has broken live in SolidWorks twice before while every
automated gate passed. Live-test in SolidWorks on `FULL_ARM.SLDASM` before
considering Phase 1 done: open Create Robot, build a link tree with at least
one Revolute and one Prismatic joint, set axis + limits, export, and inspect
the resulting `.urdf.xacro` for correct `type`/`<axis>`/`<limit>` — then
confirm editing a joint's fields survives adding/removing an unrelated link
(merge-preserve check).
