# Wizard Step 3 Link-Tree Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans. Steps use `- [ ]`.

**Goal:** Rework Step 3 into an editable link-hierarchy tree: links shown parent→child (root = base), drag to re-parent, rename in place, right-click "Set as base", and geometry assigned by picking it in the viewport (instant assign + move), with Add/Remove links.

**Architecture:** Pure `LinkDef.ParentName` + pure `LinkHierarchy` helpers + reworked `LinkDefValidator` (all net8-tested). A WinForms `LinkTreeView` embedded in the Step 3 PMP group via `WindowFromHandle` (mirroring `ExportPropertyManager`), driving the live `config.Links`. A thin selection-box "pick funnel" does instant assign+move.

**Tech Stack:** .NET Framework 4.8.1 add-in (SW COM + WinForms, `#if SW_INTEROP`); DataContract; xUnit net8 for pure parts; VS BuildTools MSBuild.

**Reference:** spec `docs/superpowers/specs/2026-06-02-wizard-step-3-link-tree-redesign.md`; tree-embed pattern `URDFExport/ExportPropertyManager.cs:955-977`.

**Note:** Tasks 1–3 are pure and verified by `dotnet test` (the test project does not compile the PMP). The add-in compiles only at Task 5's build — the interim addin state (old IsBase Step 3 code removed) is expected and fine.

---

## Task 1: `LinkDef.ParentName` (drop `IsBase`)

**Files:** edit `SW2GZ/Build/Model/LinkDef.cs`, `Test/URDFExport/Sw2gzExportConfigTests.cs`.

- [ ] **Step 1: Replace `IsBase` with `ParentName`**

`SW2GZ/Build/Model/LinkDef.cs` — replace the `IsBase` member:

```csharp
        [DataMember] public string Name { get; set; } = string.Empty;
        [DataMember] public List<string> ComponentIds { get; set; } = new List<string>();
        [DataMember] public string ParentName { get; set; } = string.Empty;   // "" = root (base)
```

- [ ] **Step 2: Update the codec round-trip test**

In `Test/URDFExport/Sw2gzExportConfigTests.cs`, change `RoundTrip_PreservesLinks` to use
`ParentName` instead of `IsBase`:

```csharp
            config.Links.Add(new SW2GZ.Build.Model.LinkDef
            {
                Name = "base_link",
                ParentName = "",
                ComponentIds = { "chassis-1@robot", "motor-1@robot" },
            });
            config.Links.Add(new SW2GZ.Build.Model.LinkDef { Name = "wheel_left", ParentName = "base_link" });
            ...
            Assert.Equal("", restored.Links[0].ParentName);
            Assert.Equal("base_link", restored.Links[1].ParentName);
```
(remove the `IsBase` asserts.)

- [ ] **Step 3: Run** `dotnet test Test/SW2GZ.Writers.Test.csproj --filter FullyQualifiedName~Sw2gzExportConfigTests` → PASS.

- [ ] **Step 4: Commit** `git commit -am "feat(addin): LinkDef.ParentName replaces IsBase (tree structure)"`

---

## Task 2: `LinkHierarchy` pure helpers (TDD)

**Files:** create `SW2GZ/Build/LinkHierarchy.cs`, `Test/Build/LinkHierarchyTests.cs`; register in `SW2GZ.csproj` + test csproj.

- [ ] **Step 1: Register source** — add to `SW2GZ.csproj` (beside `LinkDefValidator.cs`): `<Compile Include="Build\LinkHierarchy.cs" />`; to test csproj: `<Compile Include="..\SW2GZ\Build\LinkHierarchy.cs" Link="Sources\Build\LinkHierarchy.cs" />`.

- [ ] **Step 2: Write failing tests** `Test/Build/LinkHierarchyTests.cs`:

