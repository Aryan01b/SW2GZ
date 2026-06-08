/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — WinForms TreeView of the Step 3 link hierarchy, embedded in the SW2GZ
export PropertyManagerPage via WindowFromHandle (see ExportPropertyManager).
Operates on the live List<LinkDef>: link nodes (Tag = LinkDef) parent->child,
each link's assigned components shown as non-draggable leaf nodes (Tag = string).
Drag a link onto another to re-parent (cycle-guarded); F2 / double-click renames;
right-click -> "Set as base link" re-roots. Raises LinksChanged on any edit and
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
        private TreeNode dropTarget;   // node currently highlighted as the drop parent

        public event EventHandler<LinkDef> ActiveLinkChanged = delegate { };
        public event EventHandler LinksChanged = delegate { };

        public LinkTreeView()
        {
            LabelEdit = true;
            AllowDrop = true;
            HideSelection = false;
            ShowNodeToolTips = true;
            ItemDrag += OnItemDrag;
            DragEnter += (s, e) => e.Effect = DragDropEffects.Move;
            DragOver += OnDragOver;
            DragLeave += (s, e) => ClearDropHighlight();
            DragDrop += OnDragDrop;
            AfterSelect += OnAfterSelect;
            AfterLabelEdit += OnAfterLabelEdit;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Set as base link", null, OnSetAsBase);
            ContextMenuStrip = menu;
        }

        private void ClearDropHighlight()
        {
            if (dropTarget != null)
            {
                dropTarget.BackColor = System.Drawing.Color.Empty;
                dropTarget = null;
            }
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
            int n = link.ComponentIds?.Count ?? 0;
            // Links only — the component-name leaf duplicated the link name and added
            // no information; show the part count as a suffix instead.
            string label = (link.Name ?? "")
                + (isRoot ? "  (base)" : "")
                + "  [" + n + (n == 1 ? " part]" : " parts]");
            var node = new TreeNode(label) { Tag = link };
            if (n == 0) node.ForeColor = System.Drawing.Color.Firebrick;   // unassigned = needs attention
            foreach (LinkDef child in LinkHierarchy.ChildrenOf(links, link.Name))
                node.Nodes.Add(BuildNode(child));
            return node;
        }

        public void SelectByLinkName(string name)
        {
            foreach (TreeNode n in AllNodes(Nodes))
                if (n.Tag is LinkDef l && l.Name == name) { SelectedNode = n; return; }
        }

        // Update the SELECTED node's display label without rebuilding the tree.
        // Used by the rename textbox so each keystroke doesn't fire a Rebuild +
        // ActiveLinkChanged cascade that would reset the textbox cursor to 0.
        public void RefreshActiveNodeLabel()
        {
            TreeNode n = SelectedNode;
            if (n == null || !(n.Tag is LinkDef link)) return;
            bool isRoot = string.IsNullOrEmpty(link.ParentName);
            int parts = link.ComponentIds?.Count ?? 0;
            n.Text = (link.Name ?? "")
                + (isRoot ? "  (base)" : "")
                + "  [" + parts + (parts == 1 ? " part]" : " parts]");
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

            // Highlight the prospective new parent so the drop destination is obvious.
            if (target != dropTarget)
            {
                ClearDropHighlight();
                if (ok)
                {
                    dropTarget = target;
                    dropTarget.BackColor = System.Drawing.Color.LightSkyBlue;
                    target.EnsureVisible();
                }
            }
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            var dragged = (TreeNode)e.Data.GetData(typeof(TreeNode));
            TreeNode target = GetNodeAt(PointToClient(new System.Drawing.Point(e.X, e.Y)));
            ClearDropHighlight();
            if (dragged?.Tag is LinkDef a && target?.Tag is LinkDef b &&
                a != b && !LinkHierarchy.IsDescendant(links, a.Name, b.Name))
            {
                a.ParentName = b.Name;
                Rebuild();
                SelectByLinkName(a.Name);
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
