/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

3D-only preview of what Sw2gzModelExporter would write. The pipeline runs
against a temp directory; this dialog renders the resulting collision STLs
in a WPF Viewport3D (Robot3DViewport) — same code path as the real export,
so what you see is what you get.

Buttons:
  - Open temp folder    — Explorer on the workspace (for browsing files).
  - Back to edit        — close, return to ExportDialog.
  - Looks good — Export — close OK, ExportDialog proceeds to real export.

Closing the dialog deletes the temp workspace (best-effort).
*/
#if SW_INTEROP
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using SW2GZ.URDFExport;

namespace SW2GZ.UI
{
    public sealed class PreviewDialog : Form
    {
        private readonly Sw2gzModelPreviewer.PreviewResult _result;

        public PreviewDialog(Sw2gzModelPreviewer.PreviewResult result)
        {
            _result = result ?? throw new ArgumentNullException(nameof(result));

            Text = "SW2GZ — 3D preview";
            Width = 960; Height = 720;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new System.Drawing.Size(640, 480);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true; MinimizeBox = false;

            int warnings = 0;
            if (result.Report != null) foreach (var _ in result.Report.Warnings) warnings++;
            string warnSuffix = warnings > 0 ? "  |  " + warnings + " warning(s)" : "";

            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 28,
                Padding = new Padding(12, 6, 12, 0),
                Text = result.Mode + "  ·  " + System.IO.Path.GetFileName(result.WorkspaceDir) + warnSuffix,
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

            Control viewport;
            try
            {
                viewport = new ElementHost
                {
                    Dock = DockStyle.Fill,
                    Child = new Robot3DViewport(result.MeshesDir, result.UrdfOrSdfText),
                };
            }
            catch (Exception ex)
            {
                viewport = new Label
                {
                    Dock = DockStyle.Fill,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                    Text = "3D preview unavailable: " + ex.Message,
                };
            }
            Controls.Add(viewport);
            Controls.SetChildIndex(viewport, 0);   // fill between header and buttons

            FormClosed += (s, e) => CleanupTempDir();
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
