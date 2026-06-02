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
