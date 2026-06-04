/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Read-only preview of what Sw2gzModelExporter would write. Driven by
Sw2gzModelPreviewer (which runs the real pipeline against a temp dir so the
user sees the authoritative output, not a hand-rolled summary).

Tabs:
  - Summary  — mode/pkg/links/joints/coord convention + output paths.
  - URDF/SDF — the .urdf.xacro (Robot Package) or model.sdf (Gz modes).
  - Launch   — the per-mode launch.py.
  - Log      — sw2gz_export.log produced by the pipeline.
  - Warnings — validation issues (warnings only; errors throw before preview).

Closing the dialog deletes the temp workspace (best-effort).
*/
#if SW_INTEROP
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using SW2GZ.URDFExport;

namespace SW2GZ.UI
{
    public sealed class PreviewDialog : Form
    {
        private readonly Sw2gzModelPreviewer.PreviewResult _result;

        public PreviewDialog(Sw2gzModelPreviewer.PreviewResult result)
        {
            _result = result ?? throw new ArgumentNullException(nameof(result));

            Text = "SW2GZ — Export preview (read-only)";
            Width = 880; Height = 620;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new System.Drawing.Size(620, 420);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true; MinimizeBox = false;

            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 36,
                Padding = new Padding(12, 8, 12, 0),
                Text = "Preview of " + result.Mode + " — review before exporting. " +
                       "Files shown are the actual output of the pipeline, written to a temp folder.",
            };
            Controls.Add(header);

            var buttons = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            Controls.Add(buttons);

            var openFolder = new Button { Text = "Open temp folder", Left = 12, Top = 10, Width = 140 };
            openFolder.Click += (s, e) => OpenTempFolder();
            buttons.Controls.Add(openFolder);

            var back = new Button { Text = "Back to edit",
                Left = buttons.ClientSize.Width - 256, Top = 10, Width = 124,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                DialogResult = DialogResult.Cancel };
            var confirm = new Button { Text = "Looks good — Export",
                Left = buttons.ClientSize.Width - 128, Top = 10, Width = 116,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                DialogResult = DialogResult.OK };
            buttons.Controls.Add(back);
            buttons.Controls.Add(confirm);
            AcceptButton = confirm;
            CancelButton = back;

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(MakeTab("Summary",
                result.SummaryText + WarningsBlock(result.Report)));
            tabs.TabPages.Add(MakeTab("TF frames", result.TfTreeText));
            tabs.TabPages.Add(MakeTab(result.UrdfOrSdfFileName, result.UrdfOrSdfText));
            tabs.TabPages.Add(MakeTab(result.LaunchFileName, result.LaunchText));
            tabs.TabPages.Add(MakeTab("sw2gz_export.log", result.LogText));
            Controls.Add(tabs);
            Controls.SetChildIndex(tabs, 0);   // fill below header, above buttons

            FormClosed += (s, e) => CleanupTempDir();
        }

        private TabPage MakeTab(string title, string text)
        {
            var page = new TabPage(title);
            var tb = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new System.Drawing.Font(System.Drawing.FontFamily.GenericMonospace, 9.0f),
                Text = text ?? string.Empty,
            };
            page.Controls.Add(tb);
            return page;
        }

        private static string WarningsBlock(SW2GZ.Validate.ValidationReport report)
        {
            if (report == null) return string.Empty;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine();
            sb.AppendLine("Warnings:");
            bool any = false;
            foreach (SW2GZ.Validate.ValidationIssue w in report.Warnings)
            {
                sb.AppendLine("  - [" + w.Code + "] " + w.Message);
                any = true;
            }
            if (!any) sb.AppendLine("  (none)");
            return sb.ToString();
        }

        private void OpenTempFolder()
        {
            try
            {
                if (!Directory.Exists(_result.WorkspaceDir)) return;
                Process.Start("explorer.exe", "\"" + _result.WorkspaceDir + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open folder: " + ex.Message,
                    "SW2GZ Preview", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void CleanupTempDir()
        {
            // Best-effort cleanup. If "Open temp folder" was clicked, Explorer
            // may still have a handle; ignore and let the OS reap on shutdown.
            try
            {
                if (Directory.Exists(_result.TempDir))
                    Directory.Delete(_result.TempDir, recursive: true);
            }
            catch { /* swallow — temp dir cleanup is best-effort */ }
        }
    }
}
#endif
