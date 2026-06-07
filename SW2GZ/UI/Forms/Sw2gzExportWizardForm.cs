/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Sw2gzExportWizardForm — modal centered installer-style wizard launched from
the SW2GZ ribbon Export button. Replaces the v2.1.1 legacy ExportDialog
single-pane form with three pages:

    1) Meta      — output folder + package name + author + email + license
                   + read-only mode label
    2) Scope     — per-mode file tree preview ("what will be exported")
                   computed by Sw2gzExportScopePlanner; no code shown
    3) Run       — spinner while Sw2gzPipeline.Run executes, then result
                   panel (success: workspace path + Open folder button + Copy
                   launch cmd; error: bullet list of issues + Copy error log)

Code-only (no Designer noise). Pages are Panels owned by the form and swapped
via Visible toggling, mirroring the per-step PMP pattern from Sw2gzExportPmp.

CCW-marshalling caveat (Sw2gzCreateRobotPmp comment): the SW PMP handler path
needs DeferToIdle; this form has no PMP handler, so SwAddin.LaunchExport just
ShowDialog()s it directly.
*/
#if SW_INTEROP
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SW2GZ.Build;
using SW2GZ.URDFExport;
using SW2GZ.Utilities;

namespace SW2GZ.UI.Forms
{
    public sealed class Sw2gzExportWizardForm : Form
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        private readonly SldWorks _swApp;
        private readonly ModelDoc2 _modelDoc;
        private readonly Sw2gzDoc _doc;

        // ─── Page panels ─────────────────────────────────────────────
        private Panel _pageMeta;
        private Panel _pageScope;
        private Panel _pageRun;
        private Panel[] _pages;
        private int _currentPage;

        // ─── Meta controls ───────────────────────────────────────────
        private TextBox _txtOutput;
        private TextBox _txtPkg;
        private TextBox _txtAuthor;
        private TextBox _txtEmail;
        private TextBox _txtLicense;
        private Label _modeLabel;

        // ─── Scope controls ──────────────────────────────────────────
        private Label _scopeHeader;
        private Label _scopeCounts;
        private ListBox _scopeFiles;
        private Label _scopeWorkspace;

        // ─── Run controls ────────────────────────────────────────────
        private Label _runStatus;
        private ProgressBar _runSpinner;
        private TextBox _runDetail;
        private Button _btnOpenFolder;
        private Button _btnCopyCmd;
        private Button _btnCopyLog;
        private string _resultWorkspace;
        private string _resultLaunchCmd;
        private bool _resultOk;

        // ─── Footer nav ──────────────────────────────────────────────
        private Button _btnBack;
        private Button _btnNext;
        private Button _btnCancel;

