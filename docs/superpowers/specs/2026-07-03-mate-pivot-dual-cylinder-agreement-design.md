# Mate-pivot dual-cylinder agreement check (Phase 2 follow-up)

**Date:** 2026-07-03
**Mode:** Robot (v3)
**Branch:** `feat/robot-mode-v3`
**Status:** Approved via chat, implemented same session.

## Problem

Live-testing the Phase 2 mate-suggestion feature (`FULL_ARM.SLDASM`,
base_link ↔ link_1 Concentric mate) surfaced two distinct bugs:

1. **Mesh/mass not rebased with the joint origin override.** Fixed
   separately (see `Sw2gzRobotExporter.cs` — `frameOrigin` now anchors
   mesh, mass, AND joint `<origin>` together, not just the joint math).
2. **Child link's rotation pivot doesn't coincide with the parent's marked
   axis** — axis direction correct (parallel), but position off. Root
   cause: `SwMateJointResolver.TryExtractCylinderLocal` only ever reads
   ONE side of a Concentric mate (parent's cylinder, falling back to
   child's only if parent's entity isn't a real cylindrical face) and
   trusts it blindly. No cross-check exists — a bug in either extraction
   path (wrong entity, wrong pose lookup) or a genuinely ambiguous
   multi-hole part goes undetected.

## Design

A satisfied Concentric mate geometrically **forces** both mated
cylinders' axes onto the exact same 3D line — that is what the constraint
solve guarantees. So instead of reading one side and hoping, read both and
verify:

- `SwMateJointResolver` now extracts cylinder geometry from **both** the
  parent-side and child-side mated faces (previously: parent, with
  fallback to child only if parent extraction failed).
- `MateJointClassification.CylinderPair` (new, mirrors the existing
  `PlanePair` shape) carries both sides' local origin/axis + each side's
  own component rotation/translation into the pure classifier.
- `Classify` transforms both sides into assembly frame independently, then
  — only when both sides produced a real cylinder — computes:
  - `AxisAgreementDot` = `|dot(parentAxis, childAxis)|` (should be ~1.0)
  - `OriginPerpendicularDistance` = perpendicular distance from the
    child's origin point to the parent's axis line (should be ~0)
- The **chosen** axis/origin is unchanged (still parent-preferred, falls
  back to child-only if parent's side isn't a real cylinder) — this check
  never changes behavior on its own, it only adds a diagnostic.
- `SwMateJointResolver` (the impure, logger-owning layer) checks the two
  new fields against a tolerance (`AxisAgreementDot < 0.999` i.e. ~2.5°,
  or `OriginPerpendicularDistance > 0.001` i.e. 1mm) and `logger.Warn`s
  with the mate name and both components if they disagree — turning a
  silent wrong pivot into a loud, actionable log line instead of a guess.
- `MateJointClassification` stays pure/COM-free — it reports numbers, it
  doesn't decide whether to warn. That decision (and the logger) belongs
  to `SwMateJointResolver`.

## Non-goals (unchanged from prior discussion)

- **Multiple mates spanning the same link pair** (a link with 2+ separate
  Concentric mates to its parent) — still handled by the existing
  `ChooseBest` heuristic (limit-bearing wins, else first-seen). Explicitly
  deferred; the planned manual reference-geometry override (Phase 2 Task
  6, still pending) is the intended escape hatch for this case, not a new
  picker UI.
- **Exact-face highlight instead of point-proximity `TEMPAXIS` guessing**
  — still a known gap (Task 5's spike notes), not addressed by this
  change. Separate follow-up.

## Testing

`MateJointClassificationTests.cs` gained 3 new pure unit tests:
agreement-when-both-sides-match, disagreement-when-parallel-but-offset,
and fields-stay-null-when-only-one-side-extracted. All 508 tests green.
Live verification (does the warning actually fire against `FULL_ARM`'s
real Concentric mates, and does the pivot look right after this) is the
next step, per this project's standing "green tests are necessary but not
sufficient for COM-touching changes" rule.
