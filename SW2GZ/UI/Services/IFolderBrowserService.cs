/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — wizard service boundary. Abstracts the folder-picker dialog so the
OutputStep view-model is testable without WinForms. The net48 impl
(WinFormsFolderBrowserService) lives in UI\Services\Sw\ and is NOT
source-linked into the test project.
*/
namespace SW2GZ.UI.Services
{
    public interface IFolderBrowserService
    {
        /// Returns the chosen folder, or null if the user cancelled.
        string BrowseForFolder(string initialPath);
    }
}
