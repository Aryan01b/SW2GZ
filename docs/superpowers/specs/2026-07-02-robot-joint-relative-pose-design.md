# Robot joint/link relative pose + multi-mesh links

**Date:** 2026-07-02
**Mode:** Robot (v3)
**Branch:** `feat/robot-mode-v3`
**Status:** Approved via brainstorming dialogue (this doc), ready for
implementation plan.

## Problem

The Links step wizard (`Sw2gzCreateRobotPmp.cs`) now lets a user build a real,
arbitrary-depth, drag-to-reparent link tree, and lets a single link claim
multiple mesh components (multi-select on the mesh picker, `LinkDef.
ComponentIds` is already a list). The exporter (`Sw2gzRobotExporter.cs`)
was never updated to match either capability:

1. **Joint origin is always root-relative, not parent-relative.** The
   relative-pose formula it uses is correct, but `baseR`/`baseT` (used as
   "the parent" for every joint) are hardcoded to `links[0]`, never to the
   link's own `ParentName`. Invisible under the old flat wizard (everything
   was fixed straight to base, so parent == root for every link). Wrong now
   that a grandchild's true parent can be a non-root link.
2. **"Root" is detected by list position (`links[0]`), not tree structure.**
   The tree's "Set as base link" (re-root) flips `ParentName` pointers
   without reordering the `Links` list, so `links[0]` can silently stop
   being the actual root after a reroot.
3. **Only the first `ComponentIds` entry is used.** Mesh tessellation,
   pose lookup, and mass-properties lookup all read
   `link.ComponentIds.FirstOrDefault()` — every other component assigned to
   that link is silently dropped from the export (mesh, mass, everything).

## Design decisions (from brainstorming)

- **Multi-mesh union is in scope for this pass** (user chose to bundle it
  rather than defer — the parent-relative fix and the multi-mesh fix touch
  the same per-link pose plumbing anyway).
- **Root link keeps the identity-orientation convention.** `base_link`'s own
  SW rotation stays baked into its mesh rather than represented as TF
  orientation, same as today. Not touched by this pass.
- **No new coordinate-conversion math.** `InertialAggregator.Combine` already
  does correct mass-weighted-COM + parallel-axis inertia combination, but
  takes `Pose` (quaternion-based); the exporter works entirely in `Matrix3`.
  Writing a `Matrix3→Quaternion` converter to bridge them would be new,
  security-adjacent-precision math in exactly the category that has already
  produced two real bugs in this codebase (the `Transform2.ArrayData`
  column-major bug, the mate-classification bug — see memory
  `sw-mathtransform-column-major`, `robot-mode-dev`). Instead,
  `InertialAggregator`'s core combination loop is extracted into a
  `Matrix3`-parameterized overload; the existing `Quaternion` overload
  becomes a thin wrapper over it (`Matrix3.FromQuaternion(f.Rotation)` was
  already its first step). Zero new conversion code, zero duplicated
  physics math, existing callers untouched.
- **Joint type stays hardcoded Fixed.** Mate-driven type/axis detection is a
  separate, previously-reverted effort (see memory `robot-mode-dev`) — not
  reopened here.
- **A link's "own frame" = its first `ComponentIds` entry**, consistently,
  everywhere a link needs one frame: mesh un-bake anchor, joint origin
  math, and inertial rebase anchor. Order = pick order in the wizard
  (already implicit — no new UI concept).

## Math

Both link poses are read directly from `IComponentPoses.GetPose`, which
already returns each component's pose in the assembly frame (verified
column-major). No kinematic-chain walking is needed — every component's
pose is already absolute, so a joint's relative pose is one direct
computation between the child's reference component and the parent link's
reference component, regardless of tree depth:

```
R_joint = R_parent^T · R_child
t_joint = R_parent^T · (t_child - t_parent)
```