```csharp
/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Collections.Generic;
using System.Linq;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using Xunit;

namespace SW2GZ.Test.Build
{
    public class LinkHierarchyTests
    {
        private static LinkDef L(string name, string parent, params string[] ids) =>
            new LinkDef { Name = name, ParentName = parent, ComponentIds = new List<string>(ids) };

        private static List<LinkDef> Tree() => new List<LinkDef>
        {
            L("base", ""), L("arm", "base"), L("hand", "arm"), L("wheel", "base"),
        };

        [Fact]
        public void Roots_ReturnsParentlessLinks()
        {
            var roots = LinkHierarchy.Roots(Tree());
            Assert.Single(roots);
            Assert.Equal("base", roots[0].Name);
        }

        [Fact]
        public void ChildrenOf_ReturnsDirectChildren()
        {
            var kids = LinkHierarchy.ChildrenOf(Tree(), "base").Select(l => l.Name).ToList();
            Assert.Equal(new[] { "arm", "wheel" }, kids);
        }

        [Fact]
        public void IsDescendant_TrueForTransitiveChild()
        {
            Assert.True(LinkHierarchy.IsDescendant(Tree(), "base", "hand"));
            Assert.False(LinkHierarchy.IsDescendant(Tree(), "hand", "base"));
        }

        [Fact]
        public void HasCycle_DetectsLoop()
        {
            var links = new List<LinkDef> { L("a", "b"), L("b", "a") };
            Assert.True(LinkHierarchy.HasCycle(links));
            Assert.False(LinkHierarchy.HasCycle(Tree()));
        }

        [Fact]
        public void AssignComponent_MovesFromPreviousLink()
        {
            var links = new List<LinkDef> { L("base", "", "c1"), L("arm", "base") };
            LinkHierarchy.AssignComponent(links, "arm", "c1");
            Assert.Empty(links[0].ComponentIds);
            Assert.Equal(new[] { "c1" }, links[1].ComponentIds.ToArray());
        }

        [Fact]
        public void AssignComponent_NoDuplicateWhenAlreadyOnTarget()
        {
            var links = new List<LinkDef> { L("base", "", "c1") };
            LinkHierarchy.AssignComponent(links, "base", "c1");
            Assert.Equal(new[] { "c1" }, links[0].ComponentIds.ToArray());
        }

        [Fact]
        public void Reroot_MakesChosenLinkTheRoot()
        {
            var links = Tree();
            LinkHierarchy.Reroot(links, "arm");
            Assert.Equal("", links.First(l => l.Name == "arm").ParentName);
            Assert.Equal("arm", links.First(l => l.Name == "base").ParentName);
            Assert.False(LinkHierarchy.HasCycle(links));
            Assert.Single(LinkHierarchy.Roots(links));
        }
    }
}
```

- [ ] **Step 3: Run → FAIL** (type missing).

- [ ] **Step 4: Implement** `SW2GZ/Build/LinkHierarchy.cs`:

