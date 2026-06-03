# Modular Stack Ribbon — D2 (Ribbon Flyout + Export Routing) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`).

**Goal:** Make the persisted `StackProfile` actually drive the ribbon export, and let the user set it from a ribbon **Stacks ▼ flyout** (on/off + actuation radio), per-assembly.

**Architecture:** Split the work so the *decision logic* is pure and unit-tested, and the COM surface stays thin:
1. `StackFlyoutModel` — pure, COM-free: maps a `StackProfile` to per-item check state and applies a click to produce a new profile (toggles + actuation radio). Fully unit-tested.
2. Export routing — `Sw2gzModelExporter` passes `config.Stacks` into `Sw2gzPipeline.Run(profile)` instead of the hardcoded `modelOnly: true`.
3. COM flyout — `SwAddin` creates a SolidWorks flyout group whose populate-callback reads the **active** assembly's `StackProfile` and renders items via `StackFlyoutModel`; item callbacks apply the click and save back. Because SW repopulates the flyout via callback on every open, per-document state is inherent — **no ActiveDocChange plumbing needed**.

**Tech Stack:** C# .NET Framework 4.8.1 (`SW2GZ`, COM parts under `#if SW_INTEROP`), net8.0 xUnit. SolidWorks flyout API: `ICommandManager.CreateFlyoutGroup2` / `IFlyoutGroup.AddCommandItem` / enable-callback return codes (0 deselect+disable, 1 deselect+enable, 2 select+disable, 3 select+enable).

**Checkpoint / revert:** `git reset --hard checkpoint/pre-modular-ribbon` (wipes D1+D2). D1 commits are already in; D2 builds on them.

**Commenting requirement (user):** every new type/method/branch gets an intent comment. Reviewers reject thin commenting.

**Build/test notes for implementers:**
- Tests: `dotnet test Test/SW2GZ.Writers.Test.csproj` (currently 560 green).
- Both csprojs use EXPLICIT compile lists; the **Test** project source-links individual `SW2GZ\...` files (e.g. `Ros2\StackProfile.cs`). A new pure file under `SW2GZ/Ros2` used by tests must be added to BOTH `SW2GZ/SW2GZ.csproj` and `Test/SW2GZ.Writers.Test.csproj` (mirror the StackProfile.cs entries). The Test project auto-globs its OWN `Test\...` test files.
- COM build (Task 3 only): `& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" SW2GZ\SW2GZ.csproj /p:Configuration=Release /p:SolutionDir=C:\aryan\SW2GZ\ /t:Build /v:m`. PostBuild regasm MSB3216 and bin-copy MSB3027 (SolidWorks open) are EXPECTED/non-fatal — compile success = pass.
- `Sw2gzModelExporter.cs` and `SwAddin.cs` are `#if SW_INTEROP` / COM and are NOT in the test project; verify them via the Framework MSBuild compile, not `dotnet test`.

---

### Task 1: `StackFlyoutModel` — pure flyout decision logic

**Files:**
- Create: `SW2GZ/Ros2/StackFlyoutModel.cs`
- Modify: `SW2GZ/SW2GZ.csproj` + `Test/SW2GZ.Writers.Test.csproj` (compile includes, mirror StackProfile.cs)
- Test: `Test/URDFExport/StackFlyoutModelTests.cs`

**Semantics:**
- Items: `GazeboSim` (toggle `GzSim`), `ActuationNone` / `ActuationGzPlugin` / `ActuationRos2Control` (radio → set `Actuation`), `Sensors` (toggle `SensorsEnabled`).
- Bridge is auto-derived downstream (not a manual item in v1) — do NOT add a bridge item.
- `IsChecked`: GazeboSim↔GzSim; each Actuation* item checked iff `profile.Actuation` equals that backend; Sensors↔SensorsEnabled.
- `Apply`: a toggle flips its bool; an actuation radio item SETS `Actuation` to that backend (selecting one deselects the others by construction). Returns a NEW `StackProfile` (do not mutate input — copy all fields).

- [ ] **Step 1: Write failing tests**

