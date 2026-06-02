/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — no-op IFolderBrowserService for design-time + unit tests. Always
returns null (as if the user cancelled). Pure C#; source-linked into the
test project. The net48 impl is WinFormsFolderBrowserService.
*/
namespace SW2GZ.UI.Services
{
    public sealed class NullFolderBrowserService : IFolderBrowserService
    {
        public string BrowseForFolder(string initialPath) => null;
    }
}