```csharp
/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — pure (COM-free) helpers for the Step 3 link hierarchy: roots, children,
descendant test, cycle detection, instant assign-with-move, and re-rooting.
Unit-tested in the net8 project; the WinForms LinkTreeView + PMP drive these.
*/
using System.Collections.Generic;
using SW2GZ.Build.Model;

namespace SW2GZ.Build
{
    public static class LinkHierarchy
    {
        public static List<LinkDef> Roots(IReadOnlyList<LinkDef> links)
        {
            var names = new HashSet<string>();
            foreach (LinkDef l in links) names.Add(l.Name);
            var roots = new List<LinkDef>();
            foreach (LinkDef l in links)
            {
                string p = l.ParentName ?? "";
                if (p.Length == 0 || !names.Contains(p)) roots.Add(l);
            }
            return roots;
        }

        public static List<LinkDef> ChildrenOf(IReadOnlyList<LinkDef> links, string name)
        {
            var kids = new List<LinkDef>();
            foreach (LinkDef l in links)
                if (string.Equals(l.ParentName, name)) kids.Add(l);
            return kids;
        }

        public static bool IsDescendant(IReadOnlyList<LinkDef> links, string ancestor, string candidate)
        {
            string cur = candidate;
            var guard = new HashSet<string>();
            while (!string.IsNullOrEmpty(cur) && guard.Add(cur))
            {
                LinkDef node = Find(links, cur);
                if (node == null) return false;
                if (string.Equals(node.ParentName, ancestor)) return true;
                cur = node.ParentName;
            }
            return false;
        }

        public static bool HasCycle(IReadOnlyList<LinkDef> links)
        {
            foreach (LinkDef start in links)
            {
                var seen = new HashSet<string>();
                string cur = start.Name;
                while (!string.IsNullOrEmpty(cur))
                {
                    if (!seen.Add(cur)) return true;
                    LinkDef node = Find(links, cur);
                    if (node == null) break;
                    cur = node.ParentName;
                }
            }
            return false;
        }

        public static void AssignComponent(IReadOnlyList<LinkDef> links, string activeName, string componentId)
        {
            foreach (LinkDef l in links)
                if (!string.Equals(l.Name, activeName))
                    l.ComponentIds.Remove(componentId);
            LinkDef target = Find(links, activeName);
            if (target != null && !target.ComponentIds.Contains(componentId))
                target.ComponentIds.Add(componentId);
        }

        public static void Reroot(IReadOnlyList<LinkDef> links, string newRootName)
        {
            // Reverse parent pointers along the path from newRoot up to the old root.
            LinkDef node = Find(links, newRootName);
            if (node == null) return;
            string prevParent = node.ParentName;
            node.ParentName = "";
            string childName = node.Name;
            while (!string.IsNullOrEmpty(prevParent))
            {
                LinkDef parent = Find(links, prevParent);
                if (parent == null) break;
                string grand = parent.ParentName;
                parent.ParentName = childName;
                childName = parent.Name;
                prevParent = grand;
            }
        }

        private static LinkDef Find(IReadOnlyList<LinkDef> links, string name)
        {
            foreach (LinkDef l in links)
                if (string.Equals(l.Name, name)) return l;
            return null;
        }
    }
}
```

- [ ] **Step 5: Run → PASS; full suite; commit** `git commit -am "feat(addin): LinkHierarchy pure helpers (roots/cycle/assign-move/reroot)"`

---

## Task 3: Rework `LinkDefValidator` for hierarchy (TDD)

**Files:** edit `SW2GZ/Build/LinkDefValidator.cs`, `Test/Build/LinkDefValidatorTests.cs`.

- [ ] **Step 1: Replace the tests' base-flag cases with hierarchy cases**