```csharp
// Test/URDFExport/StackFlyoutModelTests.cs
using SW2GZ.Ros2;
using Xunit;

namespace Test.URDFExport
{
    public class StackFlyoutModelTests
    {
        [Fact]
        public void IsChecked_ReflectsProfile()
        {
            var p = new StackProfile { GzSim = true, Actuation = ActuationBackend.Ros2Control, SensorsEnabled = false };
            Assert.True(StackFlyoutModel.IsChecked(p, StackFlyoutItem.GazeboSim));
            Assert.True(StackFlyoutModel.IsChecked(p, StackFlyoutItem.ActuationRos2Control));
            Assert.False(StackFlyoutModel.IsChecked(p, StackFlyoutItem.ActuationGzPlugin));
            Assert.False(StackFlyoutModel.IsChecked(p, StackFlyoutItem.ActuationNone));
            Assert.False(StackFlyoutModel.IsChecked(p, StackFlyoutItem.Sensors));
        }

        [Fact]
        public void Apply_ActuationRadio_SetsBackend_DeselectsOthers()
        {
            var p = StackProfile.Default(); // Ros2Control
            StackProfile r = StackFlyoutModel.Apply(p, StackFlyoutItem.ActuationGzPlugin);
            Assert.Equal(ActuationBackend.GzPlugin, r.Actuation);
            Assert.True(StackFlyoutModel.IsChecked(r, StackFlyoutItem.ActuationGzPlugin));
            Assert.False(StackFlyoutModel.IsChecked(r, StackFlyoutItem.ActuationRos2Control));
        }

        [Fact]
        public void Apply_Toggle_FlipsBool_DoesNotMutateInput()
        {
            var p = StackProfile.Default(); // GzSim true, Sensors false
            StackProfile r = StackFlyoutModel.Apply(p, StackFlyoutItem.Sensors);
            Assert.True(r.SensorsEnabled);
            Assert.False(p.SensorsEnabled); // input unchanged

            StackProfile r2 = StackFlyoutModel.Apply(p, StackFlyoutItem.GazeboSim);
            Assert.False(r2.GzSim);
            Assert.True(p.GzSim); // input unchanged
        }

        [Fact]
        public void Label_NonEmptyForEveryItem()
        {
            foreach (StackFlyoutItem it in System.Enum.GetValues(typeof(StackFlyoutItem)))
                Assert.False(string.IsNullOrWhiteSpace(StackFlyoutModel.Label(it)));
        }
    }
}
```

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter StackFlyoutModelTests`
Expected: FAIL — type missing.

- [ ] **Step 3: Implement `StackFlyoutModel.cs`**

```csharp
/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

