# Joints step: manual axis/pivot geometry pick (replaces mate-geometry auto-axis)

**Date:** 2026-07-03
**Mode:** Robot (v3)
**Branch:** `feat/robot-mode-v3`
**Status:** Approved via chat, ready for implementation plan.

## Problem

Three live-tested attempts at automatic mate-geometry-derived axis/pivot
(single-cylinder trust → dual-cylinder cross-check → ChooseBest
Concentric-graft) each fixed one real bug and surfaced another on the next
live test against `FULL_ARM.SLDASM`:

1. Mesh not rebased with the joint origin override (fixed).
2. Pivot direction right, position off — traced to trusting one cylinder
   blindly; added dual-cylinder cross-check as a diagnostic.
3. `ChooseBest` picked a limited Angle mate wholesale for its limit,
   discarding a co-existing Concentric mate's exact axis in favor of
   Angle's approximate plane-cross-product one — fixed by grafting.
4. Still wrong after the graft fix, on an ALREADY-suggested joint — because
   `IsSuggested` permanently opts a joint out of re-suggestion, so
   improving the classifier doesn't retroactively fix joints suggested
   before the fix landed. Per systematic-debugging's guidance (3+ fix
   attempts on the same mechanism → question the architecture, don't
   attempt a 4th), this phase replaces the mechanism instead of patching it
   again.

## Design

**Type and Limit stay automatic** (mate type + `MinimumVariation`/
`MaximumVariation` → `UrdfJointType` + limits) — this classification never
needed geometry and has not been the source of any of the four bugs above.

**Axis and pivot become a direct, manual geometry pick.** The Joints step
gets a new `Selectionbox` control: "Axis reference — click a cylindrical
face or straight edge in the viewport." Filtered to `swSelFACES` +
`swSelEDGES`. On selection:

- **Cylindrical face** → axis = the face's cylinder axis direction; pivot
  point = a point on that axis (`ISurface.CylinderParams`, same extraction
  already proven in `SwMateJointResolver.TryExtractCylinderLocal` — reused
  verbatim, just triggered by a direct pick instead of a mate walk).
- **Straight edge** → axis = `normalize(endPoint - startPoint)`; pivot
  point = the start point. (`IEdge.GetStartPoint()`/`GetEndPoint()` — two
  `double[3]` arrays, standard SW API; no curve-type checking needed since
  any edge that isn't a line will produce a direction the user can see is
  wrong and shouldn't have picked.)

Both are PART-LOCAL — transformed to assembly frame via the picked
entity's owning component's pose, resolved through the same
`IComponentPoses.GetPose` abstraction already used everywhere else in this
codebase (never raw `Transform2.ArrayData`, per memory
`sw-mathtransform-column-major`). Owning component is resolved via
`ISelectionMgr.GetSelectedObjectsComponent4(index, mark)` — a standard SW
API for "which component owns this picked sub-entity," **not yet used
anywhere in this codebase and not yet live-verified against this SW
version** (see "Live-test spike" below — this is the one genuinely new
uncertainty in this design and must be confirmed before the rest is built
on top of it).

Result feeds the existing `JointDef.SetAxis`/`SetMatePoint` — no model
changes needed, both already exist and already flow through
`Sw2gzRobotExporter`'s mesh/mass/origin rebase (already fixed, unaffected
by this change).

## What gets removed

The entire mate-geometry-for-axis subsystem, now dead once axis stops
coming from mate classification:

- `MateJointClassification.CylinderPair` / `PlanePair`, the Concentric/
  Angle/Distance geometry branches inside `Classify`, `AxisAgreementDot`/
  `OriginPerpendicularDistance`, and `ChooseBest`'s Concentric-graft logic.
  `Classify` shrinks to `(mateType, limitLower, limitUpper) → (Type,
  Limit)` — no geometry parameters at all.
- `SwMateJointResolver.TryExtractCylinderLocal`/`TryExtractPlaneLocal`, the
  dual-cylinder-agreement logging, `SelectPivotFace`/`FindMateFeatureByName`
  (superseded by the direct pick — no more "re-find the mate that
  produced this axis" since axis no longer comes from a mate at all).
- Joints step: `_jointPivotSourceLabel`/`_jointPivotSourceCombo`,
  `_currentJointCandidates`, `LoadPivotSourceCombo`, `HandlePivotSourceChanged`
  — the "which mate" ambiguity this solved doesn't exist once the user
  picks geometry directly.
- `HighlightJointPivotAxis`'s SW-face-reselect-by-mate-name — replaced by:
  the axis-reference Selectionbox itself IS the live highlight (SW's
  default selection outline on whatever's currently picked), no separate
  highlight call needed.

`ResolveAllCandidates` and per-mate walking for Type/Limit stay — only the
axis-geometry half goes.

## What stays manual either way

The Axis X/Y/Z numberboxes and Limit boxes are unchanged — the
Selectionbox pick WRITES into them (via the same `SetAxis`/commit path
already wired), it doesn't replace them. A user can still fine-tune by
typing after picking, or type from scratch without picking at all (e.g.
if the exact pivot has no clean face/edge to click). This keeps the
existing manual-entry path from Phase 1 as a fallback, per YAGNI — no new
UI needed for "manual override of the manual pick."

## Live-test spike (required before the rest is built)

`ISelectionMgr.GetSelectedObjectsComponent4` — confirm it resolves the
correct owning `Component2` for a face/edge picked inside `FULL_ARM.SLDASM`.
If it doesn't exist or doesn't resolve correctly in this interop version,
fallback: since the Selectionbox can be scoped to `SingleEntityOnly` and
the assembly is flat (no nested sub-assemblies, confirmed via
`sw_list_components` earlier this session), the owning component could
instead be inferred by testing membership against each `LinkDef`'s
assigned components — messier, only needed if the standard API fails.

## Testing

`MateJointClassification.Classify`/`ChooseBest` (now geometry-free) get
their existing pure tests trimmed to match the simplified signature — no
new pure logic to test (axis/pivot math is a straight cylinder-params/
edge-endpoints read, same proven transform pattern, not worth re-deriving
tests for). The COM-facing pick-and-transform code is not unit-testable
(same as the rest of `SwMateJointResolver`) — needs the live-test spike
above, then a full live check against `FULL_ARM.SLDASM` picking both a
cylindrical face and a straight edge before considering this done.
