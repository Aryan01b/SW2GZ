# Mode Pills Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the chevron-style Mode flyout on the Common ribbon box with a "big Create button + 3 small pill toggles" pattern, and delete the throwaway Demo Split button.

**Architecture:** The big "Create [Mode]" button stays an `IFlyoutGroup` (so its label can keep tracking the active mode via the same-userId re-create trick), but the tab-box style flag flips from `swCommandTabButton_ActionFlyout` to `swCommandTabButton_NoFlyout` so no chevron renders. The 3 chevron sub-items are deleted. Three new regular `AddCommandItem2` pills are added with `swCommandTabButton_TextHorizontal` style — SW stacks them 3-per-column, fitting next to the big button. Active mode = pill whose update callback returns 0 (disabled = grayed visual cue, since SW has no native toggle state).

**Tech Stack:** C# / .NET Framework 4.8, SolidWorks Interop COM (`SolidWorks.Interop.sldworks`, `SolidWorks.Interop.swconst`), MSBuild from VS 2022 BuildTools, xUnit test suite via `TestRunner.exe` (542 green baseline).

**Spec:** `docs/superpowers/specs/2026-06-07-mode-pills-redesign-design.md`

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `SW2GZ/UI/Ribbon/RibbonCommandIds.cs` | Central constants for ribbon command IDs | Drop `DemoFlyoutGroup`. Add `ModeRobotPill = 4`, `ModeWorldPill = 5`, `ModeAssetPill = 6`. Add the 3 pill IDs to `AllUserIds`. |
| `SW2GZ/UI/Ribbon/Sw2gzRibbonRegistrar.cs` | Builds the SW2GZ ribbon tab | Drop `_demoFlyout` field + `BuildDemoFlyout` method + its call site + tab-box block. In `BuildModeFlyout`, drop the 3 `AddCommandItem` sub-item calls. In `Register`, register the 3 pills via `AddItem`. In `BuildCommonTabBox`, change the Mode flyout's tab-box flag from `ActionFlyout` to `NoFlyout`, and append the 3 pill cmdIds with `TextHorizontal` style. |
| `SW2GZ/SW/SwAddin.cs` | COM add-in entry + ribbon callbacks | Drop the `Demo*` callbacks region. Delete `ModeSubItemUpdate` (no sub-items left to gate). Add `ModeRobotPillUpdate / ModeWorldPillUpdate / ModeAssetPillUpdate`. Existing `ModeRobotClick / ModeWorldClick / ModeAssetClick` are reused unchanged. |
| `agent-progress/progress.md` | Local progress scratchpad | Add a short bullet under "Done" noting the Mode pills redesign. |

The pills' click callbacks reuse the existing `ModeRobotClick / WorldClick / AssetClick` handlers — same mode-switching code path the chevron sub-items used. The redesign is plumbing only: no behaviour changes inside `SetMode` or `RefreshTabForMode`.

## Build, deploy, and test conventions

- MSBuild path: `C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe`
- Build command (from PowerShell):
  ```powershell
  & 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe' `
    'C:\aryan\SW2GZ\SW2GZ\SW2GZ.csproj' /p:Configuration=Release `
    /p:SolutionDir=C:\aryan\SW2GZ\ /t:Build /v:minimal /nologo /m
  ```
- The compile lands in `obj\Release\SW2GZ.dll`. The post-build copies it to `bin\Release\SW2GZ.dll`. SolidWorks must be **fully closed** before the copy succeeds — otherwise MSB3027 "file locked by SolidWorks" surfaces but the obj DLL is still valid for compile verification.
- The `MSB3216 regasm access denied` warning at the end is non-fatal (same GUID, registration already in place).
- Test suite runner: `TestRunner\bin\Debug\net452\TestRunner.exe` (xUnit-based). Baseline: 542 green. Test runner has a "repo not dirty" assertion — commit before running.
- Tests do not cover ribbon code (`#if SW_INTEROP` is excluded from the test project). Verification for ribbon changes is the compile + manual smoke in SW.

---

## Task 1: Remove the throwaway Demo Split button

