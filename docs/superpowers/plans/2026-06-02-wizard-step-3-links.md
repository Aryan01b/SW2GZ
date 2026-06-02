# Wizard Step 3 (Links) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans or subagent-driven-development. Steps use `- [ ]` checkboxes.

**Goal:** Implement Step 3 of the SW2GZ export PMP — define links (name + assigned components + base flag), with read-only mass readout and validation, persisted in the checkpoint.

**Architecture:** A pure serializable `LinkDef` (in `Sw2gzExportConfig.Links`) + a pure `LinkDefValidator`, both unit-tested in net8. The COM-bound Step 3 group inside `Sw2gzExportPmp` mirrors `GeometryPropertyManager`'s live-viewport selection flow, seeds links from the assembly's top-level components, and writes into `config.Links`.

**Tech Stack:** C# .NET Framework 4.8.1 add-in (SW COM, `#if SW_INTEROP`); DataContract serialization; xUnit net8 for pure parts. Build with VS BuildTools MSBuild (`SolidWorksDir`/`SolutionDir` env vars), `RegisterForComInterop=false`.

**Reference:** spec `docs/superpowers/specs/2026-06-02-wizard-step-3-links-design.md`; pattern `URDFExport/GeometryPropertyManager.cs`.

---

## Task 1: `LinkDef` model + config field + round-trip test (pure, TDD)

**Files:** Create `SW2GZ/Build/Model/LinkDef.cs`; edit `SW2GZ/URDFExport/Sw2gzExportConfig.cs`, `Test/SW2GZ.Writers.Test.csproj`, `SW2GZ/SW2GZ.csproj`; edit `Test/URDFExport/Sw2gzExportConfigTests.cs`.

- [ ] **Step 1: Create `LinkDef`**

```csharp
/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — one robot link as defined in wizard Step 3: a name, the SolidWorks
component ids (Component2.Name2) assigned to it, and whether it is the base/
root link. Pure / COM-free and DataContract-serialized inside Sw2gzExportConfig
so Step 3 resumes from the document checkpoint.
*/
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SW2GZ.Build.Model
{
    [DataContract(Name = "LinkDef", Namespace = "")]
    public sealed class LinkDef
    {
        [DataMember] public string Name { get; set; } = string.Empty;
        [DataMember] public List<string> ComponentIds { get; set; } = new List<string>();
        [DataMember] public bool IsBase { get; set; }
    }
}
```

- [ ] **Step 2: Add `Links` to `Sw2gzExportConfig`**

In `SW2GZ/URDFExport/Sw2gzExportConfig.cs` add `using System.Collections.Generic;` and
`using SW2GZ.Build.Model;`, then after `LastStep`:

```csharp
        // Step 3 — link definitions (name + assigned component ids + base flag).
        [DataMember] public List<LinkDef> Links { get; set; } = new List<LinkDef>();
```

- [ ] **Step 3: Register pure sources in both projects**

In `Test/SW2GZ.Writers.Test.csproj`, after the `Sw2gzConfigCodec.cs` line:

```xml
    <Compile Include="..\SW2GZ\Build\Model\LinkDef.cs" Link="Sources\Build\Model\LinkDef.cs" />
```

(`Build\Model\LinkDef.cs` is new; `GeometryAssignment.cs` is NOT needed.) In `SW2GZ/SW2GZ.csproj`, beside the other `Build\Model` compile items, add:

```xml
    <Compile Include="Build\Model\LinkDef.cs" />
```

- [ ] **Step 4: Extend the round-trip test (write failing first)**

Append to `Test/URDFExport/Sw2gzExportConfigTests.cs` inside the class:

