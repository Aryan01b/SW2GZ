/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Sw2gzExportWizardForm — modal centered installer-style wizard launched from
the SW2GZ ribbon Export button. Three pages: Meta → Scope → Run (spinner →
result panel). Layout uses TableLayoutPanel-style absolute placement with
consistent 16 px gutters; theme follows the user's Windows light/dark
preference via Sw2gzTheme.

Code-only (no Designer). Pages are Panels owned by the form and swapped via
Visible toggling.
*/
#if SW_INTEROP
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SW2GZ.Build;
using SW2GZ.URDFExport;
using SW2GZ.UI;
using SW2GZ.Utilities;

namespace SW2GZ.UI.Forms
{
    public sealed class Sw2gzExportWizardForm : Form
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        // ─── Layout constants ────────────────────────────────────────
        // Field row = label at y, input at y + LabelLift, next label at
        // y + RowH. Hard-coded numbers (not derived from LabelLift +
        // InputH + extra) so a tweak to one doesn't accidentally collapse
        // the row gap — that's the bug an earlier "y += LabelLift +
        // InputH + FieldSpacing - LabelLift" expression hid.
        private const int FormW       = 640;
        private const int Gutter      = 20;
        private const int LabelLift   = 22;   // label top → input top
        private const int InputH      = 26;   // textbox height
        private const int RowGap      = 16;   // input bottom → next label top
        private const int RowH        = LabelLift + InputH + RowGap;   // 64 px
        private const int SectionGap  = 18;
        private const int PageH       = 460;
        private const int NavH        = 56;

        private readonly SldWorks _swApp;
        private readonly ModelDoc2 _modelDoc;
        private readonly Sw2gzDoc _doc;

        // ─── Pages ───────────────────────────────────────────────────
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

        // ─── Footer ──────────────────────────────────────────────────
        private Button _btnBack;
        private Button _btnNext;
        private Button _btnCancel;

        public Sw2gzExportWizardForm(SldWorks swApp, ModelDoc2 modelDoc, Sw2gzDoc doc)
        {
            _swApp    = swApp    ?? throw new ArgumentNullException(nameof(swApp));
            _modelDoc = modelDoc ?? throw new ArgumentNullException(nameof(modelDoc));
            _doc      = doc      ?? throw new ArgumentNullException(nameof(doc));

            Text            = "SW2GZ — Export";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition   = FormStartPosition.CenterScreen;
            MaximizeBox     = false;
            MinimizeBox     = false;
            ShowInTaskbar   = false;
            ClientSize      = new Size(FormW, PageH + NavH);
            // PageH bumped to 460 so all four meta rows + headline fit
            // without the last License row clipping into the nav strip.
            Font            = new Font("Segoe UI", 9F);

            BuildPages();
            BuildNav();
            ApplyDefaults();
            ApplyTheme();
            ShowPage(0);
        }

        // System theme can change while open — re-apply on WM_SETTINGCHANGE.
        protected override void WndProc(ref Message m)
        {
            const int WM_SETTINGCHANGE = 0x001A;
            base.WndProc(ref m);
            if (m.Msg == WM_SETTINGCHANGE) ApplyTheme();
        }

        private void ApplyTheme() => Sw2gzTheme.Apply(this, Sw2gzTheme.Current());

        // ─── Page build ──────────────────────────────────────────────
        private void BuildPages()
        {
            _pageMeta  = BuildMetaPage();
            _pageScope = BuildScopePage();
            _pageRun   = BuildRunPage();
            _pages = new[] { _pageMeta, _pageScope, _pageRun };
            foreach (var p in _pages)
            {
                p.Location = new Point(0, 0);
                p.Size     = new Size(FormW, PageH);
                Controls.Add(p);
            }
        }

        private Panel BuildMetaPage()
        {
            var p = new Panel { Padding = new Padding(Gutter) };
            int contentW = FormW - 2 * Gutter;

            int y = Gutter;
            _modeLabel = AddLabel(p, "Mode: " + ModeText(_doc.Mode), Gutter, y,
                new Font("Segoe UI Semibold", 12F, FontStyle.Bold));
            y += 34;

            // Sub-headline divider.
            var rule = new Panel
            {
                Location = new Point(Gutter, y),
                Size = new Size(contentW, 1),
                Tag = "rule",
            };
            p.Controls.Add(rule);
            y += SectionGap;

            // Output folder + Browse
            AddLabel(p, "Output folder", Gutter, y, subtle: true);
            int browseW = 90;
            _txtOutput = NewTextBox(Gutter, y + LabelLift, contentW - browseW - 10);
            var browse = new Button
            {
                Text = "Browse…",
                Location = new Point(Gutter + contentW - browseW, y + LabelLift - 1),
                Width = browseW, Height = InputH + 2,
            };
            browse.Click += (s, e) => BrowseOutput();
            p.Controls.Add(_txtOutput);
            p.Controls.Add(browse);
            y += RowH;

            // Package name
            AddLabel(p, "Package name", Gutter, y, subtle: true);
            _txtPkg = NewTextBox(Gutter, y + LabelLift, contentW);
            p.Controls.Add(_txtPkg);
            y += RowH;

            // Author / Email — true 50/50 split with consistent gutter.
            int half = (contentW - 16) / 2;
            AddLabel(p, "Author", Gutter, y, subtle: true);
            AddLabel(p, "Email", Gutter + half + 16, y, subtle: true);
            _txtAuthor = NewTextBox(Gutter, y + LabelLift, half);
            _txtEmail  = NewTextBox(Gutter + half + 16, y + LabelLift, half);
            p.Controls.Add(_txtAuthor);
            p.Controls.Add(_txtEmail);
            y += RowH;

            // License
            AddLabel(p, "License", Gutter, y, subtle: true);
            _txtLicense = NewTextBox(Gutter, y + LabelLift, contentW);
            p.Controls.Add(_txtLicense);

            return p;
        }