**Files:**
- Modify: `SW2GZ/UI/Ribbon/RibbonCommandIds.cs`
- Modify: `SW2GZ/UI/Ribbon/Sw2gzRibbonRegistrar.cs`
- Modify: `SW2GZ/SW/SwAddin.cs`

- [ ] **Step 1: Remove the `DemoFlyoutGroup` constant from `RibbonCommandIds.cs`**

Find this block (around line 38–45) and delete it entirely:

```csharp
        // Throwaway Smart-Dimension-style split-button demo. Single boxed unit:
        // big icon + label on top (face — click fires DemoFaceClick), small ▾
        // chevron strip at the bottom (drops 3 options). Placed at the tail of
        // the Common tab box. Delete this constant + BuildDemoFlyout + the
        // SwAddin.Demo* callbacks to remove cleanly.
        public const int DemoFlyoutGroup = 199;
```

- [ ] **Step 2: Remove the `_demoFlyout` field in `Sw2gzRibbonRegistrar.cs`**

Find this block (around the `_modeFlyout` field) and delete it:

```csharp
        // Throwaway Smart-Dimension-style demo flyout. Single boxed button —
        // face on top (DemoFaceClick), chevron strip at the bottom (drops 3
        // options). Placed at the tail of the Common tab box with the
        // ActionFlyout style flag — see BuildCommonTabBox below.
        private IFlyoutGroup _demoFlyout;
```

- [ ] **Step 3: Remove the `BuildDemoFlyout()` call in `Register()`**

Find this line in `Register()` (right after `BuildModeFlyout(_activeMode);`) and delete it plus its comment:

```csharp
            // Throwaway Smart-Dimension-style split-button demo (single boxed unit).
            BuildDemoFlyout();
```

- [ ] **Step 4: Remove the entire `BuildDemoFlyout()` method**

Delete the whole method (the comment block above it + the method body). It ends with `logger.Info("Sw2gzRibbonRegistrar: demo split button built — ...")`.

- [ ] **Step 5: Remove the demo flyout block in `BuildCommonTabBox`**

Find and delete this block inside `BuildCommonTabBox` (right before `box.AddCommands(...)`):

```csharp
            // Throwaway Smart-Dimension-style demo — appended after Export.
            // TextBelow (large boxed button) | ActionFlyout (clickable face +
            // chevron strip at bottom) = the standard SW split-button shape.
            if (_demoFlyout != null)
            {
                int demoTextType = textBelow |
                    (int)swCommandTabButtonFlyoutStyle_e.swCommandTabButton_ActionFlyout;
                cmdIds.Add(_demoFlyout.CmdID);
                textTypes.Add(demoTextType);
            }
```

- [ ] **Step 6: Remove the `Demo*` callbacks region in `SwAddin.cs`**

Find the `// ─── Demo split-button (throwaway) ──` region and delete the entire block, including `DemoFaceClick`, `DemoOneClick`, `DemoTwoClick`, `DemoThreeClick`, `DemoFlyoutUpdate`, `DemoSubItemUpdate`. The region ends just before `// ─── Common cluster ──`.

- [ ] **Step 7: Build to verify compile**

```powershell
Get-Process SLDWORKS -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
& 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe' `
    'C:\aryan\SW2GZ\SW2GZ\SW2GZ.csproj' /p:Configuration=Release `
    /p:SolutionDir=C:\aryan\SW2GZ\ /t:Build /v:minimal /nologo /m 2>&1 |
  Select-String -Pattern 'error CS|-> C:' | Select-Object -Last 5
```

Expected: line `SW2GZ -> C:\aryan\SW2GZ\SW2GZ\bin\Release\SW2GZ.dll` and no `error CS*` lines. (MSB3216 regasm warning is acceptable.)

- [ ] **Step 8: Confirm demo symbols are gone from the compiled DLL**

```powershell
$dll = 'C:\aryan\SW2GZ\SW2GZ\obj\Release\SW2GZ.dll'
$text = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($dll))
foreach ($sym in @('DemoFaceClick','BuildDemoFlyout','DemoFlyoutGroup','DemoSubItemUpdate')) {
    "{0,-22} {1}" -f $sym, $(if ($text -match [regex]::Escape($sym)) { 'STILL PRESENT' } else { 'gone' })
}
```