This is the exact formula already in `Sw2gzRobotExporter.cs:123-124` today —
only the source of `R_parent/t_parent` changes (from "always `links[0]`'s
pose" to "the pose of the link named in `link.ParentName`").

Mesh un-baking for a multi-component link re-expresses every component's
world-frame tessellation in the **link's own reference frame** (not each
component's individual frame — that would scatter the pieces):

```
p_local_i = R_ref^T · (p_world_i - t_ref)      for every component i on the link
```

Mass/inertia combination for a multi-component link: gather
`(MassProps, R_i, t_i)` per component, run the new `Matrix3`-based
`InertialAggregator.Combine` overload (mass-weighted COM + parallel-axis,
same algorithm as today, no representation change), then rebase the
combined result into the link's reference frame the same way the existing
`Combine(parts, linkAnchor)` overload already does — just with `Matrix3`
in place of `Pose`.

## Scope (locked)

**Backend:**
- Parent-relative joint origin (existing formula, corrected parent lookup).
- Root detection via `LinkHierarchy.Roots(links)` (pure, already
  unit-tested), not `links[0]`.
- Multi-mesh union per link: tessellate every `ComponentIds` entry, un-bake
  each into the link's reference frame, concatenate with vertex-offset
  bookkeeping (same pattern `SolidWorksMeshTessellator` already uses
  internally for multi-body union, applied across components instead of
  bodies).
- Multi-part mass/inertia via a new `Matrix3`-parameterized
  `InertialAggregator.Combine` overload; existing `Quaternion` overload
  becomes a thin wrapper, byte-identical for existing callers.

**UI (small, motivated by the backend change):**
- Once a link's frame is defined by *which* assigned component is first,
  that needs to be visible, not implicit. Add a `(primary)` marker on the
  first mesh in:
  - `Sw2gzCreateRobotPmp`'s "Selected: name  Mesh: a, b" info label.
  - `LinkTreeView`'s per-node label (which already shows a `[N parts]`
    suffix — this sits next to it).

**Out of scope:** joint type/axis/limit detection (Fixed only, unchanged),
reordering or removing individual mesh entries within a link, changing
which component is primary after the fact, real root orientation.

## Code seams

| Action | File | Change |
|---|---|---|
| EDIT | `SW2GZ/URDFExport/Sw2gzRobotExporter.cs` | Replace the single `baseR/baseT`-vs-everyone loop with: resolve root via `LinkHierarchy.Roots`; for each non-root link, look up its own `ParentName`'s reference pose (not root's) for the joint formula; replace the single-component tessellate/pose/mass calls with a loop over `ComponentIds`, unioning mesh + combining mass via the new helpers below. |
| EDIT | `SW2GZ/Build/InertialAggregator.cs` | Extract the existing `Combine` loop body into a `Matrix3`-parameterized core; add `Combine(IReadOnlyList<(MassProps Props, Matrix3 Rotation, Vector3 Position)> parts)` (+ the `linkAnchor`-rebase overload in `Matrix3` form). Existing `Quaternion`-based overloads call the new core via `Matrix3.FromQuaternion` — byte-identical output, no behavior change for current callers (`SwJointStateSampler` and friends). |
| EDIT (new private helper) | `SW2GZ/URDFExport/Sw2gzRobotExporter.cs` | Small private mesh-union method: given a link's `ComponentIds` + reference `(R, t)`, tessellate each, un-bake into the reference frame, concatenate with vertex-offset-adjusted triangle indices. Not extracted to a shared file — single caller, matches `SolidWorksMeshTessellator`'s existing private-helper precedent for this exact pattern. |
| EDIT | `SW2GZ/UI/Pmp/Sw2gzCreateRobotPmp.cs` | `RefreshSelectedInfo` — mark the first `ComponentIds` entry `(primary)` in the mesh list string. |
| EDIT | `SW2GZ/UI/LinkTreeView.cs` | `BuildNode`/`RefreshActiveNodeLabel` — same `(primary)` marker in the node label, next to the existing `[N parts]` suffix. |
| TEST | `Test/URDFExport/Sw2gzRobotExporterTests.cs` (confirmed existing) | 3+-level chain: grandchild joint origin computed relative to its true (non-root) parent, not root. Reroot-then-export: root resolved by structure, not list order. Multi-mesh link: union mesh vertex/triangle count = sum of parts, correctly positioned in the reference frame. Existing single-mesh / 2-level-flat cases stay numerically identical (they're the degenerate case of the new logic). |
| TEST | `Test/Build/InertialAggregatorTests.cs`, `Test/Build/InertialAggregatorRotationTests.cs` (both confirmed existing) | New `Matrix3` overload matches the existing `Quaternion` overload byte-for-byte when rotations are equivalent (cross-checked via `Matrix3.FromQuaternion` — test-only conversion, never production code). `InertialAggregatorRotationTests.cs` already covers non-identity-rotation cases for the `Quaternion` path — mirror the same cases against the new `Matrix3` overload. Multi-part combine + rebase math unchanged from what's already tested there. |

## Definition of done

- All new + existing tests green (470 baseline + new cases above).
- Add-in compiles clean (`SW2GZ.csproj` + `Test/SW2GZ.Writers.Test.csproj`).
- Live SW check on `FULL_ARM.SLDASM`: build a 3-level chain (base → mid →
  leaf) and a link with 2+ assigned mesh components; confirm correct
  placement/orientation in Preview (or RViz) — both the joint chain and the
  unioned mesh land where they should, not just "doesn't crash."
- DLL redeployed to `C:\Program Files\SW2GZ\SW2GZ.dll` after the live
  check passes.