        private Panel BuildScopePage()
        {
            var p = new Panel { Padding = new Padding(Gutter) };
            int contentW = FormW - 2 * Gutter;
            int y = Gutter;

            _scopeHeader = AddLabel(p, "", Gutter, y,
                new Font("Segoe UI Semibold", 12F, FontStyle.Bold));
            y += 34;

            _scopeCounts    = AddLabel(p, "", Gutter, y);
            y += 24;
            _scopeWorkspace = AddLabel(p, "", Gutter, y, subtle: true);
            y += 30;

            AddLabel(p, "Files that will be written", Gutter, y, subtle: true);
            y += 26;   // explicit gap below this section label, > label height
            _scopeFiles = new ListBox
            {
                Location = new Point(Gutter, y),
                Size     = new Size(contentW, PageH - y - Gutter),
                Font     = new Font("Consolas", 9F),
                IntegralHeight = false,
                BorderStyle = BorderStyle.FixedSingle,
            };
            p.Controls.Add(_scopeFiles);
            return p;
        }

        private Panel BuildRunPage()
        {
            var p = new Panel { Padding = new Padding(Gutter) };
            int contentW = FormW - 2 * Gutter;
            int y = Gutter;

            _runStatus = AddLabel(p, "", Gutter, y,
                new Font("Segoe UI Semibold", 12F, FontStyle.Bold));
            y += 32;

            _runSpinner = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                Location = new Point(Gutter, y),
                Size = new Size(contentW, 8),
                MarqueeAnimationSpeed = 28,
                Visible = false,
            };
            p.Controls.Add(_runSpinner);
            y += 24;