Expected: all four print `gone`.

- [ ] **Step 9: Commit**

```bash
git add SW2GZ/UI/Ribbon/RibbonCommandIds.cs SW2GZ/UI/Ribbon/Sw2gzRibbonRegistrar.cs SW2GZ/SW/SwAddin.cs
git commit -m "Ribbon: drop throwaway Demo Split button"
```

---

## Task 2: Strip the chevron off the Mode flyout

After this task the big "Create Robot" button becomes face-only with no chevron, AND the chevron-based mode switcher stops working. Mode is locked to whatever it was on load until Task 3 wires up the pills. The Mode flyout stays an `IFlyoutGroup` (label still re-tracks via `RefreshTabForMode`); only the chevron rendering and its sub-items go.

**Files:**
- Modify: `SW2GZ/UI/Ribbon/Sw2gzRibbonRegistrar.cs`
- Modify: `SW2GZ/SW/SwAddin.cs`

- [ ] **Step 1: Drop the 3 chevron sub-items in `BuildModeFlyout`**

In `BuildModeFlyout(...)`, find the three `_modeFlyout.AddCommandItem(...)` calls and delete them. They look like:

```csharp
            _modeFlyout.AddCommandItem("Create Robot",
                "Author an actuated robot (URDF + ros2_control + Gz plugins). Disabled once Robot content exists.",
                1, "ModeRobotClick", "ModeSubItemUpdate");
            _modeFlyout.AddCommandItem("Create World",
                "Author a Gazebo world (SDF world + included assets + physics + scene). Disabled once World content exists.",
                2, "ModeWorldClick", "ModeSubItemUpdate");
            _modeFlyout.AddCommandItem("Create Asset",
                "Author a single-body static SDF model. Disabled once Asset content exists.",
                3, "ModeAssetClick", "ModeSubItemUpdate");
```

Also update the trailing log line so it doesn't claim "3 sub-items":

```csharp
            logger.Info("Sw2gzRibbonRegistrar: mode flyout (re)built — face='" +
                faceLabel + "', cmdId=" + _modeFlyout.CmdID + " (no sub-items, pills handle mode switch)");
```

- [ ] **Step 2: Flip the Mode flyout's tab-box flag from `ActionFlyout` to `NoFlyout`**

In `BuildCommonTabBox`, replace the existing Mode flyout block:

```csharp
            if (_modeFlyout != null)
            {
                // True split button: OR in swCommandTabButton_ActionFlyout so
                // SW renders a clickable face + a separate chevron that drops
                // the menu (this is the "Insert Components" pattern). Without
                // this flag SW falls back to the SimpleFlyout look — the whole
                // button is one big dropdown trigger and the face never fires
                // its click callback. SimpleFlyout was the bit that previously
                // crashed; ActionFlyout is a different flag and is what the
                // standard SW split buttons use.
                int flyoutTextType = textBelow |
                    (int)swCommandTabButtonFlyoutStyle_e.swCommandTabButton_ActionFlyout;
                cmdIds.Add(_modeFlyout.CmdID);
                textTypes.Add(flyoutTextType);
            }
```

with:

```csharp
            if (_modeFlyout != null)
            {
                // Face-only "Create [Mode]" — no chevron. NoFlyout style hides
                // the dropdown affordance; mode switching moved to the 3 pills
                // appended after Coord/Preview/Export below. Mode flyout stays
                // an IFlyoutGroup so its face label can keep tracking the
                // active mode via the same-userId re-create trick (see
                // BuildModeFlyout — there's no setter on IFlyoutGroup.Name).
                int faceOnlyType = textBelow |
                    (int)swCommandTabButtonFlyoutStyle_e.swCommandTabButton_NoFlyout;
                cmdIds.Add(_modeFlyout.CmdID);
                textTypes.Add(faceOnlyType);
            }
```

- [ ] **Step 3: Delete `ModeSubItemUpdate` in `SwAddin.cs`**

Find and delete:

