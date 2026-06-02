/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — net48 IFolderBrowserService backed by WinForms FolderBrowserDialog.
References System.Windows.Forms, so this file is compiled ONLY into
SW2GZ.csproj (net48) and is NOT source-linked into the net8 test project.

Write-only deliverable: not unit-tested here. The OutputStep VM is tested
against the pure NullFolderBrowserService / FakeFolderBrowserService instead.
*/
using System.Windows.Forms;
using SW2GZ.UI.Services;

namespace SW2GZ.UI.Services.Sw
{
    public sealed class WinFormsFolderBrowserService : IFolderBrowserService
    {
        public string BrowseForFolder(string initialPath)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Select the output folder for the exported package";
                dlg.ShowNewFolderButton = true;
                if (!string.IsNullOrEmpty(initialPath))
                    dlg.SelectedPath = initialPath;

                return dlg.ShowDialog() == DialogResult.OK ? dlg.SelectedPath : null;
            }
        }
    }
}