Rewrite `Test/Build/LinkDefValidatorTests.cs`:

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
        private static LinkDef L(string name, string parent, params string[] ids) =>
            new LinkDef { Name = name, ParentName = parent, ComponentIds = new List<string>(ids) };

        [Fact]
        public void Valid_SingleRoot_FullCoverage_UniqueNames()
        {
            var links = new List<LinkDef> { L("base", "", "a"), L("wheel", "base", "b") };
            Assert.Empty(LinkDefValidator.Validate(links, new[] { "a", "b" }));
        }

        [Fact]
        public void Flags_NoRoot_And_MultipleRoots()
        {
            var two = new List<LinkDef> { L("a", "", "x"), L("b", "", "y") };
            Assert.Contains(LinkDefValidator.Validate(two, new[] { "x", "y" }), i => i.Contains("root"));

            var cyc = new List<LinkDef> { L("a", "b", "x"), L("b", "a", "y") };
            Assert.Contains(LinkDefValidator.Validate(cyc, new[] { "x", "y" }), i => i.Contains("root") || i.Contains("cycle"));
        }

        [Fact]
        public void Flags_UnknownParent()
        {
            var links = new List<LinkDef> { L("base", "", "a"), L("arm", "ghost", "b") };
            Assert.Contains(LinkDefValidator.Validate(links, new[] { "a", "b" }), i => i.Contains("parent"));
        }

        [Fact]
        public void Flags_Unassigned_And_EmptyLink_And_DuplicateName()
        {
            var links = new List<LinkDef> { L("dup", "", "a"), L("dup", "dup") };
            var issues = LinkDefValidator.Validate(links, new[] { "a", "b" });
            Assert.Contains(issues, i => i.Contains("unassigned") && i.Contains("b"));
            Assert.Contains(issues, i => i.Contains("no components"));
            Assert.Contains(issues, i => i.Contains("name"));
        }
    }
}
```

- [ ] **Step 2: Run → FAIL** (old validator uses IsBase).

- [ ] **Step 3: Rewrite the validator** `SW2GZ/Build/LinkDefValidator.cs`:

```csharp
/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — pure validation for the Step 3 link hierarchy. Blocking issues (empty =
ready to advance): exactly one root, valid parents, no cycle, unique non-empty
names, no empty link, and full component coverage. COM-free + unit-tested.
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

            var names = new HashSet<string>();
            foreach (LinkDef l in links) names.Add(l.Name ?? "");

            // Roots: exactly one parentless (or unknown-parent) link.
            List<LinkDef> roots = LinkHierarchy.Roots(links);
            if (links.Count > 0 && roots.Count == 0)
                issues.Add("No root (base) link — every link has a parent (cycle?).");
            else if (roots.Count > 1)
                issues.Add("More than one root link — exactly one base is allowed.");

            if (LinkHierarchy.HasCycle(links))
                issues.Add("The link hierarchy has a cycle.");

            // Names + parents + empty links.
            var seenNames = new HashSet<string>();
            foreach (LinkDef l in links)
            {
                string name = (l.Name ?? "").Trim();
                if (name.Length == 0) issues.Add("A link has an empty name.");
                else if (!seenNames.Add(name)) issues.Add("Duplicate link name: " + name);

                string p = l.ParentName ?? "";
                if (p.Length > 0 && !names.Contains(p))
                    issues.Add("Link '" + name + "' has an unknown parent: " + p);

                if (l.ComponentIds == null || l.ComponentIds.Count == 0)
                    issues.Add("Link '" + name + "' has no components assigned.");
            }

            // Coverage + duplicates.
            var once = new HashSet<string>();
            var twice = new HashSet<string>();
            foreach (LinkDef l in links)
                if (l.ComponentIds != null)
                    foreach (string id in l.ComponentIds)
                        if (!once.Add(id)) twice.Add(id);
            foreach (string id in twice)
                issues.Add("Component assigned to more than one link: " + id);
            if (allComponentIds != null)
                foreach (string id in allComponentIds)
                    if (!once.Contains(id)) issues.Add("Component unassigned: " + id);

            return issues;
        }
    }
}
```

- [ ] **Step 4: Run → PASS; full suite; commit** `git commit -am "feat(addin): LinkDefValidator hierarchy rules (one root, acyclic, coverage)"`

---

## Task 4: `LinkTreeView` WinForms control (addin-only)

**Files:** create `SW2GZ/UI/LinkTreeView.cs`; register in `SW2GZ.csproj` (`<Compile Include="UI\LinkTreeView.cs" />`). Not in the test project (WinForms).

- [ ] **Step 1: Implement the control**

A `System.Windows.Forms.TreeView` over `List<LinkDef>`: rebuild from `ParentName`, component
leaves under each link, label-edit rename, link-only drag-reparent (cycle-guarded via
`LinkHierarchy.IsDescendant`), right-click "Set as base" (`LinkHierarchy.Reroot`), and an
`ActiveLinkChanged` / `LinksChanged` event pair. Full code:

```csharp
/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — WinForms TreeView of the Step 3 link hierarchy, embedded in the SW2GZ
export PropertyManagerPage via WindowFromHandle (see ExportPropertyManager).
Operates on the live List<LinkDef>: link nodes (Tag = LinkDef) parent→child,
each link's assigned components shown as non-draggable leaf nodes (Tag = string).
Drag a link onto another to re-parent (cycle-guarded); F2 / double-click renames;
right-click → "Set as base link" re-roots. Raises LinksChanged on any edit and
ActiveLinkChanged when the selected link changes.

Addin-only (WinForms) — not source-linked into the net8 test project. The pure
hierarchy logic it calls (SW2GZ.Build.LinkHierarchy) is unit-tested separately.
*/
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using SW2GZ.Build;
using SW2GZ.Build.Model;

namespace SW2GZ.UI
{
    public sealed class LinkTreeView : TreeView
    {
        private List<LinkDef> links;

        public event EventHandler<LinkDef> ActiveLinkChanged = delegate { };
        public event EventHandler LinksChanged = delegate { };