```csharp
        // UpdateCallback for the 3 SUB-ITEMS inside the flyout — gated on the
        // doc-lock: once any user element exists (Links/Joints/Sensors/Ground/
        // Assets/BodyPart), mode is frozen, sub-items disable. Tooltip on each
        // sub-item explains; user has to delete content to switch mode.
        public int ModeSubItemUpdate()
        {
            try
            {
                if (AssemblyEnable() == 0) return 0;
                if (!TryGetActiveAssembly(out ModelDoc2 modeldoc)) return 0;
                var doc = SW2GZ.URDFExport.Sw2gzDocStore.GetOrCreate(modeldoc);
                return SW2GZ.URDFExport.Sw2gzDocLock.IsLocked(doc) ? 0 : 1;
            }
            catch (Exception e)
            {
                logger.Warn("ModeSubItemUpdate failed", e);
                return 0;
            }
        }
```

- [ ] **Step 4: Build to verify compile**

```powershell
Get-Process SLDWORKS -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
& 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe' `
    'C:\aryan\SW2GZ\SW2GZ\SW2GZ.csproj' /p:Configuration=Release `
    /p:SolutionDir=C:\aryan\SW2GZ\ /t:Build /v:minimal /nologo /m 2>&1 |
  Select-String -Pattern 'error CS|-> C:' | Select-Object -Last 5
```

Expected: line `SW2GZ -> C:\aryan\SW2GZ\SW2GZ\bin\Release\SW2GZ.dll` and no `error CS*` lines.

- [ ] **Step 5: Confirm `ModeSubItemUpdate` is gone from the DLL**

```powershell
$dll = 'C:\aryan\SW2GZ\SW2GZ\obj\Release\SW2GZ.dll'
$text = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($dll))
"ModeSubItemUpdate: " + $(if ($text -match 'ModeSubItemUpdate') { 'STILL PRESENT' } else { 'gone' })
```

Expected: `ModeSubItemUpdate: gone`.

- [ ] **Step 6: Commit**

```bash
git add SW2GZ/UI/Ribbon/Sw2gzRibbonRegistrar.cs SW2GZ/SW/SwAddin.cs
git commit -m "Ribbon: strip chevron off Mode flyout (NoFlyout style, drop sub-items)"
```

---

## Task 3: Add the 3 mode pills

After this task pill clicks switch modes via the existing `ModeRobotClick / ModeWorldClick / ModeAssetClick` handlers, the active pill renders grayed (its update callback returns 0), and the big "Create [Mode]" button face label retracks via the existing `RefreshTabForMode → BuildModeFlyout` path.

**Files:**
- Modify: `SW2GZ/UI/Ribbon/RibbonCommandIds.cs`
- Modify: `SW2GZ/UI/Ribbon/Sw2gzRibbonRegistrar.cs`
- Modify: `SW2GZ/SW/SwAddin.cs`

- [ ] **Step 1: Add the 3 pill userIds to `RibbonCommandIds.cs`**

Locate the existing Mode block (around line 30) and add the pill IDs right after `ModeFlyoutGroup`:

```csharp
        public const int ModeFlyoutGroup = 99;

        // Mode pills — three small TextHorizontal toggles next to the big
        // Create button. Replace the chevron-style sub-items (slots 0..2 in
        // the flyout's own ID space, kept reserved as documentation). Click
        // reuses the existing ModeRobotClick / WorldClick / AssetClick path.
        public const int ModeRobotPill   = 4;
        public const int ModeWorldPill   = 5;
        public const int ModeAssetPill   = 6;
```

Then update `AllUserIds` to include the 3 pills. Find:

```csharp
        public static readonly int[] AllUserIds = new[]
        {
            ModeRobot, ModeWorld, ModeAsset,
            CoordPmp, PreviewPmp, ExportPmp,
            RobotInertia, RobotSensors, RobotActuation, RobotStack,
            WorldGround, WorldAssets, WorldPhysics, WorldScene,
            AssetBody, AssetSurface,
        };
```

and replace with:

```csharp
        public static readonly int[] AllUserIds = new[]
        {
            ModeRobot, ModeWorld, ModeAsset,
            ModeRobotPill, ModeWorldPill, ModeAssetPill,
            CoordPmp, PreviewPmp, ExportPmp,
            RobotInertia, RobotSensors, RobotActuation, RobotStack,
            WorldGround, WorldAssets, WorldPhysics, WorldScene,
            AssetBody, AssetSurface,
        };
```

