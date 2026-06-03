# Modular Stack Ribbon — D2b (Ribbon Stacks Section + Per-Stack Config) Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`).

**Why this supersedes the flyout:** After seeing the "Stacks ▼" flyout, the user changed the UX: each stack gets its **own ribbon button** in a **dedicated ribbon section**; clicking a button opens that stack's **config wizard** (not a toggle). The buttons are **disabled until Create Model has run** and **disabled unless ExportMode == RobotPackage** (greyed for World/Assets). This replaces the flyout (commit `3196536`) and the discarded `StackFlyoutModel` (commit `ac338db`). The D1 foundation (`StackProfile`, persistence, pipeline routing — commits `0bf867b`/`b8cd3bb`/`a02a661`/`3b815e3`/`826f3f7`) all stay.

**Goal:** A "Stacks" ribbon section with four wizard-launching buttons (Actuation, Sensors, Gazebo/World, Bridge), each opening a focused WinForms config dialog that edits the assembly's persisted `StackProfile`; buttons gated on Create-Model-done + RobotPackage mode.

**Tech Stack:** C# .NET Framework 4.8.1 (`SW2GZ`, COM under `#if SW_INTEROP`), WinForms dialogs (mirror `SW2GZ/UI/ExportDialog`), net8.0 xUnit. SolidWorks ribbon: extra `AddCommandItem2` items in command group `92` + a second `CommandTabBox` for the section; per-item enable callback.

**Checkpoint / revert:** `git reset --hard checkpoint/pre-modular-ribbon`.

**Commenting requirement (user):** every new type/method/branch gets an intent comment.

**Build/test notes:** Tests `dotnet test Test/SW2GZ.Writers.Test.csproj`. Both csprojs explicit compile lists; pure SW2GZ files used by tests must be registered in BOTH (mirror `Ros2\StackProfile.cs`). COM compile: `& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" SW2GZ\SW2GZ.csproj /p:Configuration=Release /p:SolutionDir=C:\aryan\SW2GZ\ /t:Build /v:m` — MSB3216 (regasm) / MSB3027 (bin-lock) non-fatal; compile success = pass. COM files (`SwAddin.cs`, dialogs, `#if SW_INTEROP`) verified by compile + user manual test, not `dotnet test`.

---

### Task 1: Extend `StackProfile` (BridgePlan + copy-ctor) — additive only

**Files:**
- Modify: `SW2GZ/Ros2/StackProfile.cs`
- Test: `Test/URDFExport/StackProfileTests.cs` (add copy-ctor + Bridge tests)

**Why:** Add a `BridgePlan` so the Bridge button has real state, and a copy-constructor so dialogs edit a clone and commit on OK (and future fields aren't dropped — the reviewer's D3 note).

**ORDERING NOTE:** `StackFlyoutModel` (the discarded flyout helper) is NOT deleted here — `SwAddin`'s flyout still references it. It is removed together with the flyout in Task 4, so every intermediate commit keeps compiling (both `dotnet test` AND the Framework COM build).

- [ ] **Step 1: Write failing tests** (append to `StackProfileTests`):

```csharp
[Fact]
public void CopyCtor_CopiesAllFields()
{
    var src = new StackProfile {
        GzSim = false, Actuation = ActuationBackend.GzPlugin, SensorsEnabled = true,
        Bridge = new BridgePlan { Clock = false, Tf = false, JointStates = false, CmdVel = true, Odom = true }
    };
    var copy = new StackProfile(src);
    Assert.False(copy.GzSim);
    Assert.Equal(ActuationBackend.GzPlugin, copy.Actuation);
    Assert.True(copy.SensorsEnabled);
    Assert.True(copy.Bridge.CmdVel);
    Assert.True(copy.Bridge.Odom);
    Assert.False(copy.Bridge.Clock);
    // mutating the copy must not touch the source (deep copy of Bridge)
    copy.Bridge.CmdVel = false;
    Assert.True(src.Bridge.CmdVel);
}

[Fact]
public void Default_BridgeHasSaneDefaults()
{
    var p = StackProfile.Default();
    Assert.True(p.Bridge.Clock);
    Assert.True(p.Bridge.Tf);
    Assert.True(p.Bridge.JointStates);
    Assert.False(p.Bridge.CmdVel);
    Assert.False(p.Bridge.Odom);
}
```

- [ ] **Step 2: Run — fail.** `dotnet test Test/SW2GZ.Writers.Test.csproj --filter StackProfileTests` → FAIL (BridgePlan / copy-ctor missing).

- [ ] **Step 3: Implement.** Add to `StackProfile.cs`:

