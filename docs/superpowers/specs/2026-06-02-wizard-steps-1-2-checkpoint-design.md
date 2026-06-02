# SW2GZ — Wizard Steps 1–2 + Checkpoint Persistence

**Status:** Approved design · **Date:** 2026-06-02 · **Type:** feature increment (part of P8 — UI wizard)

Builds on the navigation shell in `URDFExport/Sw2gzExportPmp.cs` (5-step PMP wizard:
Mode → Output → Geometry → Joints → Review, currently placeholder labels only). This
increment makes **Step 1 (Mode)** and **Step 2 (Output)** real, and adds **checkpoint
persistence**: the wizard's working state is auto-saved into the SolidWorks document
tree on each Next, so reopening the assembly and clicking the SW2GZ button **resumes**
the configuration.

The "instance in tree" mechanism is the same one the legacy exporter already uses for
the URDF link tree (`URDFExport/ConfigurationSerialization.cs`): a named SolidWorks
`Attribute` feature carrying a DataContract-serialized string. We add a **new, dedicated**
attribute for the wizard config rather than overloading the legacy one.

---

## 1. Persisted config model — `Sw2gzExportConfig`

New `[DataContract]` class (in `URDFExport/`), serialized exactly like the legacy `Link`
tree (DataContractSerializer → ASCII string → SW Attribute parameter):

| `[DataMember]` | Type | Source step | Notes |
|---|---|---|---|
| `Mode` | `SW2GZ.Ros2.ExportMode` | Step 1 | RobotPackage / SdfModel / SdfWorld |
| `OutputFolder` | `string` | Step 2 | target output directory |
| `PackageName` | `string` | Step 2 | sanitized via `PackageNameSanitizer` at export time |
| `Author` | `string` | Step 2 | → `RobotMeta` / `package.xml` |
| `Email` | `string` | Step 2 | → `RobotMeta` / `package.xml` |
| `License` | `string` | Step 2 | → `RobotMeta` / `package.xml` |
| `LastStep` | `int` | — | resume position (0-based step index) |

Pure, COM-free, unit-testable (round-trip serialization). Extensible: Geometry/Joints/
Review steps add their own members in later increments. At export time it maps onto the
existing `TargetProfile` (Mode) and `RobotMeta` (name/author/email/license).

ROS 2 distro and Gz version are **not** stored or editable — v2.0 locks them to Jazzy +
Harmonic (see `Ros2/TargetProfile.cs` header). They appear in Step 2 as read-only labels.

## 2. New serializer — `Sw2gzConfigSerialization`

Mirrors `ConfigurationSerialization` but for `Sw2gzExportConfig`, writing a **new**
attribute feature named `"SW2GZ Export Configuration (v1)"`:

- `Save(SldWorks swApp, ModelDoc2 model, Sw2gzExportConfig config)` — DataContract-
  serialize → `SaveDataToModelDoc` (define attribute with `data` / `date` / `version`
  params, `CreateInstance5`, reuse if present). Same attribute plumbing as the legacy
  serializer.
- `Load(ModelDoc2 model, out bool error) → Sw2gzExportConfig` — find the attribute by
  name, read `data`, deserialize. Returns a fresh default config (Mode=RobotPackage,
  empty fields, LastStep=0) when no attribute exists.

**Legacy attribute is left untouched** — not read, not deleted. The old `"URDF Export
Configuration (v1.4)"` feature stays physically in the tree; full migration of its
link-tree data is deferred to when Steps 3–4 (Geometry/Joints) are implemented (tracked
under P7). This increment only adds the new attribute.

## 3. Checkpoint trigger — auto-save on Next

`Sw2gzExportPmp` gains a `ModelDoc2` reference (the active assembly), passed in from
`SwAddin.LaunchWizard` (which already resolves and validates `ActiveDoc` as an assembly).

- `GoNext` writes the current config (`Sw2gzConfigSerialization.Save`) **before** advancing.
- Finish also saves.
- No save button — saving is implicit on step transition (per design decision).

## 4. Resume on open

In the constructor (after `BuildPage`) / `AfterActivation`, the wizard calls
`Sw2gzConfigSerialization.Load`, seeds the Step 1/2 controls from the loaded config, and
calls `ShowStep(config.LastStep)`. Reopening the assembly and clicking SW2GZ resumes.

## 5. Controls (replacing placeholder labels)

`BuildPage` is refactored to dispatch per-step builders: `BuildModeStep`, `BuildOutputStep`,
and the existing generic placeholder for steps 3–5.

- **Step 1 — Mode:** three radio buttons (`swControlType_Option`), mutually exclusive,
  handled in `OnOptionCheck(int Id)` → sets `config.Mode`. This choice defines which
  files/folders the export later generates:
  - "Robot package (URDF/Xacro)" → `ExportMode.RobotPackage`
  - "Gz asset (SDF model)" → `ExportMode.SdfModel`
  - "Gz world (SDF world)" → `ExportMode.SdfWorld`
- **Step 2 — Output:**
  - folder textbox (`swControlType_Textbox`) + "Browse…" button (`swControlType_Button`)
    opening `System.Windows.Forms.FolderBrowserDialog`;
  - package-name textbox;
  - author / email / license textboxes;
  - read-only labels: "ROS 2: Jazzy", "Gz Sim: Harmonic".
  - text edits handled in `OnTextboxChanged(int Id, string Text)`.

**Control IDs:** widen the per-step stride from 10 to 20 (`StepIdBase + step*20 + offset`)
to fit Step 2's ~10 controls. Fixed IDs (header, nav) unchanged.

## 6. Files touched

- **New:** `URDFExport/Sw2gzExportConfig.cs` (model), `URDFExport/Sw2gzConfigSerialization.cs`
  (serializer, `#if SW_INTEROP` for the COM/ModelDoc parts).
- **Edit:** `URDFExport/Sw2gzExportPmp.cs` (controls, handlers, load/save wiring, ctor
  takes `ModelDoc2`), `SW/SwAddin.cs` (`LaunchWizard` passes the assembly `ModelDoc2`).
- **Tests:** `Sw2gzExportConfig` round-trip serialization (pure, no COM).

## 6a. Architecture decision (2026-06-02)

The pure-C# MVVM layer (`UI/ViewModels/WizardViewModel` + step VMs, `UI/Services/*`)
has **no view wired to it** (nothing instantiates `WizardViewModel`). Decision: the
**native PMP (`Sw2gzExportPmp`) is the sole UI** going forward. Step 1/2 + checkpoint are
built **self-contained** on `Sw2gzExportConfig` (above), NOT bound to the MVVM VMs.
Retiring/deleting the unused MVVM layer is tracked as a separate follow-up (out of scope
here). Metadata fields (author/email/license) therefore live on `Sw2gzExportConfig`, not
on `OutputStepViewModel`.

## 7. Out of scope

- Steps 3–5 (Geometry/Joints/Review) real controls.
- Migrating / deleting the legacy URDF link-tree attribute (P7).
- Wiring the saved config into an actual export run (Finish currently just closes).
- Validation of folder/package-name beyond existing sanitizers (live validation later).