```csharp
        [Fact]
        public void RoundTrip_PreservesLinks()
        {
            var config = new Sw2gzExportConfig();
            config.Links.Add(new SW2GZ.Build.Model.LinkDef
            {
                Name = "base_link",
                IsBase = true,
                ComponentIds = { "chassis-1@robot", "motor-1@robot" },
            });
            config.Links.Add(new SW2GZ.Build.Model.LinkDef { Name = "wheel_left" });

            string xml = Sw2gzConfigCodec.ToXmlString(config);
            Sw2gzExportConfig restored = Sw2gzConfigCodec.FromXmlString(xml);

            Assert.Equal(2, restored.Links.Count);
            Assert.Equal("base_link", restored.Links[0].Name);
            Assert.True(restored.Links[0].IsBase);
            Assert.Equal(2, restored.Links[0].ComponentIds.Count);
            Assert.Equal("motor-1@robot", restored.Links[0].ComponentIds[1]);
            Assert.False(restored.Links[1].IsBase);
        }
```

- [ ] **Step 5: Run — expect fail then pass**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter FullyQualifiedName~Sw2gzExportConfigTests`
First FAIL (LinkDef missing) → after Steps 1–3 PASS (7 tests).

- [ ] **Step 6: Full suite + commit**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj` → all green.
```bash
git add SW2GZ/Build/Model/LinkDef.cs SW2GZ/URDFExport/Sw2gzExportConfig.cs Test/SW2GZ.Writers.Test.csproj SW2GZ/SW2GZ.csproj Test/URDFExport/Sw2gzExportConfigTests.cs
git commit -m "feat(addin): LinkDef model + config.Links checkpoint field (round-trip tested)"
```

---

## Task 2: `LinkDefValidator` (pure, TDD)

**Files:** Create `SW2GZ/Build/LinkDefValidator.cs`, `Test/Build/LinkDefValidatorTests.cs`; edit both `.csproj`s.

- [ ] **Step 1: Register sources** — add `Build\LinkDefValidator.cs` to `SW2GZ.csproj` (beside other `Build\*.cs`) and to the test csproj:
```xml
    <Compile Include="..\SW2GZ\Build\LinkDefValidator.cs" Link="Sources\Build\LinkDefValidator.cs" />
```

- [ ] **Step 2: Write failing tests**

`Test/Build/LinkDefValidatorTests.cs`:

```csharp
/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Collections.Generic;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using Xunit;

namespace SW2GZ.Test.Build
{
    public class LinkDefValidatorTests
    {
        private static LinkDef Link(string name, bool baseLink, params string[] ids) =>
            new LinkDef { Name = name, IsBase = baseLink, ComponentIds = new List<string>(ids) };

        [Fact]
        public void Valid_WhenEveryComponentAssignedOnce_OneBase_UniqueNames()
        {
            var links = new List<LinkDef> { Link("base", true, "a", "b"), Link("wheel", false, "c") };
            var issues = LinkDefValidator.Validate(links, new[] { "a", "b", "c" });
            Assert.Empty(issues);
        }

        [Fact]
        public void Flags_UnassignedComponent()
        {
            var links = new List<LinkDef> { Link("base", true, "a") };
            var issues = LinkDefValidator.Validate(links, new[] { "a", "b" });
            Assert.Contains(issues, i => i.Contains("unassigned") && i.Contains("b"));
        }

        [Fact]
        public void Flags_ComponentInTwoLinks()
        {
            var links = new List<LinkDef> { Link("base", true, "a"), Link("two", false, "a") };
            var issues = LinkDefValidator.Validate(links, new[] { "a" });
            Assert.Contains(issues, i => i.Contains("more than one") && i.Contains("a"));
        }

        [Fact]
        public void Flags_ZeroOrMultipleBase()
        {
            var none = LinkDefValidator.Validate(
                new List<LinkDef> { Link("x", false, "a") }, new[] { "a" });
            Assert.Contains(none, i => i.Contains("base"));

            var two = LinkDefValidator.Validate(
                new List<LinkDef> { Link("x", true, "a"), Link("y", true, "b") }, new[] { "a", "b" });
            Assert.Contains(two, i => i.Contains("base"));
        }

        [Fact]
        public void Flags_DuplicateNames_And_EmptyLink()
        {
            var links = new List<LinkDef> { Link("dup", true, "a"), Link("dup", false), };
            var issues = LinkDefValidator.Validate(links, new[] { "a" });
            Assert.Contains(issues, i => i.Contains("name"));
            Assert.Contains(issues, i => i.Contains("no components"));
        }
    }
}
```