        public LinkTreeView()
        {
            LabelEdit = true;
            AllowDrop = true;
            HideSelection = false;
            ItemDrag += OnItemDrag;
            DragEnter += (s, e) => e.Effect = DragDropEffects.Move;
            DragOver += OnDragOver;
            DragDrop += OnDragDrop;
            AfterSelect += OnAfterSelect;
            AfterLabelEdit += OnAfterLabelEdit;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Set as base link", null, OnSetAsBase);
            ContextMenuStrip = menu;
        }

        public LinkDef ActiveLink
        {
            get
            {
                TreeNode n = SelectedNode;
                while (n != null && !(n.Tag is LinkDef)) n = n.Parent;
                return n?.Tag as LinkDef;
            }
        }

        public void SetLinks(List<LinkDef> value)
        {
            links = value;
            Rebuild();
        }

        public void Rebuild()
        {
            BeginUpdate();
            string activeName = ActiveLink?.Name;
            Nodes.Clear();
            if (links != null)
                foreach (LinkDef root in LinkHierarchy.Roots(links))
                    Nodes.Add(BuildNode(root));
            ExpandAll();
            EndUpdate();
            if (activeName != null) SelectByLinkName(activeName);
        }

        private TreeNode BuildNode(LinkDef link)
        {
            bool isRoot = string.IsNullOrEmpty(link.ParentName);
            var node = new TreeNode((link.Name ?? "") + (isRoot ? "  (base)" : "")) { Tag = link };
            foreach (string id in link.ComponentIds)
                node.Nodes.Add(new TreeNode("• " + id) { Tag = id, ForeColor = System.Drawing.Color.DimGray });
            foreach (LinkDef child in LinkHierarchy.ChildrenOf(links, link.Name))
                node.Nodes.Add(BuildNode(child));
            return node;
        }

        public void SelectByLinkName(string name)
        {
            foreach (TreeNode n in AllNodes(Nodes))
                if (n.Tag is LinkDef l && l.Name == name) { SelectedNode = n; return; }
        }

        private static IEnumerable<TreeNode> AllNodes(TreeNodeCollection nodes)
        {
            foreach (TreeNode n in nodes)
            {
                yield return n;
                foreach (TreeNode c in AllNodes(n.Nodes)) yield return c;
            }
        }

        private void OnAfterSelect(object sender, TreeViewEventArgs e) => ActiveLinkChanged(this, ActiveLink);

        private void OnAfterLabelEdit(object sender, NodeLabelEditEventArgs e)
        {
            if (!(e.Node.Tag is LinkDef link) || e.Label == null) { e.CancelEdit = true; return; }
            string sanitized = RosNameSanitizer.Sanitize(e.Label).Value;
            if (string.IsNullOrEmpty(sanitized)) { e.CancelEdit = true; return; }
            // Re-point children that referenced the old name.
            string old = link.Name;
            foreach (LinkDef l in links) if (l.ParentName == old) l.ParentName = sanitized;
            link.Name = sanitized;
            e.CancelEdit = true;   // we set text via Rebuild to keep the "(base)" badge
            Rebuild();
            LinksChanged(this, EventArgs.Empty);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F2 && SelectedNode != null && SelectedNode.Tag is LinkDef)
                SelectedNode.BeginEdit();
            base.OnKeyDown(e);
        }

        private void OnItemDrag(object sender, ItemDragEventArgs e)
        {
            if (e.Item is TreeNode n && n.Tag is LinkDef) DoDragDrop(n, DragDropEffects.Move);
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            var dragged = (TreeNode)e.Data.GetData(typeof(TreeNode));
            TreeNode target = GetNodeAt(PointToClient(new System.Drawing.Point(e.X, e.Y)));
            bool ok = dragged?.Tag is LinkDef a && target?.Tag is LinkDef b &&
                      a != b && !LinkHierarchy.IsDescendant(links, a.Name, b.Name);
            e.Effect = ok ? DragDropEffects.Move : DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            var dragged = (TreeNode)e.Data.GetData(typeof(TreeNode));
            TreeNode target = GetNodeAt(PointToClient(new System.Drawing.Point(e.X, e.Y)));
            if (dragged?.Tag is LinkDef a && target?.Tag is LinkDef b &&
                a != b && !LinkHierarchy.IsDescendant(links, a.Name, b.Name))
            {
                a.ParentName = b.Name;
                Rebuild();
                LinksChanged(this, EventArgs.Empty);
            }
        }