- [ ] **Step 2: Add the 3 pill update callbacks to `SwAddin.cs`**

Insert just before `// ─── Common cluster ──`:

```csharp
        // ─── Mode pills ───────────────────────────────────────────────
        // Three small TextHorizontal toggles next to the big Create button.
        // The pill matching the active mode returns 0 (disabled = grayed in
        // the ribbon) so the user reads "I'm in this mode now". Doc-lock
        // (Sw2gzDocLock.IsLocked) freezes all 3 pills, matching the prior
        // chevron-sub-item behaviour.
        public int ModeRobotPillUpdate() => PillUpdate(SW2GZ.URDFExport.Sw2gzMode.Robot);
        public int ModeWorldPillUpdate() => PillUpdate(SW2GZ.URDFExport.Sw2gzMode.World);
        public int ModeAssetPillUpdate() => PillUpdate(SW2GZ.URDFExport.Sw2gzMode.Asset);

        private int PillUpdate(SW2GZ.URDFExport.Sw2gzMode pillMode)
        {
            try
            {
                if (AssemblyEnable() == 0) return 0;
                if (!TryGetActiveAssembly(out ModelDoc2 modeldoc)) return 0;
                var doc = SW2GZ.URDFExport.Sw2gzDocStore.GetOrCreate(modeldoc);
                if (SW2GZ.URDFExport.Sw2gzDocLock.IsLocked(doc)) return 0;
                // Disable the pill that already represents the active mode —
                // gives the grayed-out "you are here" visual cue.
                return (doc.Mode == pillMode) ? 0 : 1;
            }
            catch (Exception e)
            {
                logger.Warn("PillUpdate(" + pillMode + ") failed", e);
                return 0;
            }
        }
```

- [ ] **Step 3: Register the 3 pills in `Sw2gzRibbonRegistrar.Register()`**

Find the last `AddItem(...)` call in `Register()` (the Asset cluster):

```csharp
            AddItem(grp, RibbonCommandIds.AssetBody,    "Body",    "Single-part body",          "OpenAssetBodyPmp",    "AssetClusterEnable", IMG_BODY,    toolbar);
            AddItem(grp, RibbonCommandIds.AssetSurface, "Surface", "Friction / contact",        "OpenAssetSurfacePmp", "AssetClusterEnable", IMG_SURFACE, toolbar);
```

Append 3 pill registrations right after the AssetSurface line. The strip column indices 1/2/3 are the Robot/World/Asset glyphs documented in the column comment at the top of `Register`:

```csharp
            // Mode pills — TextHorizontal style applied in BuildCommonTabBox.
            // Image columns 1/2/3 = Robot/World/Asset glyphs (same icons as
            // the deleted chevron sub-items used). Click callbacks are the
            // existing ModeRobotClick / WorldClick / AssetClick handlers.
            AddItem(grp, RibbonCommandIds.ModeRobotPill, "Robot", "Switch to Robot mode (disabled when already active or doc-locked).", "ModeRobotClick", "ModeRobotPillUpdate", 1, toolbar);
            AddItem(grp, RibbonCommandIds.ModeWorldPill, "World", "Switch to World mode (disabled when already active or doc-locked).", "ModeWorldClick", "ModeWorldPillUpdate", 2, toolbar);
            AddItem(grp, RibbonCommandIds.ModeAssetPill, "Asset", "Switch to Asset mode (disabled when already active or doc-locked).", "ModeAssetClick", "ModeAssetPillUpdate", 3, toolbar);
```

- [ ] **Step 4: Append pill cmdIds to the Common tab box with `TextHorizontal` style**

In `BuildCommonTabBox`, find the existing Coord/Preview/Export loop:

```csharp
            foreach (int uid in new[] { RibbonCommandIds.CoordPmp, RibbonCommandIds.PreviewPmp, RibbonCommandIds.ExportPmp })
            {
                if (_userToCmdId.TryGetValue(uid, out int cmdId))
                {
                    cmdIds.Add(cmdId);
                    textTypes.Add(textBelow);
                }
            }
            box.AddCommands(cmdIds.ToArray(), textTypes.ToArray());
```

