/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — no-op IExportRunner for design-time + unit tests. Reports a successful,
zero-error export without writing anything. Pure C#; source-linked into the
test project. The real SW-side runner is Sw2gzPipelineExportRunner.
*/
using System;
using SW2GZ.Build.Model;
using SW2GZ.Ros2;

namespace SW2GZ.UI.Services
{
    public sealed class NullExportRunner : IExportRunner
    {
        public ExportResult Run(RobotModel model, string outputDir, ExportMode mode) =>
            new ExportResult(true, 0, Array.Empty<string>());
    }
}
