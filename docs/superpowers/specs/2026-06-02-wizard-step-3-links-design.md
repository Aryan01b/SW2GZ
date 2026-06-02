# SW2GZ — Wizard Step 3 (Links) Design

**Status:** Approved design · **Date:** 2026-06-02 · **Type:** feature increment (part of P8 — UI wizard)

Step 3 of the native SW2GZ export PropertyManagerPage (`Sw2gzExportPmp`) lets the user
**define the robot's links**: the set of rigid bodies, each composed of one or more
SolidWorks components, with one designated base/root. It is the kinematics-free half of
the robot description (joints are Step 4). It reuses the proven live-viewport selection
pattern from `GeometryPropertyManager`.

Decisions (2026-06-02, with the user):
- **Seeding:** hybrid — auto-seed one link per top-level component, user edits/merges.
- **Granularity:** component-level (whole SW components, the rigid units of an assembly).
- **Scope:** name + assigned components + base flag + read-only mass readout + validation.
  Collision, materials, gazebo props, sensors, and manual inertial override are **separate
  later modules**, out of scope here.
- **UX:** reuse `GeometryPropertyManager`'s selection-box / assign-clear / prev-next flow,
  embedded as Step 3's group inside `Sw2gzExportPmp`.

---

## 1. What a link captures in Step 3

| Field | How | Notes |
|---|---|---|
| `Name` | seeded from component name, editable | sanitized via `RosNameSanitizer`, must be unique |
| `ComponentIds` | viewport pick → Assign | `Component2.Name2` strings (the id the geometry PMP already uses); 1..n = multi-body link |
| `IsBase` | checkbox, auto-guessed | exactly one link is the root |

Read-only feedback (not stored): combined **mass** (Σ component masses via `IMassProperties`)
+ component count + a "material missing" warning when a component's mass is 0. The full
inertia tensor is NOT computed in Step 3 — it is produced at export by the existing
`RobotModelBuilder` / `InertialAggregator` (parallel-axis + frame transform), so Step 3
stays fast and COM-light.

**Component identity:** `Component2.Name2` (instance name path). Stable within a saved
assembly across reopen; breaks if a component is renamed/replaced. Accepted limitation
(matches `GeometryPropertyManager`); persistent-reference ids are a future hardening.

## 2. Data model (pure, COM-free, serialized in the checkpoint)

Extend the checkpoint so Step 3 resumes. New serializable type (DataContract), added to
`Sw2gzExportConfig`:

```
[DataContract] LinkDef
    [DataMember] string Name
    [DataMember] List<string> ComponentIds   // Component2.Name2
    [DataMember] bool IsBase

Sw2gzExportConfig (additions)
    [DataMember] List<LinkDef> Links          // empty until Step 3 seeded/edited
```

`Sw2gzConfigCodec` already round-trips the whole config; `List<LinkDef>` rides along.
Round-trip test extended.

## 3. Validation (pure, testable)

New `LinkDefValidator.Validate(IReadOnlyList<LinkDef> links, IReadOnlyCollection<string>
allComponentIds)` → list of issues. Rules:

- Every id in `allComponentIds` assigned to **exactly one** link (report unassigned).
- No component id in **two** links (report duplicates).
- Link names **unique** and non-empty after sanitization.
- Exactly **one** `IsBase` link.
- No link with zero components (an empty link after a merge must be removed).

Pure (no COM) → unit-tested in the net8 project. The PMP feeds it the COM-derived
`allComponentIds`; the "Next" gate blocks on any blocking issue and a status label shows
the count (e.g. "2 components unassigned").

## 4. PMP Step 3 group (embedded in `Sw2gzExportPmp`, step index 2)

Mirrors `GeometryPropertyManager`:

- **Link selector** combobox + "Link i of N" progress label.
- **Selection box** (`swControlType_SelectionBox`) filtered to components/solid bodies;
  `OnSubmitSelection` rejects edges/faces; `SetSelectionFocus()` so viewport picks land in it.
- **Assign / Clear** buttons — Assign reads `Component2.Name2` from the selection box into
  the current `LinkDef` (via the same `SelectionMgr` mark technique), then refreshes mass.
- **Name** textbox (sanitized + committed on Assign).
- **"Set as base"** checkbox (`OnCheckboxCheck`) — checking one clears the others.
- **Add link / Remove link** buttons + **Prev / Next** link.
- **Mass readout** label + **validation status** label.

Seeding on first open (no checkpoint links): enumerate top-level components via
`((AssemblyDoc)model).GetComponents(true)`, skip suppressed, one `LinkDef` each
(`ComponentIds = [Name2]`, `Name = sanitize(component name)`), base = the fixed/grounded
component if detectable else the first. On resume: use the checkpoint's `Links`.

Auto-save on Next already persists the config (Steps 1–2 mechanism) — now including `Links`.

## 5. Reuse map

| Need | Existing piece |
|---|---|
| selection-box + assign/clear + prev/next | `GeometryPropertyManager` (pattern copied into Step 3) |
| link/body container | `LinkGeometry` shape → generalized into serializable `LinkDef` |
| name sanitization | `RosNameSanitizer` |
| combined mass / material check | `SolidWorksMassProperties` (`IMassProperties.Get`) |
| full inertia at export | `InertialAggregator` / `RobotModelBuilder` (unchanged) |
| top-level components | `AssemblyDoc.GetComponents(true)` |

## 6. Files

- **New:** `Build/Model/LinkDef.cs` (model), `Build/LinkDefValidator.cs` (validation),
  `Test/Build/LinkDefValidatorTests.cs`, `Test/URDFExport/...` extend config round-trip.
- **Edit:** `URDFExport/Sw2gzExportConfig.cs` (+`List<LinkDef> Links`),
  `Test/SW2GZ.Writers.Test.csproj` (+new sources), `SW2GZ.csproj` (+new sources),
  `URDFExport/Sw2gzExportPmp.cs` (Step 3 group + handlers + seeding).

## 7. Out of scope (later modules)

Collision strategy, material/color, gazebo friction/self_collide, sensors, manual inertial
override, joint/tree structure (Step 4), persistent-reference component ids.