StackFlyoutModel — the pure, COM-free brain of the ribbon "Stacks" flyout. It
maps a StackProfile to per-item check state (IsChecked) and applies a flyout
click to produce a NEW profile (Apply). Keeping this logic out of the SolidWorks
COM layer means the flyout's behaviour is fully unit-tested; SwAddin only does
the thin COM glue (render items, load/save the active assembly's profile).

Actuation is a radio: the three Actuation* items are mutually exclusive because
they all write the single StackProfile.Actuation enum (selecting one implicitly
deselects the others). GazeboSim and Sensors are independent toggles. Bridge is
auto-derived downstream and is intentionally NOT a flyout item in v1.
*/
namespace SW2GZ.Ros2
{
    // The selectable rows in the Stacks flyout, in display order.
    public enum StackFlyoutItem
    {
        GazeboSim,             // toggle: build for Gz simulation
        ActuationNone,         // radio: no actuation backend
        ActuationGzPlugin,     // radio: Gz native plugins
        ActuationRos2Control,  // radio: gz_ros2_control
        Sensors,               // toggle: emit Gz sensor blocks + bridge entries
    }

    public static class StackFlyoutModel
    {
        // Whether the given flyout row should render with a checkmark for this
        // profile. Toggles reflect their bool; actuation rows reflect equality
        // with the profile's single Actuation backend.
        public static bool IsChecked(StackProfile p, StackFlyoutItem item)
        {
            switch (item)
            {
                case StackFlyoutItem.GazeboSim:            return p.GzSim;
                case StackFlyoutItem.ActuationNone:        return p.Actuation == ActuationBackend.None;
                case StackFlyoutItem.ActuationGzPlugin:    return p.Actuation == ActuationBackend.GzPlugin;
                case StackFlyoutItem.ActuationRos2Control: return p.Actuation == ActuationBackend.Ros2Control;
                case StackFlyoutItem.Sensors:             return p.SensorsEnabled;
                default:                                  return false;
            }
        }

        // Apply a click on `item` to `p`, returning a NEW profile (input is never
        // mutated — callers persist the result). Toggle rows flip their bool;
        // actuation rows set the single backend (radio behaviour).
        public static StackProfile Apply(StackProfile p, StackFlyoutItem item)
        {
            // Copy every field so the returned profile is independent of the input.
            var next = new StackProfile
            {
                GzSim = p.GzSim,
                Actuation = p.Actuation,
                SensorsEnabled = p.SensorsEnabled,
            };

            switch (item)
            {
                case StackFlyoutItem.GazeboSim:            next.GzSim = !p.GzSim; break;
                case StackFlyoutItem.Sensors:             next.SensorsEnabled = !p.SensorsEnabled; break;
                case StackFlyoutItem.ActuationNone:        next.Actuation = ActuationBackend.None; break;
                case StackFlyoutItem.ActuationGzPlugin:    next.Actuation = ActuationBackend.GzPlugin; break;
                case StackFlyoutItem.ActuationRos2Control: next.Actuation = ActuationBackend.Ros2Control; break;
            }
            return next;
        }

        // Human-readable row label shown in the flyout.
        public static string Label(StackFlyoutItem item)
        {
            switch (item)
            {
                case StackFlyoutItem.GazeboSim:            return "Gazebo sim";
                case StackFlyoutItem.ActuationNone:        return "Actuation: none";
                case StackFlyoutItem.ActuationGzPlugin:    return "Actuation: Gz plugin";
                case StackFlyoutItem.ActuationRos2Control: return "Actuation: ros2_control";
                case StackFlyoutItem.Sensors:             return "Sensors";
                default:                                  return item.ToString();
            }
        }
    }
}
```

- [ ] **Step 4: Run — verify pass**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter StackFlyoutModelTests` → PASS (4/4). Then full suite → 560 + 4 = 564, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add SW2GZ/Ros2/StackFlyoutModel.cs Test/URDFExport/StackFlyoutModelTests.cs SW2GZ/SW2GZ.csproj Test/SW2GZ.Writers.Test.csproj
git commit -m "feat: add pure StackFlyoutModel (flyout check-state + click logic)"
```
No AI co-author line.

---

### Task 2: Route `config.Stacks` into the ribbon export

**Files:**
- Modify: `SW2GZ/URDFExport/Sw2gzModelExporter.cs`

**Problem:** `Sw2gzModelExporter.Run` calls the pipeline with a hardcoded `modelOnly: true`, so every ribbon export is bare regardless of the saved profile. Replace it with the persisted `config.Stacks`.

This file is `#if SW_INTEROP` (not in the test project). Behaviour is proven by the D1 pipeline tests (`DefaultProfile_EmitsFullStack` / `ModelOnlyProfile_OmitsControlAndPlugins`), so verification here is: (a) the Framework MSBuild compile succeeds, and (b) code review confirms the one-line routing + null-guard.

- [ ] **Step 1: Change the pipeline call** in `Sw2gzModelExporter.Run`:

Replace:
```csharp
            return new Sw2gzPipeline(mass, walker, tess, appearances).Run(
                config.OutputFolder, config.PackageName, config.Author, config.Email, config.License,
                System.Array.Empty<SensorDef>(), modelOnly: true);
```
with:
```csharp
            // Drive the export from the assembly's persisted stack selection
            // (Stacks). Defensive default: an older config that somehow carries a
            // null profile falls back to the full stack rather than throwing.
            SW2GZ.Ros2.StackProfile profile = config.Stacks ?? SW2GZ.Ros2.StackProfile.Default();

            return new Sw2gzPipeline(mass, walker, tess, appearances).Run(
                config.OutputFolder, config.PackageName, config.Author, config.Email, config.License,
                System.Array.Empty<SensorDef>(), profile);
```

- [ ] **Step 2: Compile the add-in (Framework MSBuild)**

Run the MSBuild command above. Expected: `SW2GZ -> ...SW2GZ.dll` (compile clean; regasm/bin-lock non-fatal).

- [ ] **Step 3: Commit**

```bash
git add SW2GZ/URDFExport/Sw2gzModelExporter.cs
git commit -m "feat: route persisted StackProfile into ribbon export (replace hardcoded modelOnly)"
```
No AI co-author line.

---

### Task 3: COM flyout in `SwAddin`

**Files:**
- Modify: `SW2GZ/SW/SwAddin.cs`

**This is COM/UI — not unit-testable. Verification = Framework compile + a manual SolidWorks smoke test by the user.** Keep the glue thin; all decisions go through `StackFlyoutModel`.

**Design:**
- Add a flyout group (id `flyoutGroupID = 91`, already reserved) via `CmdMgr.CreateFlyoutGroup2`, with a populate callback `FlyoutCallback` and an enable method.
- `FlyoutCallback` (invoked by SW each time the flyout opens): clear/add one `AddCommandItem` per `StackFlyoutItem` in enum order, label from `StackFlyoutModel.Label`, each wired to a single click handler `OnFlyoutItemClick` carrying the item index, and an enable/state method that returns the checked code from `StackFlyoutModel.IsChecked` (3 = select+enable when checked, 1 = deselect+enable when not).
- Reading state: the callback loads the **active** assembly's profile via `Sw2gzConfigSerialization.Load(activeDoc).Stacks` — so the flyout always shows the active robot's selection (per-doc state for free; no ActiveDocChange handler).
- Click: `OnFlyoutItemClick(item)` → load config → `config.Stacks = StackFlyoutModel.Apply(config.Stacks, item)` → `Sw2gzConfigSerialization.Save(...)`. Guard: active doc must be an assembly (reuse the `WizardEnable`-style check); if not, no-op.
- Place the flyout on the existing assembly command tab box next to Create Model + Export (add `flyout.CmdID` to the `box.AddCommands` id array, with text-type below). Best-effort inside the existing try/catch.
- Add an intent comment block above the flyout setup explaining the callback-repopulate-per-open model and why no ActiveDocChange sync is needed.

- [ ] **Step 1: Implement the flyout** in `SwAddin.cs`:
  1. In `AddCommandMgr` (after the command group is activated, inside the existing tab try-block or just after it), create the flyout group and add its command to the tab box. Use the existing `Sw2gzIconList()`/`Sw2gzStripIconList()` for icons.
  2. Add public callback methods (invoked by SW via reflection, so must be `public`): `FlyoutCallback()` (populate), `OnFlyoutItemClick0..N` OR a small set — NOTE: SolidWorks flyout item callbacks are referenced by method NAME string. Use one distinct public method per item (e.g. `FlyoutClickGazeboSim`, `FlyoutClickActNone`, `FlyoutClickActGz`, `FlyoutClickActRos2`, `FlyoutClickSensors`) each delegating to a private `ApplyFlyoutClick(StackFlyoutItem)`, and one enable method per item (e.g. `FlyoutEnableGazeboSim` ... returning 3/1 from `StackFlyoutModel.IsChecked`). This mirrors how `AddCommandItem2` takes string callback + enable names.
  3. `private void ApplyFlyoutClick(StackFlyoutItem item)`: guard active doc is assembly; `Load` → `Apply` → `Save`; log; swallow+log exceptions (never throw into the COM caller).
  4. `private int FlyoutItemState(StackFlyoutItem item)`: load active assembly profile (fall back to `StackProfile.Default()` if no doc/config); `return StackFlyoutModel.IsChecked(profile, item) ? 3 : 1;` Wrap in try/catch returning 1 on failure.

Implementer: consult the SolidWorks API for the exact `CreateFlyoutGroup2` signature available in this interop version (params: id, name, tooltip, hint, smallIcons, largeIcons, callbackFunction, enableMethod). Match the icon-list shape already used. If `CreateFlyoutGroup2` is unavailable, use `CreateFlyoutGroup`. Keep everything best-effort: if flyout creation fails, log a warning and leave the two existing buttons working (mirror the existing tab-placement try/catch philosophy).

- [ ] **Step 2: Compile the add-in (Framework MSBuild)**

Run the MSBuild command. Expected: compile clean (`SW2GZ -> ...SW2GZ.dll`). Fix any COM signature mismatches until it compiles. regasm/bin-lock non-fatal.

- [ ] **Step 3: Commit**

```bash
git add SW2GZ/SW/SwAddin.cs
git commit -m "feat: add Stacks flyout to ribbon (per-assembly stack toggles via StackFlyoutModel)"
```
No AI co-author line.

- [ ] **Step 4: Manual verification (USER, in SolidWorks)** — document the steps in the task report for the user to run:
  1. Close SolidWorks, run elevated `regasm /codebase SW2GZ.dll` (or the installer), reopen.
  2. Open an assembly → SW2GZ tab → click **Stacks ▼**: see Gazebo sim / Actuation (none/Gz/ros2_control) / Sensors, with checkmarks matching the saved profile (default: Gazebo sim + Actuation ros2_control checked).
  3. Toggle Actuation → ros2_control vs none; Sensors on. Reopen the flyout → checkmarks persisted.
  4. Run **Export** → confirm output matches the selection (ros2_control → control/plugin files present; none → bare model). Switch to a second assembly → flyout reflects ITS own profile.

---

## Done-when (D2)

- `StackFlyoutModel` pure + unit-tested (4 tests green; full suite 564/0).
- Ribbon export driven by `config.Stacks` (no more hardcoded `modelOnly`).
- Stacks flyout compiles into the add-in; manual SW smoke test steps handed to the user.
- D3 (Configure PMP for detail params: controller types, gz world, sensor rates) remains.
