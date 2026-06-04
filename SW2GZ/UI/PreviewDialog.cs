/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Browser-backed preview. The pipeline writes a real export to a temp dir;
this dialog spins up a local HttpListener (PreviewServer) on a random
127.0.0.1 port, opens the default browser at that URL, and lets the user
review the three.js render. Joint values stream from SW live via the
server's /joint_states endpoint — move a mate in SW, the browser updates
on the next ~100 ms poll.

The dialog itself stays open as the control center:
  - Open temp folder    — Explorer on the workspace.
  - Reopen browser      — relaunch the tab if the user closed it.
  - Back to edit        — close, return to ExportDialog.
  - Looks good — Export — close OK, ExportDialog proceeds to real export.

Closing the dialog stops the server and deletes the temp workspace.
*/
#if SW_INTEROP
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using SW2GZ.URDFExport;

namespace SW2GZ.UI
{
    public sealed class PreviewDialog : Form
    {
        private readonly Sw2gzModelPreviewer.PreviewResult _result;
        private PreviewServer _server;

        public PreviewDialog(Sw2gzModelPreviewer.PreviewResult result)
        {
            _result = result ?? throw new ArgumentNullException(nameof(result));

            Text = "SW2GZ — 3D preview (browser)";
            Width = 520; Height = 280;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;

            int warnings = 0;
            if (result.Report != null) foreach (var _ in result.Report.Warnings) warnings++;

            // Spin up the local server before showing the dialog so we can
            // surface "running on http://127.0.0.1:PORT" in the status label.
            string assetsDir = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "preview");
            string urdfForBrowser = StripXacroIncludes(result.UrdfOrSdfText);

            string statusText;
            try
            {
                _server = new PreviewServer(assetsDir, result.MeshesDir, urdfForBrowser, result.JointSampler);
                _server.Start();
                statusText = "Preview server: " + _server.Url + Environment.NewLine +
                             "Move mates in SolidWorks → browser updates live (~100 ms).";
                OpenBrowser(_server.Url);
            }
            catch (Exception ex)
            {
                statusText = "Preview server failed to start: " + ex.Message;
            }

            var header = new Label
            {
                Dock = DockStyle.Top,
                Height = 60,
                Padding = new Padding(12, 10, 12, 0),
                Text = result.Mode + "  ·  " + Path.GetFileName(result.WorkspaceDir) +
                       (warnings > 0 ? "  ·  " + warnings + " warning(s)" : "") +
                       Environment.NewLine + Environment.NewLine + statusText,
            };
            Controls.Add(header);

            var buttons = new Panel { Dock = DockStyle.Bottom, Height = 80, Padding = new Padding(12, 8, 12, 8) };
            Controls.Add(buttons);

            var openFolder = new Button { Text = "Open temp folder", Left = 12, Top = 8, Width = 140 };
            openFolder.Click += (s, e) => OpenTempFolder();
            buttons.Controls.Add(openFolder);

            var reopen = new Button { Text = "Reopen browser tab", Left = 160, Top = 8, Width = 150 };
            reopen.Click += (s, e) => { if (_server != null) OpenBrowser(_server.Url); };
            buttons.Controls.Add(reopen);

            var back = new Button
            {
                Text = "Back to edit",
                Left = 12, Top = 42, Width = 150,
                DialogResult = DialogResult.Cancel,
            };
            var confirm = new Button
            {
                Text = "Looks good — Export",
                Left = 168, Top = 42, Width = 150,
                DialogResult = DialogResult.OK,
            };
            buttons.Controls.Add(back);
            buttons.Controls.Add(confirm);
            AcceptButton = confirm;
            CancelButton = back;

            FormClosed += (s, e) =>
            {
                try { _server?.Stop(); } catch { }
                CleanupTempDir();
            };
        }

        // URDFLoader on the browser side doesn't process xacro:include /
        // xacro:* macros. Strip them out before serving so the parser sees
        // only standard URDF links/joints.
        private static string StripXacroIncludes(string urdfXml)
        {
            if (string.IsNullOrEmpty(urdfXml)) return string.Empty;
            return System.Text.RegularExpressions.Regex.Replace(urdfXml,
                @"<xacro:[^>]*\/>|<xacro:[^>]*>[\s\S]*?<\/xacro:[^>]*>", string.Empty);
        }

        private static void OpenBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,   // hand off to default browser
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open browser: " + ex.Message + Environment.NewLine +
                    "Manually open: " + url,
                    "SW2GZ Preview", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
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
            // Best-effort. Browser may still be holding mesh file handles; the
            // OS reaps once the tab is closed.
            try
            {
                if (Directory.Exists(_result.TempDir))
                    Directory.Delete(_result.TempDir, recursive: true);
            }
            catch { /* swallow */ }
        }
    }
}
#endif