- [ ] **Step 3: Run — expect fail**
Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter FullyQualifiedName~LinkDefValidatorTests` → FAIL (type missing).

- [ ] **Step 4: Implement**

`SW2GZ/Build/LinkDefValidator.cs`:

```csharp
/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — pure validation for wizard Step 3 link definitions. The PMP supplies the
full set of top-level component ids (COM-derived); everything here is COM-free
and unit-tested. Returns a flat list of human-readable blocking issues (empty =
ready to advance).
*/
using System.Collections.Generic;
using SW2GZ.Build.Model;

namespace SW2GZ.Build
{
    public static class LinkDefValidator
    {
        public static List<string> Validate(
            IReadOnlyList<LinkDef> links, IReadOnlyCollection<string> allComponentIds)
        {
            var issues = new List<string>();
            if (links == null) links = new List<LinkDef>();

            // Base count.
            int baseCount = 0;
            foreach (LinkDef l in links) if (l.IsBase) baseCount++;
            if (baseCount == 0) issues.Add("No base link is set — mark exactly one link as the base.");
            else if (baseCount > 1) issues.Add("More than one base link is set — only one is allowed.");

            // Names: unique + non-empty; empty links.
            var seenNames = new HashSet<string>();
            foreach (LinkDef l in links)
            {
                string name = (l.Name ?? "").Trim();
                if (name.Length == 0) issues.Add("A link has an empty name.");
                else if (!seenNames.Add(name)) issues.Add("Duplicate link name: " + name);
                if (l.ComponentIds == null || l.ComponentIds.Count == 0)
                    issues.Add("Link '" + name + "' has no components assigned.");
            }

            // Assignment coverage + duplicates.
            var assignedOnce = new HashSet<string>();
            var assignedTwice = new HashSet<string>();
            foreach (LinkDef l in links)
            {
                if (l.ComponentIds == null) continue;
                foreach (string id in l.ComponentIds)
                {
                    if (!assignedOnce.Add(id)) assignedTwice.Add(id);
                }
            }
            foreach (string id in assignedTwice)
                issues.Add("Component assigned to more than one link: " + id);

            if (allComponentIds != null)
            {
                foreach (string id in allComponentIds)
                    if (!assignedOnce.Contains(id))
                        issues.Add("Component unassigned: " + id);
            }

            return issues;
        }
    }
}
```

- [ ] **Step 5: Run + full suite + commit**
Run filtered → PASS; full `dotnet test` → green.
```bash
git add SW2GZ/Build/LinkDefValidator.cs Test/Build/LinkDefValidatorTests.cs SW2GZ/SW2GZ.csproj Test/SW2GZ.Writers.Test.csproj
git commit -m "feat(addin): LinkDefValidator for Step 3 link coverage/base/name checks"
```

---

## Task 3: Step 3 PMP group (COM — build-verified)

**Files:** edit `SW2GZ/URDFExport/Sw2gzExportPmp.cs`. Mirrors `GeometryPropertyManager`. No unit test (COM); verified by MSBuild + workstation run.

- [ ] **Step 1: Fields + IDs**

Add `using SW2GZ.Build;` and `using SW2GZ.Build.Model;` to the `#if SW_INTEROP` usings.
Add fields (near the Step 2 fields):

```csharp
        // Step 3 (Links) controls.
        private PropertyManagerPageCombobox PMComboLink;
        private PropertyManagerPageLabel PMLabelLinkProgress;
        private PropertyManagerPageSelectionbox PMSelectionLink;
        private PropertyManagerPageTextbox PMTextLinkName;
        private PropertyManagerPageCheckbox PMCheckBase;
        private PropertyManagerPageLabel PMLabelLinkMass;
        private PropertyManagerPageLabel PMLabelLinkValidation;

        private int currentLinkIndex;
        private const int LinkSelectionMark = 3;
        private IMassProperties massProps;       // combined-mass readout (Step 3)
        private List<string> allComponentIds = new List<string>();
```

