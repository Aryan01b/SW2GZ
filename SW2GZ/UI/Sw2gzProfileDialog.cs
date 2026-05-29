/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Phase 5 SW2GZ Profile prompt. Pops up before final export so the user can
pick the export mode (Robot Package / SDF Model / SDF World) and identity
metadata. Distro/Gz combo boxes removed — v2.0 is locked to Jazzy + Harmonic.
Programmatic WinForms — no .Designer.cs required.
*/
using System;
using System.Drawing;
using System.Windows.Forms;
using SW2GZ.Ros2;
using SW2GZ.URDFExport;

namespace SW2GZ.UI
{
    internal sealed class Sw2gzProfileDialog : Form
    {
        private readonly RadioButton rbRobotPackage;
        private readonly RadioButton rbSdfModel;
        private readonly RadioButton rbSdfWorld;
        private readonly TextBox txtAuthor;
        private readonly TextBox txtEmail;
        private readonly TextBox txtLicense;

        public TargetProfile Profile { get; private set; }
        public string Author { get; private set; }
        public string Email { get; private set; }
        public string License { get; private set; }

        public Sw2gzProfileDialog(TargetProfile initial, string author, string email, string license)
        {
            if (initial == null) initial = new TargetProfile { Mode = ExportMode.RobotPackage };

            Text = "SW2GZ — Export Profile";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(420, 290);

            var grpMode = new GroupBox { Text = "Export Target", Location = new Point(12, 12), Size = new Size(396, 100) };
            rbRobotPackage = new RadioButton { Text = "Robot Package (URDF + xacro)", Location = new Point(12, 22), AutoSize = true, Checked = initial.Mode == ExportMode.RobotPackage };
            rbSdfModel = new RadioButton { Text = "SDF Model (asset)", Location = new Point(12, 45), AutoSize = true, Checked = initial.Mode == ExportMode.SdfModel };
            rbSdfWorld = new RadioButton { Text = "SDF World", Location = new Point(12, 68), AutoSize = true, Checked = initial.Mode == ExportMode.SdfWorld };
            grpMode.Controls.Add(rbRobotPackage);
            grpMode.Controls.Add(rbSdfModel);
            grpMode.Controls.Add(rbSdfWorld);

            var lblAuthor = new Label { Text = "Author:", Location = new Point(12, 125), AutoSize = true };
            txtAuthor = new TextBox { Location = new Point(110, 122), Size = new Size(280, 20), Text = string.IsNullOrWhiteSpace(author) ? Environment.UserName : author };

            var lblEmail = new Label { Text = "Email:", Location = new Point(12, 155), AutoSize = true };
            txtEmail = new TextBox { Location = new Point(110, 152), Size = new Size(280, 20), Text = string.IsNullOrWhiteSpace(email) ? "TODO@example.com" : email };

            var lblLicense = new Label { Text = "License:", Location = new Point(12, 185), AutoSize = true };
            txtLicense = new TextBox { Location = new Point(110, 182), Size = new Size(280, 20), Text = string.IsNullOrWhiteSpace(license) ? "Apache-2.0" : license };

            var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(232, 240), Size = new Size(80, 28) };
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(322, 240), Size = new Size(80, 28) };
            AcceptButton = btnOk;
            CancelButton = btnCancel;
            btnOk.Click += (s, e) => Commit();

            Controls.Add(grpMode);
            Controls.Add(lblAuthor); Controls.Add(txtAuthor);
            Controls.Add(lblEmail); Controls.Add(txtEmail);
            Controls.Add(lblLicense); Controls.Add(txtLicense);
            Controls.Add(btnOk); Controls.Add(btnCancel);
        }

        private void Commit()
        {
            ExportMode mode = ExportMode.RobotPackage;
            if (rbSdfModel.Checked) mode = ExportMode.SdfModel;
            else if (rbSdfWorld.Checked) mode = ExportMode.SdfWorld;

            Profile = new TargetProfile { Mode = mode };
            Author = string.IsNullOrWhiteSpace(txtAuthor.Text) ? Environment.UserName : txtAuthor.Text.Trim();
            Email = string.IsNullOrWhiteSpace(txtEmail.Text) ? "TODO@example.com" : txtEmail.Text.Trim();
            License = string.IsNullOrWhiteSpace(txtLicense.Text) ? "Apache-2.0" : txtLicense.Text.Trim();
        }
    }
}