            _runDetail = new TextBox
            {
                Location = new Point(Gutter, y),
                Size = new Size(contentW, PageH - y - Gutter - 48),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9F),
                BorderStyle = BorderStyle.FixedSingle,
            };
            p.Controls.Add(_runDetail);

            int actionY = PageH - Gutter - 36;
            _btnOpenFolder = new Button
            {
                Text = "Open output folder",
                Location = new Point(Gutter, actionY),
                Size = new Size(170, 32),
                Visible = false,
            };
            _btnOpenFolder.Click += (s, e) => SafeOpenFolder();
            _btnCopyCmd = new Button
            {
                Text = "Copy launch command",
                Location = new Point(Gutter + 180, actionY),
                Size = new Size(190, 32),
                Visible = false,
            };
            _btnCopyCmd.Click += (s, e) => SafeClipboard(_resultLaunchCmd);
            _btnCopyLog = new Button
            {
                Text = "Copy error log",
                Location = new Point(Gutter, actionY),
                Size = new Size(170, 32),
                Visible = false,
            };
            _btnCopyLog.Click += (s, e) => SafeClipboard(_runDetail.Text);
            p.Controls.Add(_btnOpenFolder);
            p.Controls.Add(_btnCopyCmd);
            p.Controls.Add(_btnCopyLog);
            return p;
        }

        private TextBox NewTextBox(int x, int y, int w) => new TextBox
        {
            Location = new Point(x, y),
            Size = new Size(w, InputH),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9F),
        };

        private Label AddLabel(Panel p, string text, int x, int y, Font font = null, bool subtle = false)
        {
            var l = new Label
            {
                Text = text,
                AutoSize = true,
                Location = new Point(x, y),
                Font = font ?? new Font("Segoe UI", 9F),
                BackColor = Color.Transparent,
                Tag = subtle ? "subtle" : null,
            };
            p.Controls.Add(l);
            return l;
        }

        private void BuildNav()
        {
            var nav = new Panel
            {
                Name = "nav",
                Location = new Point(0, PageH),
                Size = new Size(FormW, NavH),
            };

            int btnW = 90, btnH = 32, gap = 8, rightPad = Gutter;
            int y = (NavH - btnH) / 2;

            _btnCancel = new Button { Text = "Cancel", Size = new Size(btnW, btnH) };
            _btnNext   = new Button { Text = "Next >", Size = new Size(btnW, btnH) };
            _btnBack   = new Button { Text = "< Back", Size = new Size(btnW, btnH) };

            _btnCancel.Location = new Point(FormW - rightPad - btnW, y);
            _btnNext.Location   = new Point(FormW - rightPad - 2 * btnW - gap, y);
            _btnBack.Location   = new Point(FormW - rightPad - 3 * btnW - 2 * gap, y);

            _btnBack.Click   += (s, e) => GoBack();
            _btnNext.Click   += (s, e) => GoNext();
            _btnCancel.Click += (s, e) => Close();

            // Top border separator.
            var topRule = new Panel
            {
                Dock = DockStyle.Top, Height = 1, Tag = "rule",
            };

            nav.Controls.Add(_btnBack);
            nav.Controls.Add(_btnNext);
            nav.Controls.Add(_btnCancel);
            nav.Controls.Add(topRule);
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
            _scopeCounts.Text    = "Links: " + scope.LinkCount + "       Joints: " + scope.JointCount;
            _scopeWorkspace.Text = "Target: " + scope.WorkspaceRoot;
            _scopeFiles.Items.Clear();
            foreach (var f in scope.Files) _scopeFiles.Items.Add(f);
        }

        // ─── Navigation ──────────────────────────────────────────────
        private void ShowPage(int i)
        {
            _currentPage = i;
            for (int k = 0; k < _pages.Length; k++) _pages[k].Visible = (k == i);
            _btnBack.Enabled = i > 0 && i < 2;
            _btnNext.Text = (i == _pages.Length - 1) ? "Finish" : "Next >";
            _btnCancel.Visible = i < _pages.Length - 1
                || (!_resultOk && string.IsNullOrEmpty(_resultWorkspace));
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
            Sw2gzUserDefaults.Save(new Sw2gzUserDefaults.Values
            {
                Author = meta.Author, Email = meta.Email,
                License = meta.License, LastOutputFolder = meta.OutputFolder,
            });

            string pkgSan = PackageNameSanitizer.Sanitize(meta.PackageName).Value;

            SW2GZ.Validate.ValidationReport report = null;
            string errMsg = null;
            try
            {
                var cfg = Sw2gzDocToExportConfig.Bridge(_doc, meta);
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

            _resultOk = true;
            // Run instructions depend on the mode's output layout: Robot emits an
            // ament workspace (<pkg>_ws, colcon build); World/Asset emit a
            // self-contained <pkg>/ folder that runs with no build.
            string nl = System.Environment.NewLine;
            Sw2gzMode mode = _doc?.Mode ?? Sw2gzMode.Robot;
            if (mode == Sw2gzMode.World)
            {
                string dir = Path.Combine(meta.OutputFolder, pkgSan);
                _resultWorkspace = dir;
                _resultLaunchCmd = "ros2 launch \"" + Path.Combine(dir, "launch_world.py") + "\"";
                _runDetail.Text =
                    "Output folder (self-contained, no build):" + nl + "  " + dir + nl + nl +
                    "Run (ROS 2 Jazzy + Gz Harmonic):" + nl + "  " + _resultLaunchCmd + nl + nl +
                    "Or open the world directly in Gz:" + nl +
                    "  gz sim \"" + Path.Combine(dir, pkgSan + ".sdf") + "\"";
            }
            else if (mode == Sw2gzMode.Asset)
            {
                string dir = Path.Combine(meta.OutputFolder, pkgSan);
                _resultWorkspace = dir;
                _resultLaunchCmd = "export GZ_SIM_RESOURCE_PATH=\"" + meta.OutputFolder + "\"";
                _runDetail.Text =
                    "Output model (drop-in Gz model):" + nl + "  " + dir + nl + nl +
                    "Use it: point Gz at the parent folder, then include the model:" + nl +
                    "  " + _resultLaunchCmd + nl +
                    "  <include><uri>model://" + pkgSan + "</uri></include>";
            }
            else // Robot — ament workspace
            {
                string ws = Path.Combine(meta.OutputFolder, pkgSan + "_ws");
                _resultWorkspace = ws;
                _resultLaunchCmd = "cd \"" + ws + "\" && colcon build && source install/setup.bash && " +
                    "ros2 launch " + pkgSan + " gz_sim.launch.py";
                _runDetail.Text =
                    "Output workspace:" + nl + "  " + ws + nl + nl +
                    "Build and launch (ROS 2 Jazzy + Gz Harmonic):" + nl + "  " + _resultLaunchCmd;
            }
            _runStatus.Text = "Export complete";
            _btnOpenFolder.Visible = _btnCopyCmd.Visible = true;
            _btnNext.Enabled = true;
            _btnNext.Text = "Close";
            _btnCancel.Visible = false;

            // Re-apply theme so any newly-visible buttons pick up the palette.
            ApplyTheme();
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
            ApplyTheme();
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