Insert pill placement **between** the Mode flyout block and the Coord/Preview/Export loop, so layout is `[Big Create] [3 stacked pills] [Coord] [Preview] [Export]`. Add right after the closing brace of the `if (_modeFlyout != null)` block:

```csharp
            // Mode pills — TextHorizontal stacks 3-per-column. The 3 pills
            // sit immediately right of the big Create button, matching its
            // height. NOTE: per-pill enable is via PillUpdate (set on each
            // AddCommandItem2), not via the textType.
            int textHorizontal = (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextHorizontal;
            foreach (int uid in new[] { RibbonCommandIds.ModeRobotPill, RibbonCommandIds.ModeWorldPill, RibbonCommandIds.ModeAssetPill })
            {
                if (_userToCmdId.TryGetValue(uid, out int pillCmdId))
                {
                    cmdIds.Add(pillCmdId);
                    textTypes.Add(textHorizontal);
                }
                else
                {
                    logger.Warn("Sw2gzRibbonRegistrar: pill cmdId missing for userId=" + uid);
                }
            }
```

- [ ] **Step 5: Build to verify compile**

```powershell
Get-Process SLDWORKS -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
& 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe' `
    'C:\aryan\SW2GZ\SW2GZ\SW2GZ.csproj' /p:Configuration=Release `
    /p:SolutionDir=C:\aryan\SW2GZ\ /t:Build /v:minimal /nologo /m 2>&1 |
  Select-String -Pattern 'error CS|-> C:' | Select-Object -Last 5
```

Expected: `SW2GZ -> C:\aryan\SW2GZ\SW2GZ\bin\Release\SW2GZ.dll` and no `error CS*` lines.

- [ ] **Step 6: Confirm the pill symbols are in the DLL**

```powershell
$dll = 'C:\aryan\SW2GZ\SW2GZ\obj\Release\SW2GZ.dll'
$text = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($dll))
foreach ($sym in @('ModeRobotPill','ModeRobotPillUpdate','ModeWorldPillUpdate','ModeAssetPillUpdate','PillUpdate')) {
    "{0,-22} {1}" -f $sym, $(if ($text -match [regex]::Escape($sym)) { 'present' } else { 'MISSING' })
}
```

Expected: all 5 print `present`.

- [ ] **Step 7: Commit**

```bash
git add SW2GZ/UI/Ribbon/RibbonCommandIds.cs SW2GZ/UI/Ribbon/Sw2gzRibbonRegistrar.cs SW2GZ/SW/SwAddin.cs
git commit -m "Ribbon: add 3 mode pills next to big Create button"
```

---

## Task 4: Run the existing test suite + manual smoke + update progress

**Files:**
- Modify: `agent-progress/progress.md`

- [ ] **Step 1: Build the TestRunner project (Debug)**

```powershell
& 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe' `
    'C:\aryan\SW2GZ\TestRunner\TestRunner.csproj' /p:Configuration=Debug `
    /p:SolutionDir=C:\aryan\SW2GZ\ /t:Build /v:minimal /nologo /m 2>&1 |
  Select-String -Pattern 'error|-> C:' | Select-Object -Last 5
```

Expected: a `TestRunner -> ...TestRunner.exe` line, no errors.

- [ ] **Step 2: Run the test suite**

```powershell
& 'C:\aryan\SW2GZ\TestRunner\bin\Debug\net452\TestRunner.exe' 2>&1 | Select-Object -Last 10
```

Expected: 542 passing (or whatever the current baseline is per `progress.md`). The TestRunner asserts a clean git tree — Task 3 already committed, so this should pass. If a "repo dirty" failure shows up, run `git status` and commit any stragglers first.

- [ ] **Step 3: Close SolidWorks and deploy a fresh DLL**

```powershell
Get-Process SLDWORKS -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
& 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe' `
    'C:\aryan\SW2GZ\SW2GZ\SW2GZ.csproj' /p:Configuration=Release `
    /p:SolutionDir=C:\aryan\SW2GZ\ /t:Build /v:minimal /nologo /m 2>&1 |
  Select-String -Pattern 'error CS|-> C:|MSB3027' | Select-Object -Last 5
```