```csharp
    // Per-topic ros_gz_bridge selection. Defaults mirror the always-bridged core
    // topics (clock/tf/joint_states); cmd_vel + odom are opt-in (mobile robots).
    [DataContract(Name = "BridgePlan", Namespace = "")]
    public sealed class BridgePlan
    {
        [DataMember] public bool Clock { get; set; } = true;
        [DataMember] public bool Tf { get; set; } = true;
        [DataMember] public bool JointStates { get; set; } = true;
        [DataMember] public bool CmdVel { get; set; } = false;
        [DataMember] public bool Odom { get; set; } = false;

        public BridgePlan() { }
        // Deep-copy ctor so a StackProfile copy doesn't share its BridgePlan.
        public BridgePlan(BridgePlan o)
        {
            Clock = o.Clock; Tf = o.Tf; JointStates = o.JointStates; CmdVel = o.CmdVel; Odom = o.Odom;
        }
    }
```
Add a `[DataMember] public BridgePlan Bridge { get; set; } = new BridgePlan();` to `StackProfile` (after `SensorsEnabled`). Add a copy-constructor to `StackProfile`:

```csharp
        public StackProfile() { }
        // Copy-constructor — dialogs edit a clone and commit on OK; also keeps
        // future fields from being silently dropped when duplicating a profile.
        public StackProfile(StackProfile o)
        {
            GzSim = o.GzSim;
            Actuation = o.Actuation;
            SensorsEnabled = o.SensorsEnabled;
            Bridge = new BridgePlan(o.Bridge ?? new BridgePlan());
        }
```
(Keep the `[OnDeserializing]` hook; also seed `Bridge` there if null to guard legacy configs: set `Bridge = new BridgePlan()` alongside the existing default seeding — verify the hook seeds all reference fields.)

(Do NOT delete `StackFlyoutModel` here — see ORDERING NOTE; it goes in Task 4 with the flyout.)

- [ ] **Step 4: Run — pass.** Filter then full suite green (StackFlyoutModelTests still present + passing for now).

- [ ] **Step 5: Commit.**
```bash
git add SW2GZ/Ros2/StackProfile.cs Test/URDFExport/StackProfileTests.cs
git commit -m "feat: add BridgePlan + StackProfile copy-ctor"
```
No AI co-author line.

---

### Task 2: Pure `StackRibbonGate` — when are the stack buttons enabled

**Files:**
- Create: `SW2GZ/Ros2/StackRibbonGate.cs`
- Modify: both csprojs (compile entries)
- Test: `Test/URDFExport/StackRibbonGateTests.cs`

**Rule:** stack buttons are enabled iff the assembly has a saved model (Create Model ran → `Links` non-empty) AND `Mode == RobotPackage`. (Active-doc-is-assembly is checked in the COM layer.)

- [ ] **Step 1: Failing tests:**

```csharp
using System.Collections.Generic;
using SW2GZ.Build.Model;
using SW2GZ.Ros2;
using SW2GZ.URDFExport;
using Xunit;

namespace Test.URDFExport
{
    public class StackRibbonGateTests
    {
        private static Sw2gzExportConfig Cfg(ExportMode mode, int links)
        {
            var c = new Sw2gzExportConfig { Mode = mode };
            for (int i = 0; i < links; i++) c.Links.Add(new LinkDef { Name = "l" + i });
            return c;
        }

        [Fact] public void Enabled_WhenRobotPackage_AndHasLinks()
            => Assert.True(StackRibbonGate.IsEnabled(Cfg(ExportMode.RobotPackage, 2)));

        [Fact] public void Disabled_WhenNoLinks()
            => Assert.False(StackRibbonGate.IsEnabled(Cfg(ExportMode.RobotPackage, 0)));

        [Fact] public void Disabled_ForSdfWorld()
            => Assert.False(StackRibbonGate.IsEnabled(Cfg(ExportMode.SdfWorld, 3)));

        [Fact] public void Disabled_ForSdfModel()
            => Assert.False(StackRibbonGate.IsEnabled(Cfg(ExportMode.SdfModel, 3)));

        [Fact] public void Disabled_WhenConfigNull()
            => Assert.False(StackRibbonGate.IsEnabled(null));
    }
}
```

- [ ] **Step 2: Run — fail.**

- [ ] **Step 3: Implement `StackRibbonGate.cs`:**