        private void OnSetAsBase(object sender, EventArgs e)
        {
            LinkDef link = ActiveLink;
            if (link == null) return;
            LinkHierarchy.Reroot(links, link.Name);
            Rebuild();
            LinksChanged(this, EventArgs.Empty);
        }
    }
}
```

- [ ] **Step 2: Commit** (after Task 5 builds) — bundled with Task 5 since the addin only compiles there.

---

## Task 5: Rewrite Step 3 in the PMP (embed tree + pick funnel + Add/Remove)

**Files:** edit `SW2GZ/URDFExport/Sw2gzExportPmp.cs`.

Replaces the old Step 3 controls (combobox, name textbox, base checkbox, Assign/Clear/Prev/Next).
Keeps `SeedLinksFromAssembly` but seeds a single tree (root + children). Adds the embedded
`LinkTreeView`, the pick-funnel selection box, and Add/Remove buttons.

- [ ] **Step 1: Replace Step 3 fields** — remove `PMComboLink`, `PMTextLinkName`, `PMCheckBase`,
  `PMButtonAssignLink`, `PMButtonClearLink`, `PMButtonPrevLink`, `PMButtonNextLink`,
  `PMLabelLinkProgress`, `currentLinkIndex`; add:

```csharp
        private PropertyManagerPageWindowFromHandle PMTreeHandle;
        private LinkTreeView linkTree;
        private PropertyManagerPageSelectionbox PMPickFunnel;
        private PropertyManagerPageButton PMButtonAddLink;
        private PropertyManagerPageButton PMButtonRemoveLink;
        private PropertyManagerPageLabel PMLabelLinkValidation;
        private LinkDef activeLink;
```
Keep `PMLabelLinkMass`, `massProps`, `allComponentIds`, `LinkSelectionMark`.

- [ ] **Step 2: Replace Step 3 IDs** with:

```csharp
        private const int TreeHandleID          = StepIdBase + 2 * 20 + 2;
        private const int PickFunnelID          = StepIdBase + 2 * 20 + 3;
        private const int ButtonAddLinkID       = StepIdBase + 2 * 20 + 4;
        private const int ButtonRemoveLinkID    = StepIdBase + 2 * 20 + 5;
        private const int LabelLinkMassID       = StepIdBase + 2 * 20 + 6;
        private const int LabelLinkValidationID = StepIdBase + 2 * 20 + 7;
```

- [ ] **Step 3: `SeedLinksFromAssembly` — seed a single tree** (root = grounded/first, rest parented under it):

```csharp
        private void SeedLinksFromAssembly()
        {
            allComponentIds.Clear();
            object[] comps = (object[])((AssemblyDoc)model).GetComponents(true);
            var top = new List<Component2>();
            if (comps != null)
                foreach (object o in comps)
                {
                    var c = (Component2)o;
                    if (c.IsSuppressed()) continue;
                    top.Add(c); allComponentIds.Add(c.Name2);
                }

            if (config.Links == null) config.Links = new List<LinkDef>();
            if (config.Links.Count > 0) return;

            int rootIdx = 0;
            for (int i = 0; i < top.Count; i++) { try { if (top[i].IsFixed()) { rootIdx = i; break; } } catch { } }
            string rootName = top.Count > 0 ? RosNameSanitizer.Sanitize(top[rootIdx].Name2).Value : "base_link";

            for (int i = 0; i < top.Count; i++)
            {
                string nm = RosNameSanitizer.Sanitize(top[i].Name2).Value;
                config.Links.Add(new LinkDef
                {
                    Name = nm,
                    ComponentIds = new List<string> { top[i].Name2 },
                    ParentName = i == rootIdx ? "" : rootName,
                });
            }
        }