Add Step 3 control IDs (step index 2 → base 140), all within the 20-id stride:

```csharp
        private const int ComboLinkID        = StepIdBase + 2 * 20 + 2;
        private const int LabelLinkProgressID= StepIdBase + 2 * 20 + 3;
        private const int SelectionLinkID    = StepIdBase + 2 * 20 + 4;
        private const int LabelLinkNameID    = StepIdBase + 2 * 20 + 5;
        private const int TextLinkNameID     = StepIdBase + 2 * 20 + 6;
        private const int ButtonAssignID     = StepIdBase + 2 * 20 + 7;
        private const int ButtonClearID      = StepIdBase + 2 * 20 + 8;
        private const int CheckBaseID        = StepIdBase + 2 * 20 + 9;
        private const int ButtonAddLinkID    = StepIdBase + 2 * 20 + 10;
        private const int ButtonRemoveLinkID = StepIdBase + 2 * 20 + 11;
        private const int ButtonPrevLinkID   = StepIdBase + 2 * 20 + 12;
        private const int ButtonNextLinkID   = StepIdBase + 2 * 20 + 13;
        private const int LabelLinkMassID    = StepIdBase + 2 * 20 + 14;
        private const int LabelLinkValidationID = StepIdBase + 2 * 20 + 15;
```

- [ ] **Step 2: Dispatch + seeding**

In `BuildPage`'s `switch (step)` add:
```csharp
                    case 2:
                        BuildLinksStep(stepGroup, leftEdge, indent, visibleEnabled);
                        break;
```

In the constructor, after `ApplyDefaults();`, add `SeedLinksFromAssembly();` and build the
mass-properties reader:
```csharp
                massProps = new SolidWorksMassProperties(swApp, (AssemblyDoc)model);
                SeedLinksFromAssembly();
```
(`using SW2GZ.SwSurface;` for `SolidWorksMassProperties`.)

`SeedLinksFromAssembly` enumerates top-level components, records `allComponentIds`, and —
only when the checkpoint has no links yet — seeds one `LinkDef` per component:

```csharp
        private void SeedLinksFromAssembly()
        {
            allComponentIds.Clear();
            object[] comps = (object[])((AssemblyDoc)model).GetComponents(true);
            var topLevel = new List<Component2>();
            if (comps != null)
            {
                foreach (object o in comps)
                {
                    var c = (Component2)o;
                    if (c.IsSuppressed()) continue;
                    topLevel.Add(c);
                    allComponentIds.Add(c.Name2);
                }
            }

            if (config.Links == null) config.Links = new List<LinkDef>();
            if (config.Links.Count > 0) return;   // resume from checkpoint

            bool baseAssigned = false;
            foreach (Component2 c in topLevel)
            {
                bool isBase = !baseAssigned && IsGrounded(c);
                if (isBase) baseAssigned = true;
                config.Links.Add(new LinkDef
                {
                    Name = RosNameSanitizer.Sanitize(c.Name2).Value,
                    ComponentIds = new List<string> { c.Name2 },
                    IsBase = isBase,
                });
            }
            // Fall back to the first link as base if none was grounded.
            if (!baseAssigned && config.Links.Count > 0) config.Links[0].IsBase = true;
        }

        private static bool IsGrounded(Component2 c)
        {
            try { return c.IsFixed(); } catch { return false; }
        }
```

- [ ] **Step 3: Build the group**

