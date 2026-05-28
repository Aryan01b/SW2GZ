/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Phase 5 SW2GZ bridge: invoked from the Finish-Export button right before
ExportHelper.ExportRobot. Pops the Sw2gzProfileDialog so the user can
choose Mode (Robot Package / SDF Model / SDF World) + ROS 2 distro +
Gz version + author/email/license, then writes the result into the
ExportHelper that drives Ros2Package / SdfModelWriter / SdfWorldWriter.

Returns false if the user cancels, so the caller can short-circuit.
*/
using System.Windows.Forms;
using SW2GZ.URDFExport;

namespace SW2GZ.UI
{
    public partial class AssemblyExportForm : Form
    {
        internal bool PromptAndApplyProfile(ExportHelper exporter)
        {
            using (var dlg = new Sw2gzProfileDialog(exporter.Profile, exporter.Profile_Author, exporter.Profile_Email, exporter.Profile_License))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                exporter.Profile = dlg.Profile;
                exporter.Profile_Author = dlg.Author;
                exporter.Profile_Email = dlg.Email;
                exporter.Profile_License = dlg.License;
                return true;
            }
        }
    }
}
