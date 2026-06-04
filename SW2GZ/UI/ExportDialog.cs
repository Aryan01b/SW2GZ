/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Modal export dialog: shows what the saved model contains (the "what's
implemented" confirmation) and collects the package meta, then the ribbon
Export command runs the bare-model export. Separate from the Create-Model
wizard, which only defines the structure.
*/
#if SW_INTEROP
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using SW2GZ.Build.Model;
using SW2GZ.URDFExport;
using SW2GZ.Utilities;

namespace SW2GZ.UI
{
    public sealed class ExportDialog : Form
    {
        private readonly Sw2gzExportConfig _cfg;
        private readonly TextBox _out, _pkg, _author, _email;
        private readonly ComboBox _lic;

        public ExportDialog(Sw2gzExportConfig config)
        {
            _cfg = config;
            Text = "SW2GZ — Export model";
            Width = 470; Height = 410;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false; MinimizeBox = false;

            // Fill empty per-doc fields from cross-assembly user defaults so a brand-new
            // assembly inherits the user's identity instead of starting blank. The per-doc
            // checkpoint still wins when populated; user defaults only paper over empties.
            Sw2gzUserDefaults.Values defaults = Sw2gzUserDefaults.Load();
            string seedOut    = !string.IsNullOrEmpty(config.OutputFolder) ? config.OutputFolder : defaults.LastOutputFolder;
            string seedAuthor = !string.IsNullOrEmpty(config.Author)       ? config.Author       : defaults.Author;
            string seedEmail  = !string.IsNullOrEmpty(config.Email)        ? config.Email        : defaults.Email;
            string seedLic    = !string.IsNullOrEmpty(config.License)      ? config.License      : defaults.License;

            int links = config.Links != null ? config.Links.Count : 0;
            int joints = config.Joints != null ? config.Joints.Count : 0;
            Controls.Add(new Label
            {
                Left = 12, Top = 12, Width = 432, Height = 86,
                Text = SummaryText(config, links, joints),
            });

            int y = 106;
            _out    = Row("Output folder", seedOut,            ref y, browse: true);
            _pkg    = Row("Package name",  config.PackageName, ref y);
            _author = Row("Author",        seedAuthor,         ref y);
            _email  = Row("Email",         seedEmail,          ref y);

            Controls.Add(new Label { Left = 12, Top = y + 3, Width = 110, Text = "License" });
            _lic = new ComboBox { Left = 128, Top = y, Width = 312 };
            _lic.Items.AddRange(new object[] { "", "MIT", "Apache-2.0", "BSD-3-Clause", "GPL-3.0-only", "Proprietary" });
            _lic.Text = seedLic ?? "";
            Controls.Add(_lic);
            y += 38;

            var export = new Button { Text = "Export", Left = 256, Top = y, Width = 88, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 352, Top = y, Width = 88, DialogResult = DialogResult.Cancel };
            export.Click += (s, e) =>
            {
                _cfg.OutputFolder = _out.Text.Trim();
                _cfg.PackageName  = _pkg.Text.Trim();
                _cfg.Author       = _author.Text.Trim();
                _cfg.Email        = _email.Text.Trim();
                _cfg.License      = _lic.Text.Trim();

                // Persist user-stable fields across assemblies. PackageName is intentionally
                // omitted — it should always be project-specific.
                Sw2gzUserDefaults.Save(new Sw2gzUserDefaults.Values
                {
                    Author = _cfg.Author,
                    Email = _cfg.Email,
                    License = _cfg.License,
                    LastOutputFolder = _cfg.OutputFolder,
                });
            };
            Controls.Add(export);
            Controls.Add(cancel);
            AcceptButton = export;
            CancelButton = cancel;
        }

        // 4-line preview header: counts on line 1, link names on line 2, joint
        // edges on line 3, export-type note on line 4. Names/edges are truncated
        // with "+N more" so the dialog stays a fixed size even for big assemblies.
        private static string SummaryText(Sw2gzExportConfig config, int linkCount, int jointCount)
        {
            string linkLine = "  Links:    " + FormatLinkNames(config.Links);
            string jointLine = "  Joints:   " + FormatJointEdges(config.Joints);
            return "Implemented:  " + linkCount + " link(s), " + jointCount + " joint(s).\r\n" +
                   linkLine + "\r\n" +
                   jointLine + "\r\n" +
                   "Export type:  bare robot model — no control, no Gazebo plugins.";
        }

        private static string FormatLinkNames(List<LinkDef> links)
        {
            if (links == null || links.Count == 0) return "(none)";
            return TruncateList(links.Select(l => l.Name ?? "?"), maxChars: 70);
        }

        private static string FormatJointEdges(List<JointDef> joints)
        {
            if (joints == null || joints.Count == 0) return "(none)";
            return TruncateList(
                joints.Select(j =>
                    (string.IsNullOrEmpty(j.ParentLink) ? "?" : j.ParentLink) + "→" +
                    (string.IsNullOrEmpty(j.ChildLink) ? "?" : j.ChildLink) +
                    " (" + j.Type + ")"),
                maxChars: 70);
        }

        // Joins items with ", " until adding the next would exceed maxChars; the
        // remainder gets summarised as "+N more" so long lists never wrap the dialog.
        private static string TruncateList(IEnumerable<string> items, int maxChars)
        {
            var list = items.ToList();
            if (list.Count == 0) return "(none)";
            var sb = new System.Text.StringBuilder();
            int shown = 0;
            for (int i = 0; i < list.Count; i++)
            {
                string sep = (shown == 0) ? "" : ", ";
                int projected = sb.Length + sep.Length + list[i].Length;
                int remaining = list.Count - i;
                // Reserve room for ", +N more" if more items would follow.
                int reserve = (remaining > 1) ? (", +" + (remaining - 1) + " more").Length : 0;
                if (projected + reserve > maxChars && shown > 0) break;
                sb.Append(sep).Append(list[i]);
                shown++;
            }
            if (shown < list.Count)
                sb.Append(", +").Append(list.Count - shown).Append(" more");
            return sb.ToString();
        }

        private TextBox Row(string label, string value, ref int y, bool browse = false)
        {
            Controls.Add(new Label { Left = 12, Top = y + 3, Width = 110, Text = label });
            var tb = new TextBox { Left = 128, Top = y, Width = browse ? 226 : 312, Text = value ?? "" };
            Controls.Add(tb);
            if (browse)
            {
                var b = new Button { Text = "Browse...", Left = 360, Top = y - 1, Width = 80 };
                b.Click += (s, e) =>
                {
                    using (var d = new FolderBrowserDialog())
                    {
                        if (!string.IsNullOrEmpty(tb.Text)) d.SelectedPath = tb.Text;
                        if (d.ShowDialog() == DialogResult.OK) tb.Text = d.SelectedPath;
                    }
                };
                Controls.Add(b);
            }
            y += 34;
            return tb;
        }
    }
}
#endif