```csharp
        private void BuildLinksStep(PropertyManagerPageGroup group, int leftEdge, int indent, int visibleEnabled)
        {
            PMComboLink = (PropertyManagerPageCombobox)group.AddControl2(
                ComboLinkID, (short)swPropertyManagerPageControlType_e.swControlType_Combobox,
                "", (short)indent, visibleEnabled, "Select the link to edit");
            PMComboLink.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;

            PMLabelLinkProgress = AddFieldLabel(group, LabelLinkProgressID, "Link 0 of 0", leftEdge,
                (int)swAddControlOptions_e.swControlOptions_Visible);

            AddFieldLabel(group, LabelLinkNameID, "Link name", leftEdge,
                (int)swAddControlOptions_e.swControlOptions_Visible);
            PMTextLinkName = (PropertyManagerPageTextbox)group.AddControl2(
                TextLinkNameID, (short)swPropertyManagerPageControlType_e.swControlType_Textbox,
                "", (short)indent, visibleEnabled, "ROS link name (sanitized on assign)");

            PMSelectionLink = (PropertyManagerPageSelectionbox)group.AddControl2(
                SelectionLinkID, (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "", (short)indent, visibleEnabled, "Pick the components for this link in the viewport");
            PMSelectionLink.Height = 50;
            PMSelectionLink.SetSelectionFilters(new int[]
            {
                (int)swSelectType_e.swSelCOMPONENTS, (int)swSelectType_e.swSelSOLIDBODIES,
            });
            PMSelectionLink.Mark = LinkSelectionMark;

            PMButtonAssign = (PropertyManagerPageButton)group.AddControl2(
                ButtonAssignID, (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Assign selection", (short)indent, visibleEnabled, "Assign the picked components to this link");
            PMButtonClear = (PropertyManagerPageButton)group.AddControl2(
                ButtonClearID, (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Clear", (short)indent, visibleEnabled, "Clear this link's components");

            PMCheckBase = (PropertyManagerPageCheckbox)group.AddControl2(
                CheckBaseID, (short)swPropertyManagerPageControlType_e.swControlType_Checkbox,
                "Base (root) link", (short)indent, visibleEnabled, "Mark this link as the robot root");

            PMButtonAddLink = AddLinkButton(group, ButtonAddLinkID, "Add link", indent, visibleEnabled);
            PMButtonRemoveLink = AddLinkButton(group, ButtonRemoveLinkID, "Remove link", indent, visibleEnabled);
            PMButtonPrevLink = AddLinkButton(group, ButtonPrevLinkID, "< Prev link", indent, visibleEnabled);
            PMButtonNextLink = AddLinkButton(group, ButtonNextLinkID, "Next link >", indent, visibleEnabled);

            PMLabelLinkMass = AddFieldLabel(group, LabelLinkMassID, "", leftEdge,
                (int)swAddControlOptions_e.swControlOptions_Visible);
            PMLabelLinkValidation = AddFieldLabel(group, LabelLinkValidationID, "", leftEdge,
                (int)swAddControlOptions_e.swControlOptions_Visible);

            PopulateLinkCombo();
            LoadCurrentLink();
        }

        private PropertyManagerPageButton AddLinkButton(
            PropertyManagerPageGroup group, int id, string caption, int indent, int visibleEnabled)
        {
            return (PropertyManagerPageButton)group.AddControl2(
                id, (short)swPropertyManagerPageControlType_e.swControlType_Button,
                caption, (short)indent, visibleEnabled, caption);
        }
```

Add the corresponding button fields next to the others:
```csharp
        private PropertyManagerPageButton PMButtonAddLink;
        private PropertyManagerPageButton PMButtonRemoveLink;
        private PropertyManagerPageButton PMButtonPrevLink;
        private PropertyManagerPageButton PMButtonNextLink;
```

- [ ] **Step 4: Link navigation + current-link load + mass readout**