```csharp
/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

StackRibbonGate — pure rule for whether the ribbon "Stacks" section buttons
(Actuation / Sensors / Gazebo / Bridge) are enabled. They tune a ROBOT PACKAGE
built from a saved model, so they require:
  * a saved model — Create Model has run, i.e. config.Links is non-empty, and
  * RobotPackage mode — the stack tuning is meaningless for the SdfModel (asset)
    and SdfWorld (world) export targets, so the buttons are greyed there.
The active-document-is-assembly check stays in the COM layer (SwAddin).
*/
using SW2GZ.URDFExport;

namespace SW2GZ.Ros2
{
    public static class StackRibbonGate
    {
        public static bool IsEnabled(Sw2gzExportConfig config)
        {
            if (config == null) return false;
            if (config.Mode != ExportMode.RobotPackage) return false;
            return config.Links != null && config.Links.Count > 0;
        }
    }
}
```
(Confirm `ExportMode` is in namespace `SW2GZ.Ros2` — if it's elsewhere, fix the using. `Sw2gzExportConfig` is in `SW2GZ.URDFExport`.)

- [ ] **Step 4: Run — pass** (5/5) + full suite green.

- [ ] **Step 5: Commit.**
```bash
git add SW2GZ/Ros2/StackRibbonGate.cs Test/URDFExport/StackRibbonGateTests.cs SW2GZ/SW2GZ.csproj Test/SW2GZ.Writers.Test.csproj
git commit -m "feat: add pure StackRibbonGate (stack buttons need saved model + RobotPackage mode)"
```

---

### Task 3: Per-stack config dialogs (`StackConfigDialog`)

**Files:**
- Create: `SW2GZ/UI/StackConfigDialog.cs` (one parameterized WinForms dialog; mirror `SW2GZ/UI/ExportDialog`)
- Test: `Test/UI/StackConfigMapTests.cs` (pure mapping helper, if extracted)

**Design:** ONE `StackConfigDialog` taking a `StackTarget { Actuation, Sensors, Gazebo, Bridge }` + the `StackProfile` (edits a copy via the copy-ctor, returns the edited profile on OK). Controls per target:
- **Actuation:** 3 radio buttons (None / Gz plugin / ros2_control) bound to `Actuation`. (controller detail = future)
- **Sensors:** "Enable sensors" checkbox bound to `SensorsEnabled` + a label noting per-sensor placement lands in D4.
- **Gazebo:** "Build for Gazebo simulation" checkbox bound to `GzSim` + a label noting world params land later.
- **Bridge:** 5 checkboxes bound to `Bridge.Clock/Tf/JointStates/CmdVel/Odom`.

Extract the radio↔`ActuationBackend` mapping into a tiny pure helper `StackConfigMap` (testable). Keep the dialog otherwise standard WinForms (this file is `#if SW_INTEROP`? — it has no SW COM types, only WinForms + StackProfile, so it can compile unconditionally; put it in `SW2GZ/UI` next to ExportDialog and follow ExportDialog's `#if` convention exactly — match whatever ExportDialog does).

- [ ] **Step 1: Pure mapping test** `Test/UI/StackConfigMapTests.cs`:

```csharp
using SW2GZ.Ros2;
using SW2GZ.UI;
using Xunit;

namespace Test.UI
{
    public class StackConfigMapTests
    {
        [Theory]
        [InlineData(0, ActuationBackend.None)]
        [InlineData(1, ActuationBackend.GzPlugin)]
        [InlineData(2, ActuationBackend.Ros2Control)]
        public void RadioIndex_RoundTrips(int idx, ActuationBackend backend)
        {
            Assert.Equal(backend, StackConfigMap.BackendForRadioIndex(idx));
            Assert.Equal(idx, StackConfigMap.RadioIndexForBackend(backend));
        }
    }
}
```

- [ ] **Step 2: Run — fail.**

- [ ] **Step 3: Implement `StackConfigMap` (pure, in `SW2GZ/Ros2/StackConfigMap.cs`, registered in both csprojs)** + the `StackConfigDialog` WinForms class. `StackConfigMap`:

```csharp
/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

StackConfigMap — pure mapping between the Actuation radio-button index in the
StackConfigDialog and the ActuationBackend enum. Kept out of the WinForms file so
the radio ordering is unit-tested and can't silently drift from the enum.
*/
namespace SW2GZ.Ros2
{
    public static class StackConfigMap
    {
        // Radio order shown in the Actuation dialog: 0 None, 1 Gz plugin, 2 ros2_control.
        public static ActuationBackend BackendForRadioIndex(int idx)
        {
            switch (idx)
            {
                case 1:  return ActuationBackend.GzPlugin;
                case 2:  return ActuationBackend.Ros2Control;
                default: return ActuationBackend.None;
            }
        }

        public static int RadioIndexForBackend(ActuationBackend b)
        {
            switch (b)
            {
                case ActuationBackend.GzPlugin:    return 1;
                case ActuationBackend.Ros2Control: return 2;
                default:                            return 0;
            }
        }
    }
}
```
`StackConfigDialog` (WinForms, mirror ExportDialog's structure, ctor `(StackTarget target, StackProfile current)`, builds controls per target, exposes `StackProfile Result` after OK). Edit a clone: `var working = new StackProfile(current);` apply control values on OK, set `Result = working`. Add a public `enum StackTarget { Actuation, Sensors, Gazebo, Bridge }` (in `SW2GZ.UI` or `SW2GZ.Ros2`). Thorough intent comments.

- [ ] **Step 4: Run pure tests — pass.** Compile the add-in (Framework MSBuild) → clean.

- [ ] **Step 5: Commit.**
```bash
git add SW2GZ/UI/StackConfigDialog.cs SW2GZ/Ros2/StackConfigMap.cs Test/UI/StackConfigMapTests.cs SW2GZ/SW2GZ.csproj Test/SW2GZ.Writers.Test.csproj
git commit -m "feat: add per-stack StackConfigDialog + pure StackConfigMap"
```

---

### Task 4: Ribbon "Stacks" section + wire buttons; remove flyout

**Files:**
- Modify: `SW2GZ/SW/SwAddin.cs`

**Files (also):**
- Delete: `SW2GZ/Ros2/StackFlyoutModel.cs` + `Test/URDFExport/StackFlyoutModelTests.cs` + both csproj `<Compile>` entries for it (the flyout that used it is removed in this same task, so nothing references it after this).

**Design:**
- REMOVE all flyout code from Task-3-of-D2 (the `_stacksFlyout` field, `CreateFlyoutGroup2` call, `FlyoutCallback`/`AddFlyoutRow`/`FlyoutEnable`/`ApplyFlyoutClick`/`FlyoutItemState`, all 10 `FlyoutClick*`/`FlyoutState*` methods, the `RemoveFlyoutGroup` call, and the tab-box flyout append). Then delete `StackFlyoutModel` + its test + csproj entries. Grep the whole solution to confirm zero remaining references to `StackFlyoutModel`/`StackFlyoutItem` before building.
- Add four command items to the existing command group (`sw2gzCmdGroupID = 92`) via `AddCommandItem2`, new stable user IDs `922..925`: Actuation, Sensors, Gazebo, Bridge. Each: callback `LaunchActuationConfig` / `LaunchSensorsConfig` / `LaunchGazeboConfig` / `LaunchBridgeConfig`, enable method `StacksEnable` (shared). Image index 0 (reuse cube column for v1; distinct icons are a follow-up).
- Place them in a SEPARATE `CommandTabBox` (a new box after the Create Model + Export box) so they render as a distinct ribbon section. Reuse the existing tab/try-catch block; add a second `box2 = tab.AddCommandTabBox(); box2.AddCommands(new[]{actId,senId,gzId,brId}, textTypes4);`.
- `public int StacksEnable()`: return 1 iff active doc is an assembly AND `StackRibbonGate.IsEnabled(Sw2gzConfigSerialization.Load(activeDoc))`, else 0. Wrap in try/catch returning 0 (never throw into COM).
- Four `public void Launch<Stack>Config()` callbacks: each guards active assembly, `Load` config, `using (var dlg = new StackConfigDialog(StackTarget.X, config.Stacks ?? StackProfile.Default())) { if (dlg.ShowDialog() == DialogResult.OK) { config.Stacks = dlg.Result; Save(...); } }`, all in try/catch + log. Factor the shared body into a private `OpenStackConfig(StackTarget target)`.
- Intent comment block explaining the section + gating.

- [ ] **Step 1: Implement** per the design (remove flyout, add section + callbacks).
- [ ] **Step 2: Compile** the add-in (Framework MSBuild) → 0 CS errors, DLL produced. Iterate until clean.
- [ ] **Step 3: Commit** (includes the flyout-model deletions — use `git add -A` after verifying only intended files changed).
```bash
git add -A
git commit -m "feat: replace Stacks flyout with ribbon section of per-stack config buttons (gated on saved model + RobotPackage)"
```
- [ ] **Step 4: Manual verification (USER)** — document steps: rebuild + elevated regasm, reopen SW; with NO model saved the 4 stack buttons are greyed; run Create Model; buttons enable; click Actuation → dialog opens → pick ros2_control → OK; Export reflects it; switch Mode to World/Assets (in Create Model wizard) → buttons grey out; second assembly independent.

---

## Done-when (D2b)
- Flyout gone; "Stacks" ribbon section with 4 wizard-launching buttons.
- Buttons gated: enabled only with a saved model + RobotPackage mode (pure `StackRibbonGate`, unit-tested).
- Each button opens a `StackConfigDialog` editing the assembly's `StackProfile`; Actuation/Sensors/Gazebo/Bridge all persist + drive export.
- Pure logic unit-tested; COM compiles; user manual-test checklist provided.
- Deferred: distinct per-button icons; deep detail params (controller lists, sensor placement, world physics) — future.
