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
using System.Drawing;
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
            // ClientSize over Width/Height so the title bar + borders don't
            // eat into our content area. The old 520x280 fixed both *outer*
            // dimensions, leaving the 4-line status string clipped behind
            // the button row.
            ClientSize = new Size(600, 290);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            BackColor = SystemColors.Window;

            int warnings = 0;
            if (result.Report != null) foreach (var _ in result.Report.Warnings) warnings++;

            // Spin up the local server before showing the dialog so we can
            // surface "running on http://127.0.0.1:PORT" in the status label.
            string assetsDir = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "preview");
            string urdfForBrowser = StripXacroIncludes(result.UrdfOrSdfText);

            string serverUrl = null;
            string serverErr = null;
            try
            {
                _server = new PreviewServer(assetsDir, result.MeshesDir, urdfForBrowser, result.JointSampler);
                _server.Start();
                serverUrl = _server.Url;
                OpenBrowser(serverUrl);
            }
            catch (Exception ex)
            {
                serverErr = ex.Message;
            }

            // Root grid: info section grows / button bar pinned to bottom.
            // Replaces the prior Dock=Top + Dock=Bottom + fixed-height Panel
            // layout, which couldn't tell the header label to grow with
            // content and ended up clipping the live-sync hint.
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(18, 16, 18, 14),
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // ───── Info block: title · workspace · server URL · live-sync hint
            var info = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10),
            };

            var titleLabel = new Label
            {
                AutoSize = true,
                Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 8),
                Text = result.Mode + "   ·   " + Path.GetFileName(result.WorkspaceDir),
            };
            info.Controls.Add(titleLabel, 0, 0);

            if (warnings > 0)
            {
                var warnLabel = new Label
                {
                    AutoSize = true,
                    ForeColor = Color.DarkOrange,
                    Margin = new Padding(0, 0, 0, 6),
                    Text = "⚠  " + warnings + " warning" + (warnings == 1 ? "" : "s") +
                           " — see export log for details",
                };
                info.Controls.Add(warnLabel, 0, 1);
            }

            // Server URL gets its own row + selectable TextBox so the user
            // can copy/paste the port if the auto-opened browser tab
            // closed. A plain Label is non-selectable.
            var serverRow = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 6, 0, 0),
            };
            serverRow.Controls.Add(new Label
            {
                AutoSize = true,
                Margin = new Padding(0, 4, 6, 0),
                Text = serverUrl != null ? "Preview URL:" : "Server status:",
            });
            if (serverUrl != null)
            {
                serverRow.Controls.Add(new TextBox
                {
                    Text = serverUrl,
                    ReadOnly = true,
                    BorderStyle = BorderStyle.None,
                    BackColor = SystemColors.Window,
                    ForeColor = SystemColors.HotTrack,
                    Width = 220,
                    Margin = new Padding(0, 4, 0, 0),
                    Font = new Font(FontFamily.GenericMonospace, 9f),
                });
            }
            else
            {
                serverRow.Controls.Add(new Label
                {
                    AutoSize = true,
                    ForeColor = Color.Firebrick,
                    Margin = new Padding(0, 4, 0, 0),
                    Text = "failed to start — " + serverErr,
                });
            }
            info.Controls.Add(serverRow, 0, 2);

            if (serverUrl != null)
            {
                var hintLabel = new Label
                {
                    AutoSize = true,
                    ForeColor = SystemColors.GrayText,
                    Margin = new Padding(0, 8, 0, 0),
                    Text = "Move mates in SOLIDWORKS → the browser updates live (~100 ms).",
                };
                info.Controls.Add(hintLabel, 0, 3);
            }
            root.Controls.Add(info, 0, 0);

            // ───── Button bar: right-aligned, two visible groups
            // FlowDirection.RightToLeft → controls added first land
            // right-most. Order: Export | Back | Reopen | Open folder.
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false,
                Padding = new Padding(0),
                Margin = new Padding(0),
            };

            Button MakeButton(string text, bool primary = false)
            {
                var b = new Button
                {
                    Text = text,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Padding = new Padding(10, 4, 10, 4),
                    Margin = new Padding(6, 0, 0, 0),
                    MinimumSize = new Size(0, 30),
                    UseVisualStyleBackColor = true,
                };
                if (primary)
                {
                    b.BackColor = Color.FromArgb(245, 158, 11); // amber, matches Export ribbon accent
                    b.ForeColor = Color.Black;
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderColor = Color.FromArgb(180, 110, 0);
                }
                return b;
            }

            var confirm = MakeButton("Looks good — Export", primary: true);
            confirm.DialogResult = DialogResult.OK;
            var back = MakeButton("Back to edit");
            back.DialogResult = DialogResult.Cancel;
            var reopen = MakeButton("Reopen browser");
            reopen.Click += (s, e) => { if (_server != null) OpenBrowser(_server.Url); };
            reopen.Enabled = serverUrl != null;
            var openFolder = MakeButton("Open temp folder");
            openFolder.Click += (s, e) => OpenTempFolder();

            buttons.Controls.Add(confirm);
            buttons.Controls.Add(back);
            buttons.Controls.Add(reopen);
            buttons.Controls.Add(openFolder);
            AcceptButton = confirm;
            CancelButton = back;
            root.Controls.Add(buttons, 0, 1);

            Controls.Add(root);

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
