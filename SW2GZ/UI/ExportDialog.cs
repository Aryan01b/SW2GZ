/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Modal export dialog: shows what the saved model contains (the "what's
implemented" confirmation) and collects the package meta, then the ribbon
Export command runs the bare-model export. Separate from the Create-Model
wizard, which only defines the structure.
*/
#if SW_INTEROP
using System;
using System.Windows.Forms;
using SW2GZ.URDFExport;

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
            Width = 470; Height = 360;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false; MinimizeBox = false;

            int links = config.Links != null ? config.Links.Count : 0;
            int joints = config.Joints != null ? config.Joints.Count : 0;
            Controls.Add(new Label
            {
                Left = 12, Top = 12, Width = 432, Height = 38,
                Text = "Implemented:  " + links + " link(s), " + joints + " joint(s).\r\n" +
                       "Export type:  bare robot model — no control, no Gazebo plugins.",
            });

            int y = 58;
            _out    = Row("Output folder", config.OutputFolder, ref y, browse: true);
            _pkg    = Row("Package name",  config.PackageName,  ref y);
            _author = Row("Author",        config.Author,       ref y);
            _email  = Row("Email",         config.Email,        ref y);

            Controls.Add(new Label { Left = 12, Top = y + 3, Width = 110, Text = "License" });
            _lic = new ComboBox { Left = 128, Top = y, Width = 312 };
            _lic.Items.AddRange(new object[] { "", "MIT", "Apache-2.0", "BSD-3-Clause", "GPL-3.0-only", "Proprietary" });
            _lic.Text = config.License ?? "";
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
            };
            Controls.Add(export);
            Controls.Add(cancel);
            AcceptButton = export;
            CancelButton = cancel;
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
