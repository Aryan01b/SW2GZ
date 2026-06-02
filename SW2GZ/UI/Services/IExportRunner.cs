/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — wizard service boundary. Abstracts Sw2gzPipeline so the Review step
view-model can trigger an export and report results without referencing COM.
The SW-side impl (Sw2gzPipelineExportRunner) constructs the pipeline behind
#if SW_INTEROP.
*/
using System.Collections.Generic;
using SW2GZ.Build.Model;
using SW2GZ.Ros2;

namespace SW2GZ.UI.Services
{
    /// Small value-type result returned to the Review step. ErrorCount mirrors
    /// the pipeline's ValidationReport error count; Messages carries a flat
    /// human-readable list for display.
    public sealed record ExportResult(bool Success, int ErrorCount, IReadOnlyList<string> Messages);

    public interface IExportRunner
    {
        ExportResult Run(RobotModel model, string outputDir, ExportMode mode);
    }
}