```

- [ ] **Step 4: `BuildLinksStep`** — embed the tree + funnel + buttons:

```csharp
        private void BuildLinksStep(PropertyManagerPageGroup group, int leftEdge, int indent, int visibleEnabled)
        {
            int labelOpts = (int)swAddControlOptions_e.swControlOptions_Visible;

            linkTree = new LinkTreeView { Height = 220, Visible = true };
            linkTree.ActiveLinkChanged += (s, l) => { activeLink = l; UpdateMassReadout(activeLink); UpdateValidationLabel(); if (PMPickFunnel != null) PMPickFunnel.SetSelectionFocus(); };
            linkTree.LinksChanged += (s, e) => UpdateValidationLabel();

            PMTreeHandle = (PropertyManagerPageWindowFromHandle)group.AddControl2(
                TreeHandleID, (short)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle,
                "Link tree", (short)leftEdge, visibleEnabled, "Drag to re-parent; F2 to rename; right-click to set base");
            PMTreeHandle.Height = 220;
            PMTreeHandle.SetWindowHandlex64(linkTree.Handle.ToInt64());

            PMPickFunnel = (PropertyManagerPageSelectionbox)group.AddControl2(
                PickFunnelID, (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "Pick geometry for the selected link", (short)indent, visibleEnabled,
                "Pick components in the viewport — they are assigned to the selected link instantly");
            var filters = new swSelectType_e[] { swSelectType_e.swSelCOMPONENTS, swSelectType_e.swSelSOLIDBODIES };
            PMPickFunnel.SingleEntityOnly = false;
            PMPickFunnel.Height = 30;
            PMPickFunnel.Mark = LinkSelectionMark;
            PMPickFunnel.SetSelectionFilters((object)filters);

            PMButtonAddLink = AddLinkButton(group, ButtonAddLinkID, "Add link", indent, visibleEnabled);
            PMButtonRemoveLink = AddLinkButton(group, ButtonRemoveLinkID, "Remove link", indent, visibleEnabled);

            PMLabelLinkMass = AddFieldLabel(group, LabelLinkMassID, "", leftEdge, labelOpts);
            PMLabelLinkValidation = AddFieldLabel(group, LabelLinkValidationID, "", leftEdge, labelOpts);

            linkTree.SetLinks(config.Links);
            UpdateValidationLabel();
        }
```

Keep the existing `AddLinkButton`, `AddFieldLabel`, `UpdateMassReadout`, `ComponentPathForId`,
`UpdateValidationLabel`, `ReadSelectionBoxNames`, `DescribeSelection` helpers. Update
`UpdateMassReadout` to take the active link (already does). Remove `PopulateLinkCombo`,
`LoadCurrentLink`, `GoToLink`, `AssignCurrentLink`, `ClearCurrentLink`, `SetCurrentBase`,
`CurrentLink`.

- [ ] **Step 5: Pick-funnel instant assign+move + Add/Remove**

```csharp
        private void OnFunnelChanged()
        {
            if (activeLink == null || linkTree == null) return;
            foreach (string id in ReadSelectionBoxNames())
                LinkHierarchy.AssignComponent(config.Links, activeLink.Name, id);
            model.ClearSelection2(true);
            linkTree.Rebuild();
            UpdateMassReadout(activeLink);
            UpdateValidationLabel();
        }

        private void AddLink()
        {
            string parent = activeLink?.Name ?? (LinkHierarchy.Roots(config.Links).Count > 0 ? LinkHierarchy.Roots(config.Links)[0].Name : "");
            var link = new LinkDef { Name = UniqueLinkName(), ParentName = parent };
            config.Links.Add(link);
            linkTree.SetLinks(config.Links);
            linkTree.SelectByLinkName(link.Name);
            UpdateValidationLabel();
        }

        private void RemoveLink()
        {
            if (activeLink == null || config.Links.Count <= 1) return;
            string removed = activeLink.Name, parent = activeLink.ParentName ?? "";
            foreach (LinkDef l in config.Links)
                if (l.ParentName == removed) l.ParentName = parent;     // children adopt grandparent
            config.Links.Remove(activeLink);
            if (LinkHierarchy.Roots(config.Links).Count == 0 && config.Links.Count > 0)
                config.Links[0].ParentName = "";                        // ensure a root remains
            activeLink = null;
            linkTree.SetLinks(config.Links);
            UpdateValidationLabel();
        }

        private string UniqueLinkName()
        {
            int n = config.Links.Count + 1;
            while (true)
            {
                string candidate = RosNameSanitizer.Sanitize("link_" + n).Value;
                bool taken = false;
                foreach (LinkDef l in config.Links) if (l.Name == candidate) { taken = true; break; }
                if (!taken) return candidate;
                n++;
            }
        }
```

- [ ] **Step 6: Wire handlers**
  - `OnButtonPress`: keep `ButtonAddLinkID→AddLink()`, `ButtonRemoveLinkID→RemoveLink()`; remove the Assign/Clear/Prev/Next cases.
  - `OnSelectionboxListChanged`: `if (Id == PickFunnelID) OnFunnelChanged();` (replace the old SelectionLinkID case).
  - `OnSubmitSelection`: filter on `PickFunnelID` (rename from SelectionLinkID).
  - `OnComboboxSelectionChanged`: drop the `ComboLinkID` branch (combo removed); keep the license branch.
  - `OnCheckboxCheck`: drop the `CheckBaseID` branch (now empty `{ }`).
  - `AfterActivation`: `if (currentStep == 2 && PMPickFunnel != null) PMPickFunnel.SetSelectionFocus();`.
  - `GoNext` validation gate: unchanged (still `LinkDefValidator.Validate(config.Links, allComponentIds)` at `currentStep == 2`).

- [ ] **Step 7: Register `LinkTreeView.cs`** in `SW2GZ.csproj`: `<Compile Include="UI\LinkTreeView.cs" />`.

- [ ] **Step 8: Build (close SOLIDWORKS first)**
```
$env:SolidWorksDir="C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\"; $env:SolutionDir="C:\aryan\SW2GZ\"
& "<VS BuildTools MSBuild>" C:\aryan\SW2GZ\SW2GZ\SW2GZ.csproj /t:Build /p:Configuration=Release /p:RegisterForComInterop=false /v:minimal /clp:ErrorsOnly
```
Expect EXIT=0, DLL refreshed at `bin\Release\SW2GZ.dll`.

- [ ] **Step 9: Commit** `git add -A && git commit -m "feat(addin): Step 3 link-tree - embedded hierarchy, instant assign+move, add/remove"`

---

## Task 6: Workstation verification

- [ ] Step 3 shows a **tree** of links (root badged "(base)"), one per component, parented under the base.
- [ ] Click a link → pick components in viewport → they move under that link instantly (and leave any prior link). Tree refreshes; mass updates.
- [ ] Double-click / F2 renames a node (sanitized). Drag a node onto another re-parents (rejected if it would cycle). Right-click → "Set as base link" re-roots.
- [ ] Add link / Remove link work; removing a parent reparents its children.
- [ ] Next is blocked while there are unassigned components / >1 root; allowed when clean.
- [ ] Save, close, reopen → tree + assignments resume.

---

## Self-Review

- **Spec coverage:** §1 ParentName → Task 1; §2 LinkHierarchy → Task 2; §3 validator → Task 3; §4 LinkTreeView + funnel + Add/Remove + seeding → Tasks 4–5; §5 Step-4 implication (no code) noted; §6 files all covered; §7/§8 out-of-scope/risks honored. Covered.
- **Type consistency:** `LinkDef.{Name,ComponentIds,ParentName}` consistent across codec/hierarchy/validator/tree/PMP. `LinkHierarchy.{Roots,ChildrenOf,IsDescendant,HasCycle,AssignComponent,Reroot}` signatures match all call sites. `LinkTreeView.{SetLinks,Rebuild,SelectByLinkName,ActiveLink,ActiveLinkChanged,LinksChanged}` match the PMP usage. Removed members (CurrentLink/GoToLink/etc.) are not referenced after Task 5.
- **Placeholders:** none — full code for pure + control + PMP wiring.
- **Risk:** WinForms-tree embedding + drag/label-edit/funnel is build-and-workstation-verified (pure layer tested); `Name2` identity limitation unchanged.
