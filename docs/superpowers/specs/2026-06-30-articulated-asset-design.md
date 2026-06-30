# A1 — Articulated asset (1-DOF to world)

**Date:** 2026-06-30
**Mode:** Asset
**Status:** Self-authored under autonomous /loop (user pre-approved "implement
all, don't ask"). Backend-first staging (writer + config + exporter + tests);
doc/codec persistence + Asset-wizard UI deferred to the wizard work.

## Problem

Asset mode exports a single part as a static (or dynamic-but-free) Gz model.
The matrix calls for a **dynamic/articulated** asset — a door, lift, wheel, or
lever that moves on one axis. Rather than multi-part picking (a mini-robot),
A1 anchors the asset's single link to the **world frame** through one joint,
giving a 1-DOF moving prop.

## Scope (locked)

- One optional joint: `world` → the asset `link`, type none|fixed|revolute|
  continuous|prismatic.
- A joint to `world` is invalid on a static model, so any joint other than
  `none` forces the model **dynamic** (non-static + placeholder inertial).
- `JointType = none` (default) → **byte-identical** to current asset output.
- Pivot/axis is at the model origin (the exporter already centres X/Y and floors
  z=0), which is a sensible default for a hinge/slider.

Out of scope: multi-part articulation, real joint-origin offset, mass-properties
inertial, doc persistence, Asset-wizard UI. (Joint params are set on the config
directly for now; the wizard step wires them later, like the W3 lights list.)

## Joint semantics (SDF 1.10)

```xml
<joint name="joint" type="revolute">
  <parent>world</parent>
  <child>link</child>
  <axis>
    <xyz>0 0 1</xyz>
    <limit><lower>-1.5708</lower><upper>1.5708</upper></limit>
  </axis>
</joint>
```

- **fixed** — `type="fixed"`, no axis (rigidly anchors a dynamic body to world).
- **revolute** — `type="revolute"` + axis + `<limit>` (radians).
- **continuous** — `type="revolute"` + axis, **no `<limit>`** (free spin; SDF has
  no "continuous" type — an unlimited revolute is the idiom).
- **prismatic** — `type="prismatic"` + axis + `<limit>` (metres).

## Code seams

| Action | File | Change |
|---|---|---|
| EDIT | `Gz/SdfAssetModelWriter.cs` | `SdfAssetModelInput` gains `JointType="none"`, `JointAxisX/Y/Z`, `JointLower/Upper`. Emit the `<joint>` after `</link>` when type≠none. Force the visible static flag off when a joint is present. |
| EDIT | `URDFExport/Sw2gzExportConfig.cs` | `AssetJointType` ("none"), `AssetJointAxisX/Y/Z` (0,0,1), `AssetJointLower` (-1.5708), `AssetJointUpper` (1.5708) — DataMember + OnDeserializing + clone. |
| EDIT | `URDFExport/Sw2gzAssetExporter.cs` | Map config → input; a joint forces `IsStatic=false` + `Mass=1.0`. |
| TEST | `Test/Writers/TestSdfAssetModelWriter.cs`, `Test/URDFExport/Sw2gzAssetExporterTests.cs` | none=byte-identical; each joint type; joint forces dynamic. |

## Definition of done

- New + existing tests green.
- `JointType=none` asset output byte-identical (verified by existing asset
  golden/Contains tests staying green).
- Add-in compiles; commit on `feat/world-sensors`.