```csharp
        private LinkDef CurrentLink =>
            (config.Links != null && currentLinkIndex >= 0 && currentLinkIndex < config.Links.Count)
                ? config.Links[currentLinkIndex] : null;

        private void PopulateLinkCombo()
        {
            if (PMComboLink == null) return;
            PMComboLink.Clear();
            foreach (LinkDef l in config.Links) PMComboLink.AddItems(l.Name);
        }

        private void LoadCurrentLink()
        {
            if (PMComboLink == null) return;
            int n = config.Links.Count;
            if (currentLinkIndex >= n) currentLinkIndex = n - 1;
            if (currentLinkIndex < 0) currentLinkIndex = 0;
            PMLabelLinkProgress.Caption = "Link " + (n == 0 ? 0 : currentLinkIndex + 1) + " of " + n;

            LinkDef link = CurrentLink;
            if (link == null) { PMTextLinkName.Text = ""; PMLabelLinkMass.Caption = ""; return; }
            if (n > 0) PMComboLink.CurrentSelection = (short)currentLinkIndex;
            PMTextLinkName.Text = link.Name ?? "";
            PMCheckBase.Checked = link.IsBase;
            UpdateMassReadout(link);
            UpdateValidationLabel();
            if (PMSelectionLink != null) PMSelectionLink.SetSelectionFocus();
        }

        private void UpdateMassReadout(LinkDef link)
        {
            if (PMLabelLinkMass == null) return;
            double total = 0; bool missing = false;
            foreach (string id in link.ComponentIds)
            {
                try { total += massProps.Get(ComponentPathForId(id)).Mass; }
                catch (Exception) { missing = true; }
            }
            string s = link.ComponentIds.Count + " component(s), mass " + total.ToString("0.###") + " kg";
            if (missing) s += " (set material on all parts)";
            PMLabelLinkMass.Caption = s;
        }

        // Resolve a stored Name2 id to the component's part path for IMassProperties.
        private string ComponentPathForId(string name2)
        {
            object[] comps = (object[])((AssemblyDoc)model).GetComponents(true);
            if (comps != null)
                foreach (object o in comps)
                {
                    var c = (Component2)o;
                    if (c.Name2 == name2) return c.GetPathName();
                }
            return name2;
        }

        private void UpdateValidationLabel()
        {
            if (PMLabelLinkValidation == null) return;
            var issues = LinkDefValidator.Validate(config.Links, allComponentIds);
            PMLabelLinkValidation.Caption = issues.Count == 0
                ? "All components assigned."
                : issues.Count + " issue(s): " + issues[0];
        }

        private void GoToLink(int index)
        {
            if (config.Links.Count == 0) return;
            currentLinkIndex = index;
            LoadCurrentLink();
        }
```

- [ ] **Step 5: Assign / Clear / Add / Remove / Base handlers**

Read the selection box exactly like `GeometryPropertyManager.ReadSelectionBoxNames` /
`DescribeSelection` (copy those two helpers, using `LinkSelectionMark`). Then:

```csharp
        private void AssignCurrentLink()
        {
            LinkDef link = CurrentLink;
            if (link == null) return;
            List<string> names = ReadSelectionBoxNames();
            if (names.Count == 0)
            {
                MessageBox.Show("Pick one or more components in the viewport, then Assign.");
                return;
            }
            link.ComponentIds = names;
            string raw = PMTextLinkName.Text;
            if (!string.IsNullOrWhiteSpace(raw))
            {
                link.Name = RosNameSanitizer.Sanitize(raw).Value;
                PMTextLinkName.Text = link.Name;
            }
            PopulateLinkCombo();
            LoadCurrentLink();
        }

        private void ClearCurrentLink()
        {
            LinkDef link = CurrentLink;
            if (link == null) return;
            link.ComponentIds = new List<string>();
            model.ClearSelection2(true);
            LoadCurrentLink();
        }

        private void AddLink()
        {
            config.Links.Add(new LinkDef { Name = RosNameSanitizer.Sanitize("link_" + (config.Links.Count + 1)).Value });
            currentLinkIndex = config.Links.Count - 1;
            PopulateLinkCombo();
            LoadCurrentLink();
        }

        private void RemoveLink()
        {
            if (config.Links.Count == 0) return;
            config.Links.RemoveAt(currentLinkIndex);
            if (currentLinkIndex >= config.Links.Count) currentLinkIndex = config.Links.Count - 1;
            PopulateLinkCombo();
            LoadCurrentLink();
        }

        private void SetCurrentBase(bool isBase)
        {
            LinkDef link = CurrentLink;
            if (link == null) return;
            if (isBase) foreach (LinkDef l in config.Links) l.IsBase = false;
            link.IsBase = isBase;
            UpdateValidationLabel();
        }
```

- [ ] **Step 6: Wire the handlers**

