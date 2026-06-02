# SW2GZ — Wizard Step 3 Link-Tree Redesign

**Status:** Approved design · **Date:** 2026-06-02 · **Type:** feature rework (P8 — UI wizard)
**Supersedes:** the flat-list Step 3 in `2026-06-02-wizard-step-3-links-design.md`.

Reworks Step 3 from a flat link list + combobox into an **editable hierarchy tree**: the
robot's links shown parent→child, edited in place, with geometry assigned by simply
picking it in the viewport. This makes Step 3 own the **kinematic tree structure**; Step 4
(Joints) shrinks to setting joint type/axis/limits for each parent→child edge.

Decisions (2026-06-02, with the user):
- **3/4 boundary:** Step 3 defines the link tree (who is whose child); Step 4 = joint
  details per existing edge.
- **Base link:** the **tree root** (the one link with no parent). Re-rooting = drag a node
  to the top. No base checkbox.
- **Auto-assign:** picking a component in the viewport **instantly** assigns it to the
  active link; if it already belonged to another link it is **moved**. No Assign button.
- **Controls:** the tree (click = navigate, drag = re-parent, label-edit = rename) +
  **Add link / Remove link** only. Prev/Next/Clear/Base removed.

Feasibility is proven: `ExportPropertyManager` already embeds a WinForms `TreeView` in a
PMP via `swControlType_WindowFromHandle` + `SetWindowHandlex64`, with drag-drop reparenting,
label edit, and selection events — the same machinery this reuses.

---

## 1. Data model

`LinkDef` (in `Build/Model`) changes:

```
[DataContract] LinkDef
    [DataMember] string Name                  // unique, ROS-sanitized
    [DataMember] List<string> ComponentIds    // Component2.Name2; instant-assign target
    [DataMember] string ParentName            // "" / null = root (the base link)
```

`IsBase` is **removed** — the root (empty `ParentName`) is the base. `Sw2gzExportConfig.Links`
is unchanged in shape (`List<LinkDef>`); the codec round-trips `ParentName` automatically.

## 2. Pure hierarchy + assignment helpers (testable)

New `Build/LinkHierarchy.cs` (COM-free):

- `Roots(links)` → links with empty/unknown `ParentName`.
- `ChildrenOf(links, name)` → direct children.
- `HasCycle(links)` → true if any parent chain loops.
- `IsDescendant(links, ancestor, candidate)` → guards drag-drop (can't parent a node under
  its own descendant).
- `AssignComponent(links, activeName, componentId)` → removes `componentId` from whatever
  link currently holds it, then adds it to `activeName` (the instant-assign **move**).
- `Reroot(links, newRootName)` → reverses parent pointers along the path from the chosen
  link to the current root, so the chosen link becomes the single root (the base). Used by
  the "Set as base link" action; keeps the single-root + acyclic invariants.

All pure → unit-tested in the net8 project.

## 3. Validation (rework `LinkDefValidator`)

`Validate(links, allComponentIds)` rules become:

- **Exactly one root** (one link with empty `ParentName`) — that is the base.
- Every non-root `ParentName` references an existing link.
- **No cycle** (`HasCycle`).
- Names unique + non-empty.
- No link with zero components.
- Coverage: every `allComponentIds` assigned to exactly one link (no unassigned; duplicates
  are structurally prevented by `AssignComponent`'s move, but still checked).

Blocks "Next" past Step 3; the first issue is surfaced in a status label + on the Next gate.

## 4. UI — embedded link tree (COM / WinForms, addin-only)

New WinForms control `UI/LinkTreeView.cs : System.Windows.Forms.TreeView`, operating on the
live `List<LinkDef>`:

- **Rebuild(links)** — builds nodes from `ParentName` (link nodes, `Tag = LinkDef`), each
  link node showing its assigned components as **non-draggable leaf child nodes**
  (`Tag = string id`, prefixed "• "), and child *links* as expandable nodes. Node text =
  link name; root node badged "(base)".
- `LabelEdit = true`; `AfterLabelEdit` → sanitize via `RosNameSanitizer`, update
  `LinkDef.Name`, raise `LinksChanged`.
- Drag-drop (mirroring `ExportPropertyManager`): only **link** nodes drag; dropping link A
  onto link B sets `A.ParentName = B.Name`, rejected when `IsDescendant(links, A, B)` (would
  create a cycle). Raises `LinksChanged`.
- **Re-root / base:** right-click a node → "Set as base link" calls `LinkHierarchy.Reroot`,
  making it the single root. (Drag is reparent-only; it never creates a second root.)
- `AfterSelect` → raises `ActiveLinkChanged(LinkDef)` (selecting a component leaf reports its
  parent link as active).

Hosted in the Step 3 group via `WindowFromHandle` + `SetWindowHandlex64(tree.Handle)`
(exactly as `ExportPropertyManager` does), Height ≈ 220.

Below the tree:
- A thin **pick funnel** selection box (`swControlType_Selectionbox`, components/solid bodies)
  labelled "Pick geometry for the selected link". `OnSelectionboxListChanged` → for each id
  in the box, `LinkHierarchy.AssignComponent(links, activeLink, id)` (instant assign + move),
  then **clear** the box and `Rebuild` the tree. No Assign button.
- **Add link** / **Remove link** buttons. Add: new `LinkDef` parented under the active link
  (or root if none), unique default name, becomes active. Remove: reparent the removed link's
  children to its parent, unassign its components, delete it; removing the root promotes its
  first child to root.
- **Status label**: validation summary ("All components assigned." / "N issue(s): …") +
  unassigned count.

Seeding (first open, no checkpoint links): one root-less `LinkDef` per top-level component;
the grounded/fixed component (or first) is the **root**, the rest are parented under it by
default so there is a single valid tree to start from. Resume uses the checkpoint's `Links`.

Autosave: tree edits mutate the in-memory `config.Links` immediately (name on label-commit,
structure on drag, geometry on pick); the document checkpoint is written on Next/Finish
(existing mechanism). No per-keystroke disk writes.

## 5. Step 4 (Joints) implication (not built here)

Step 4 will iterate the parent→child edges defined by `ParentName` and set each joint's
type / axis / limits. No tree editing there. (Separate increment.)

## 6. Files

- **Edit:** `Build/Model/LinkDef.cs` (ParentName, drop IsBase), `Build/LinkDefValidator.cs`
  (hierarchy rules), `Test/...` (validator + codec + new hierarchy tests),
  `URDFExport/Sw2gzExportPmp.cs` (Step 3 group → embedded tree + funnel + Add/Remove),
  `SW2GZ.csproj` / test csproj (new sources).
- **New:** `Build/LinkHierarchy.cs` (pure helpers), `UI/LinkTreeView.cs` (WinForms tree,
  addin-only), `Test/Build/LinkHierarchyTests.cs`.

## 7. Out of scope

Joint details (Step 4), collision/material/gazebo/sensors, persistent-reference component
ids, multi-root robots, per-edit disk autosave.

## 8. Risks

- Embedded-WinForms-tree + drag-drop + label-edit + pick-funnel is COM/UI-heavy and **not
  unit-testable** — verified by build + workstation run, mirroring the proven
  `ExportPropertyManager` tree. The pure model/hierarchy/validation layer is fully tested.
- `Component2.Name2` identity (renamed/replaced components break assignments) — accepted,
  unchanged from prior Step 3.
