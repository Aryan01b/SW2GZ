/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Sw2gzModeChangeOverlay — short-lived borderless WinForms toast shown over
SolidWorks while Sw2gzRibbonRegistrar.RefreshTabForMode rebuilds the SW2GZ
ribbon boxes.

Without this, the user clicks a mode pill and SW briefly flashes — boxes
removed, then re-added — which can read as the active tab "moving". A modal
toast lasting one message-loop tick covers the flash with explicit text:
    "Changing mode from <from> to <to> …"

Modal-with-auto-dismiss: ShowDialog blocks the caller; a one-shot Timer
queues a Close() onto the next idle tick AFTER the refresh action runs on
the toast's Shown event. Net result: caller flow is

    var overlay = new Sw2gzModeChangeOverlay(from, to, refreshAction);
    overlay.ShowDialog();   // refreshAction runs while shown, then closes
*/
#if SW_INTEROP
using System;
using System.Drawing;
using System.Windows.Forms;

namespace SW2GZ.UI.Ribbon
{
    internal sealed class Sw2gzModeChangeOverlay : Form
    {
        private readonly Action _refresh;

        public Sw2gzModeChangeOverlay(
            SW2GZ.URDFExport.Sw2gzMode from,
            SW2GZ.URDFExport.Sw2gzMode to,
            Action refresh)
        {
            _refresh = refresh ?? (() => { });

            FormBorderStyle = FormBorderStyle.None;
            StartPosition   = FormStartPosition.CenterParent;
            BackColor       = Color.FromArgb(40, 40, 40);
            ForeColor       = Color.White;
            Size            = new Size(360, 110);
            ShowInTaskbar   = false;
            TopMost         = true;
            ControlBox      = false;
            MinimizeBox     = false;
            MaximizeBox     = false;

            var title = new Label
            {
                Text      = "Changing mode",
                AutoSize  = false,
                Dock      = DockStyle.Top,
                Height    = 36,
                Font      = new Font(SystemFonts.MessageBoxFont.FontFamily, 11, FontStyle.Bold),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
            };
            var subtitle = new Label
            {
                Text      = from + "  →  " + to,
                AutoSize  = false,
                Dock      = DockStyle.Top,
                Height    = 36,
                Font      = new Font(SystemFonts.MessageBoxFont.FontFamily, 10, FontStyle.Regular),
                ForeColor = Color.LightGray,
                TextAlign = ContentAlignment.MiddleCenter,
            };
            var hint = new Label
            {
                Text      = "Refreshing the SW2GZ ribbon …",
                AutoSize  = false,
                Dock      = DockStyle.Top,
                Height    = 24,
                Font      = new Font(SystemFonts.MessageBoxFont.FontFamily, 8, FontStyle.Italic),
                ForeColor = Color.DimGray,
                TextAlign = ContentAlignment.MiddleCenter,
            };
            Controls.Add(hint);
            Controls.Add(subtitle);
            Controls.Add(title);

            Shown += OnShown;
        }

        private void OnShown(object sender, EventArgs e)
        {
            try { _refresh(); }
            catch { /* logged by caller */ }

            // Close on the next idle tick. A direct Close() here would race the
            // paint pass and leave the overlay frame visible after the refresh.
            // 250ms gives SW time to redraw the new ribbon underneath before the
            // overlay vanishes — visually smooth.
            var timer = new Timer { Interval = 250 };
            timer.Tick += (s, a) =>
            {
                timer.Stop();
                timer.Dispose();
                try { Close(); } catch { }
            };
            timer.Start();
        }
    }
}
#endif