Extend the existing handler methods (do not duplicate them):
- `OnButtonPress`: add cases `ButtonAssignID→AssignCurrentLink()`, `ButtonClearID→ClearCurrentLink()`, `ButtonAddLinkID→AddLink()`, `ButtonRemoveLinkID→RemoveLink()`, `ButtonPrevLinkID→GoToLink(currentLinkIndex-1)`, `ButtonNextLinkID→GoToLink(currentLinkIndex+1)`.
- `OnComboboxSelectionChanged`: add `if (Id == ComboLinkID) GoToLink(Item);`.
- `OnCheckboxCheck`: `if (Id == CheckBaseID) SetCurrentBase(Checked);`.
- `OnSelectionboxListChanged`: `if (Id == SelectionLinkID) UpdateValidationLabel();` (optional).
- `OnSubmitSelection`: for `SelectionLinkID`, accept only `swSelCOMPONENTS`/`swSelSOLIDBODIES` (copy the geometry PMP logic).
- `AfterActivation`: when `currentStep == 2`, call `PMSelectionLink?.SetSelectionFocus();`.

- [ ] **Step 7: Validation gate on Next (Step 3 only)**

In `GoNext`, before advancing past Step 3, block on validation:
```csharp
            if (currentStep == 2)
            {
                var issues = LinkDefValidator.Validate(config.Links, allComponentIds);
                if (issues.Count > 0)
                {
                    swApp.SendMsgToUser("Resolve link issues before continuing:\n• " +
                        string.Join("\n• ", issues.ToArray()));
                    return;
                }
            }
```

- [ ] **Step 8: Build (workstation) + commit**

Close SOLIDWORKS first (DLL lock). Then:
```
$env:SolidWorksDir="C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\"; $env:SolutionDir="C:\aryan\SW2GZ\"
& "<VS BuildTools MSBuild>" C:\aryan\SW2GZ\SW2GZ\SW2GZ.csproj /t:Build /p:Configuration=Release /p:RegisterForComInterop=false /v:minimal /clp:ErrorsOnly
```
Expect EXIT=0, DLL refreshed at `bin\Release\SW2GZ.dll`.
```bash
git add SW2GZ/URDFExport/Sw2gzExportPmp.cs
git commit -m "feat(addin): Step 3 Links - seed/assign/base/validate with live viewport picking"
```

---

## Task 4: Workstation verification (manual)

- [ ] Reopen SOLIDWORKS, open an assembly, SW2GZ → Next → Next to **Step 3 (Geometry)**.
- [ ] Links pre-seeded one per top-level component; "Link i of N" updates on Prev/Next.
- [ ] Pick components in the viewport → Assign; mass readout updates; Clear empties.
- [ ] Rename a link; check "Base (root) link" — only one stays base.
- [ ] Add link / Remove link work; merging (assign 2 comps to one link, remove the freed link) leaves no unassigned components.
- [ ] Next is blocked while components are unassigned / no base; allowed once clean.
- [ ] Save, close, reopen → Step 3 resumes with the same links + assignments.

---

## Self-Review

- **Spec coverage:** §1 fields → LinkDef + Step 3 controls; §2 model+config → Task 1; §3 validation → Task 2 + gate (Task 3 Step 7); §4 PMP group/seed/reuse → Task 3; §5 reuse map honored (mass via `IMassProperties`, names via `RosNameSanitizer`, selection via geometry-PMP copy); §6 files → all tasks; §7 out-of-scope respected (no collision/material/sensors/joints). Covered.
- **Type consistency:** `LinkDef.{Name,ComponentIds,IsBase}` used identically in codec, validator, PMP. `LinkDefValidator.Validate(IReadOnlyList<LinkDef>, IReadOnlyCollection<string>)` matches both call sites (tests + PMP). Control-id constants each defined once.
- **Placeholders:** none — pure parts have full code; COM parts give concrete snippets mirroring the proven `GeometryPropertyManager`.
- **Risk:** component identity via `Name2` (documented limitation); inertia tensor intentionally deferred to export.