Expected: `SW2GZ -> C:\aryan\SW2GZ\SW2GZ\bin\Release\SW2GZ.dll`, no MSB3027 lock error.

- [ ] **Step 4: Manual smoke test in SolidWorks**

Open SolidWorks, open any assembly, click the SW2GZ tab. Verify in the Common box:

1. Big "Create Robot" button on the left — face only, **no chevron** at the bottom edge.
2. Three small stacked pills directly right of the big button: **Robot / World / Asset**, with the Robot pill grayed out.
3. Click the **World** pill. Expected: the big button's label changes to "Create World", the World pill grays out, Robot enables, and the tab rebuilds with the World cluster of panel buttons.
4. Click the **Asset** pill. Same retracking → "Create Asset", Asset pill grays out.
5. Click the big "Create Asset" face. Expected: the create wizard PMP opens for Asset mode (same `OpenCreatePmp` flow).
6. Add some content (e.g., set up a Robot link via the panel buttons) → expected: all 3 pills gray out (doc-lock kicks in), big button stays clickable.
7. Verify the Demo Split button from previous iteration is gone.

If any of those steps fail, capture the symptom (screenshot or the relevant lines from the SW log) and revert to before Task 3's commit (`git revert HEAD`) to bisect.

- [ ] **Step 5: Update `agent-progress/progress.md`**

Open `agent-progress/progress.md` and add a bullet under the existing `## Done (v2.1.0 UI shell — this plan)` section:

```markdown
- Mode flyout redesign: face-only "Create [Mode]" button + 3 TextHorizontal pills (active pill grayed) replacing the chevron-based mode picker. Demo Split throwaway removed.
```

- [ ] **Step 6: Commit**

```bash
git add agent-progress/progress.md
git commit -m "progress: note Mode pills redesign + Demo Split removal"
```

---

## Self-Review (post-write)

**1. Spec coverage:**
- Visual / "big Create + 3 pills" layout → Task 2 (strip chevron) + Task 3 (pills) ✓
- Big button keeps dynamic label → preserved (Task 2 only changes tab-box flag; `BuildModeFlyout` re-create path untouched) ✓
- Pill click → existing `SetMode` → covered by Task 3 step 3 (reusing `ModeRobotClick` etc.) ✓
- Active pill disabled (grayed) → Task 3 step 2 (`PillUpdate` returns 0 when `pillMode == active`) ✓
- Doc-locked → all pills disabled → Task 3 step 2 (`Sw2gzDocLock.IsLocked` short-circuits before active-mode check) ✓
- Demo Split deleted → Task 1 ✓
- `RibbonCommandIds` changes (`ModeRobotPill` etc., `AllUserIds` update, drop `DemoFlyoutGroup`) → Task 1 step 1 + Task 3 step 1 ✓
- Edge case: `CreateFlyoutGroup2` with zero sub-items → Task 2 leaves the flyout with zero sub-items, and the spec lists the fallback (re-add a hidden sub-item) for the manual smoke step if SW rejects it ✓
- Edge case: `NoFlyout` flag interaction → spec covers fallback; Task 4 step 4 is where it would surface; spec already calls out the fallback path ✓
- Test suite stays 542 green → Task 4 steps 1–2 ✓
- `progress.md` updated → Task 4 step 5 ✓

**2. Placeholder scan:** No "TBD", no "implement later", no "similar to Task N". Every code change is shown in full. No bare "add error handling" — `PillUpdate` shows the try/catch explicitly.

**3. Type consistency:** `Sw2gzMode` enum used identically in `PillUpdate` parameter and the three `ModePillUpdate` call sites. `Sw2gzDocStore.GetOrCreate` / `Sw2gzDocLock.IsLocked` signatures match the existing `ModeSubItemUpdate` usage (cross-checked against the deleted code in Task 2 step 3). `AddItem` parameter order (`grp, userId, name, tip, clickMethod, enableMethod, img, kind`) matches the existing call sites in `Register`. `ICommandTabBox.AddCommands(int[], int[])` matches the existing call.
