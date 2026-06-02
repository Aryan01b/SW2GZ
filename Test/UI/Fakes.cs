/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — hand-written fakes for the wizard service interfaces, used across the
view-model tests. Kept deliberately simple (no Moq) so the intent of each
test stays obvious.
*/
using System;
using System.Collections.Generic;
using SW2GZ.Build.Model;
using SW2GZ.Ros2;
using SW2GZ.UI.Services;

namespace SW2GZ.Test.UI
{
    /// Returns a preset folder; records the initialPath it was called with.
    internal sealed class FakeFolderBrowserService : IFolderBrowserService
    {
        private readonly string _result;
        public string LastInitialPath { get; private set; }
        public int CallCount { get; private set; }

        public FakeFolderBrowserService(string result) => _result = result;

        public string BrowseForFolder(string initialPath)
        {
            CallCount++;
            LastInitialPath = initialPath;
            return _result;
        }
    }

    /// Returns a fixed body-name selection.
    internal sealed class FakeViewportSelectionService : IViewportSelectionService
    {
        private IReadOnlyList<string> _names;

        public FakeViewportSelectionService(params string[] names) =>
            _names = names ?? Array.Empty<string>();

        public void SetSelection(params string[] names) =>
            _names = names ?? Array.Empty<string>();

        public IReadOnlyList<string> GetSelectedBodyNames() => _names;
        public int SelectedCount => _names.Count;
    }

    /// Captures the Run arguments and returns a preset ExportResult.
    internal sealed class FakeExportRunner : IExportRunner
    {
        private readonly ExportResult _result;
        public int CallCount { get; private set; }
        public RobotModel LastModel { get; private set; }
        public string LastOutputDir { get; private set; }
        public ExportMode LastMode { get; private set; }

        public FakeExportRunner(ExportResult result) => _result = result;

        public ExportResult Run(RobotModel model, string outputDir, ExportMode mode)
        {
            CallCount++;
            LastModel = model;
            LastOutputDir = outputDir;
            LastMode = mode;
            return _result;
        }
    }
}