        public Sw2gzExportWizardForm(SldWorks swApp, ModelDoc2 modelDoc, Sw2gzDoc doc)
        {
            _swApp = swApp ?? throw new ArgumentNullException(nameof(swApp));
            _modelDoc = modelDoc ?? throw new ArgumentNullException(nameof(modelDoc));
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));

            Text            = "SW2GZ — Export";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition   = FormStartPosition.CenterScreen;
            MaximizeBox     = false;
            MinimizeBox     = false;
            ShowInTaskbar   = false;
            Size            = new Size(560, 520);
            BackColor       = SystemColors.Window;

            BuildPages();
            BuildNav();
            ApplyDefaults();
            ShowPage(0);
        }

        // ─── Page build ──────────────────────────────────────────────
        private void BuildPages()
        {
            _pageMeta  = BuildMetaPage();
            _pageScope = BuildScopePage();
            _pageRun   = BuildRunPage();
            _pages = new[] { _pageMeta, _pageScope, _pageRun };
            foreach (var p in _pages)
            {
                p.Dock = DockStyle.Top;
                p.Height = 410;
                Controls.Add(p);
            }
        }

        private Panel BuildMetaPage()
        {
            var p = new Panel { Padding = new Padding(20) };

            int y = 12;
            _modeLabel = AddLabel(p, "Mode: " + ModeText(_doc.Mode), 12, y,
                new Font(SystemFonts.MessageBoxFont.FontFamily, 11, FontStyle.Bold));
            y += 30;
            AddLabel(p, "Output folder", 12, y);
            _txtOutput = new TextBox { Location = new Point(12, y + 18), Width = 410 };
            var browse = new Button { Text = "Browse…", Location = new Point(425, y + 16), Width = 90 };
            browse.Click += (s, e) => BrowseOutput();
            p.Controls.Add(_txtOutput);
            p.Controls.Add(browse);
            y += 56;

            AddLabel(p, "Package name", 12, y);
            _txtPkg = new TextBox { Location = new Point(12, y + 18), Width = 503 };
            p.Controls.Add(_txtPkg);
            y += 56;

            AddLabel(p, "Author", 12, y);
            _txtAuthor = new TextBox { Location = new Point(12, y + 18), Width = 245 };
            AddLabel(p, "Email", 270, y);
            _txtEmail = new TextBox { Location = new Point(270, y + 18), Width = 245 };
            p.Controls.Add(_txtAuthor);
            p.Controls.Add(_txtEmail);
            y += 56;

            AddLabel(p, "License", 12, y);
            _txtLicense = new TextBox { Location = new Point(12, y + 18), Width = 503 };
            p.Controls.Add(_txtLicense);
            return p;
        }

        private Panel BuildScopePage()
        {
            var p = new Panel { Padding = new Padding(20) };
            _scopeHeader = AddLabel(p, "", 12, 12,
                new Font(SystemFonts.MessageBoxFont.FontFamily, 11, FontStyle.Bold));
            _scopeCounts = AddLabel(p, "", 12, 38);
            _scopeWorkspace = AddLabel(p, "", 12, 62);
            _scopeWorkspace.ForeColor = Color.DimGray;

            AddLabel(p, "Files that will be written:", 12, 92);
            _scopeFiles = new ListBox
            {
                Location = new Point(12, 112),
                Width = 510,
                Height = 250,
                Font = new Font(FontFamily.GenericMonospace, 9),
                IntegralHeight = false,
            };
            p.Controls.Add(_scopeFiles);
            return p;
        }

        private Panel BuildRunPage()
        {
            var p = new Panel { Padding = new Padding(20) };
            _runStatus = AddLabel(p, "", 12, 16,
                new Font(SystemFonts.MessageBoxFont.FontFamily, 11, FontStyle.Bold));
            _runSpinner = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                Location = new Point(12, 50),
                Width = 510,
                Height = 18,
                MarqueeAnimationSpeed = 30,
                Visible = false,
            };
            p.Controls.Add(_runSpinner);

            _runDetail = new TextBox
            {
                Location = new Point(12, 80),
                Width = 510,
                Height = 230,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font(FontFamily.GenericMonospace, 9),
            };
            p.Controls.Add(_runDetail);

            _btnOpenFolder = new Button
            {
                Text = "Open output folder",
                Location = new Point(12, 322),
                Width = 170,
                Visible = false,
            };
            _btnOpenFolder.Click += (s, e) => SafeOpenFolder();
            _btnCopyCmd = new Button
            {
                Text = "Copy launch command",
                Location = new Point(190, 322),
                Width = 170,
                Visible = false,
            };
            _btnCopyCmd.Click += (s, e) => SafeClipboard(_resultLaunchCmd);
            _btnCopyLog = new Button
            {
                Text = "Copy error log",
                Location = new Point(12, 322),
                Width = 170,
                Visible = false,
            };
            _btnCopyLog.Click += (s, e) => SafeClipboard(_runDetail.Text);
            p.Controls.Add(_btnOpenFolder);
            p.Controls.Add(_btnCopyCmd);
            p.Controls.Add(_btnCopyLog);
            return p;
        }

        private static Label AddLabel(Panel p, string text, int x, int y, Font font = null)
        {
            var l = new Label
            {
                Text = text,
                AutoSize = true,
                Location = new Point(x, y),
                Font = font ?? SystemFonts.MessageBoxFont,
            };
            p.Controls.Add(l);
            return l;
        }

        private void BuildNav()
        {
            var nav = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                BackColor = SystemColors.Control,
            };
            _btnBack = new Button { Text = "< Back",   Location = new Point(280, 10), Width = 80 };
            _btnNext = new Button { Text = "Next >",   Location = new Point(370, 10), Width = 80 };
            _btnCancel = new Button { Text = "Cancel", Location = new Point(460, 10), Width = 80 };
            _btnBack.Click += (s, e) => GoBack();
            _btnNext.Click += (s, e) => GoNext();
            _btnCancel.Click += (s, e) => Close();
            nav.Controls.Add(_btnBack);
            nav.Controls.Add(_btnNext);
            nav.Controls.Add(_btnCancel);
            Controls.Add(nav);
        }

        // ─── Defaults / scope refresh ────────────────────────────────
        private void ApplyDefaults()
        {
            var defaults = Sw2gzUserDefaults.Load();
            _txtAuthor.Text  = defaults.Author;
            _txtEmail.Text   = defaults.Email;
            _txtLicense.Text = defaults.License;
            _txtOutput.Text  = string.IsNullOrEmpty(defaults.LastOutputFolder)
                ? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "SW2GZ Exports")
                : defaults.LastOutputFolder;
            _txtPkg.Text     = DefaultPackageName();
        }

        private string DefaultPackageName()
        {
            try
            {
                string path = _modelDoc.GetPathName();
                string raw  = !string.IsNullOrEmpty(path)
                    ? Path.GetFileNameWithoutExtension(path)
                    : _modelDoc.GetTitle();
                return PackageNameSanitizer.Sanitize(raw ?? "package").Value;
            }
            catch { return "package"; }
        }

        private void RefreshScope()
        {
            var scope = Sw2gzExportScopePlanner.Plan(_doc, _txtOutput.Text, _txtPkg.Text);
            _scopeHeader.Text    = scope.ModeLabel;
            _scopeCounts.Text    = "Links: " + scope.LinkCount + "   Joints: " + scope.JointCount;
            _scopeWorkspace.Text = "Target: " + scope.WorkspaceRoot;
            _scopeFiles.Items.Clear();
            foreach (var f in scope.Files) _scopeFiles.Items.Add(f);
        }

        // ─── Navigation ──────────────────────────────────────────────
        private void ShowPage(int i)
        {
            _currentPage = i;
            for (int k = 0; k < _pages.Length; k++) _pages[k].Visible = (k == i);
            _btnBack.Enabled = i > 0 && i < 2;   // Back disabled once Run page shown
            _btnNext.Text = (i == _pages.Length - 1) ? "Finish" : "Next >";
            // On Run page, Next becomes Close after pipeline finishes — guard
            // here in case it was already toggled during the run.
            _btnCancel.Visible = (i < _pages.Length - 1) || !_resultOk && string.IsNullOrEmpty(_resultWorkspace);

            if (i == 1) RefreshScope();
        }

        private void GoBack()
        {
            if (_currentPage > 0 && _currentPage < 2) ShowPage(_currentPage - 1);
        }

        private void GoNext()
        {
            if (_currentPage == 0)
            {
                if (!ValidateMeta()) return;
                ShowPage(1);
                return;
            }
            if (_currentPage == 1)
            {
                ShowPage(2);
                RunPipeline();
                return;
            }
            // Final page → Close
            Close();
        }

        private bool ValidateMeta()
        {
            if (string.IsNullOrWhiteSpace(_txtOutput.Text))
            { MessageBox.Show(this, "Pick an output folder."); return false; }
            if (string.IsNullOrWhiteSpace(_txtPkg.Text))
            { MessageBox.Show(this, "Pick a package name."); return false; }
            return true;
        }

        // ─── Pipeline ────────────────────────────────────────────────
        private async void RunPipeline()
        {
            _runStatus.Text = "Running export…";
            _runSpinner.Visible = true;
            _runDetail.Clear();
            _btnOpenFolder.Visible = _btnCopyCmd.Visible = _btnCopyLog.Visible = false;
            _btnBack.Enabled = false;
            _btnNext.Enabled = false;

            var meta = new ExportMetaInput
            {
                OutputFolder = _txtOutput.Text.Trim(),
                PackageName  = _txtPkg.Text.Trim(),
                Author       = _txtAuthor.Text.Trim(),
                Email        = _txtEmail.Text.Trim(),
                License      = _txtLicense.Text.Trim(),
            };
            // Persist user defaults so the next assembly's wizard pre-fills.
            Sw2gzUserDefaults.Save(new Sw2gzUserDefaults.Values
            {
                Author = meta.Author, Email = meta.Email,
                License = meta.License, LastOutputFolder = meta.OutputFolder,
            });

            string pkgSan = PackageNameSanitizer.Sanitize(meta.PackageName).Value;
            string ws = Path.Combine(meta.OutputFolder, pkgSan + "_ws");

            // SolidWorks COM calls must run on the UI thread, so the pipeline
            // runs synchronously on the message loop. Wrap in Task.Run only if
            // a future refactor makes the writers thread-safe; today, blocking
            // here freezes the dialog (UX-acceptable for the install-wizard
            // feel and matches main's ExportDialog behaviour).
            SW2GZ.Validate.ValidationReport report = null;
            string errMsg = null;
            try
            {
                var cfg = Sw2gzDocToExportConfig.Bridge(_doc, meta);
                // Yield once so the spinner gets a paint pass before the
                // synchronous pipeline blocks the UI thread.
                await Task.Yield();
                report = Sw2gzModelExporter.Run(_swApp, _modelDoc, cfg);
            }
            catch (Exception ex)
            {
                logger.Error("Export pipeline threw", ex);
                errMsg = ex.Message;
            }

            _runSpinner.Visible = false;
            if (errMsg != null)
            {
                ShowRunError("Export failed:\n" + errMsg);
                return;
            }
            if (report != null && report.HasErrors)
            {
                var lines = new List<string> { "Export finished with errors:" };
                lines.AddRange(report.Errors.Select(x => "  • " + x.Message));
                ShowRunError(string.Join(System.Environment.NewLine, lines));
                return;
            }

            // Success
            _resultOk = true;
            _resultWorkspace = ws;
            _resultLaunchCmd = "cd \"" + ws + "\" && colcon build && source install/setup.bash && " +
                "ros2 launch " + pkgSan + " gz_sim.launch.py";
            _runStatus.Text = "Export complete";
            _runDetail.Text =
                "Output workspace:" + System.Environment.NewLine +
                "  " + ws + System.Environment.NewLine + System.Environment.NewLine +
                "Build and launch (ROS 2 Jazzy + Gz Harmonic):" + System.Environment.NewLine +
                "  " + _resultLaunchCmd;
            _btnOpenFolder.Visible = _btnCopyCmd.Visible = true;
            _btnNext.Enabled = true;
            _btnNext.Text = "Close";
            _btnCancel.Visible = false;
        }

        private void ShowRunError(string message)
        {
            _resultOk = false;
            _runStatus.Text = "Export failed";
            _runDetail.Text = message;
            _btnCopyLog.Visible = true;
            _btnNext.Enabled = true;
            _btnNext.Text = "Close";
            _btnCancel.Visible = false;
        }

        // ─── Helpers ─────────────────────────────────────────────────
        private void BrowseOutput()
        {
            using (var fbd = new FolderBrowserDialog
            {
                Description = "Pick the output folder",
                SelectedPath = Directory.Exists(_txtOutput.Text)
                    ? _txtOutput.Text
                    : System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
            })
            {
                if (fbd.ShowDialog(this) == DialogResult.OK) _txtOutput.Text = fbd.SelectedPath;
            }
        }

        private void SafeOpenFolder()
        {
            try
            {
                if (!string.IsNullOrEmpty(_resultWorkspace) && Directory.Exists(_resultWorkspace))
                    Process.Start("explorer.exe", "\"" + _resultWorkspace + "\"");
            }
            catch (Exception e) { logger.Warn("Open folder failed", e); }
        }

        private void SafeClipboard(string text)
        {
            try { if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text); }
            catch (Exception e) { logger.Warn("Clipboard failed", e); }
        }

        private static string ModeText(Sw2gzMode m)
        {
            switch (m)
            {
                case Sw2gzMode.World: return "Gz world (SDF world)";
                case Sw2gzMode.Asset: return "Gz asset (SDF model)";
                default:              return "Robot package (URDF/Xacro)";
            }
        }
    }
}
#endif
