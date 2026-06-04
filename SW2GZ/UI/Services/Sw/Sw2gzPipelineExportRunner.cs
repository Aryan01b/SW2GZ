/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — IExportRunner that drives Sw2gzPipeline. The pipeline walks the live
SolidWorks assembly itself (it owns the SW boundary services), so the actual
Run call needs the COM handles and is guarded by #if SW_INTEROP. The runner
takes the package metadata from the wizard-built RobotModel and converts the
pipeline's ValidationReport into the VM-friendly ExportResult.

Compiled only into SW2GZ.csproj (net48); NOT source-linked into the test
project. The Review VM is tested against NullExportRunner / FakeExportRunner.

TODO P8-COM: when v2.1 lands joint/sensor extraction from the model, route
them into the pipeline overload instead of relying on its internal walk.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using SW2GZ.Build.Model;
using SW2GZ.Ros2;
using SW2GZ.UI.Services;

#if SW_INTEROP
using SW2GZ.SwSurface;
using SW2GZ.SwSurface.Abstractions;
using SW2GZ.URDFExport;
#endif

namespace SW2GZ.UI.Services.Sw
{
    public sealed class Sw2gzPipelineExportRunner : IExportRunner
    {
#if SW_INTEROP
        private readonly IMassProperties _mass;
        private readonly IAssemblyWalker _walker;
        private readonly IMeshTessellator _tess;
        private readonly IAppearanceSource _appearances;

        // Real ctor — takes the four SW boundary services the pipeline needs.
        public Sw2gzPipelineExportRunner(
            IMassProperties mass, IAssemblyWalker walker,
            IMeshTessellator tess, IAppearanceSource appearances)
        {
            _mass = mass ?? throw new ArgumentNullException(nameof(mass));
            _walker = walker ?? throw new ArgumentNullException(nameof(walker));
            _tess = tess ?? throw new ArgumentNullException(nameof(tess));
            _appearances = appearances ?? throw new ArgumentNullException(nameof(appearances));
        }
#endif

        // Skeleton ctor — present so the type can be referenced when SW handles
        // aren't available (e.g. early shell wiring). Run() throws until the
        // real ctor seeds the SW services.
        public Sw2gzPipelineExportRunner() { }

        public ExportResult Run(RobotModel model, string outputDir, ExportMode mode)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (string.IsNullOrWhiteSpace(outputDir))
                throw new ArgumentException("Output directory is required.", nameof(outputDir));

#if SW_INTEROP
            if (_mass == null || _walker == null || _tess == null || _appearances == null)
            {
                throw new InvalidOperationException(
                    "Sw2gzPipelineExportRunner requires SW boundary services — use the 4-arg ctor.");
            }

            RobotMeta meta = model.Meta;
            var pipeline = new Sw2gzPipeline(_mass, _walker, _tess, _appearances);
            // Thread the wizard's selected ExportMode into the pipeline so the
            // chosen artifact (Robot Package / gz asset / gz world) is emitted.
            // StackProfile.Default() = full stack for Robot Package; gz modes
            // ignore the actuation backend.
            SW2GZ.Validate.ValidationReport report = pipeline.Run(
                outputDir, meta.PackageName, meta.Author, meta.Email, meta.License,
                model.Sensors, StackProfile.Default(), mode, meta.Frame);

            var messages = report.Issues.Select(i => i.Message).ToList();
            return new ExportResult(!report.HasErrors, report.Errors.Count(), messages);
#else
            throw new NotImplementedException(
                "Sw2gzPipelineExportRunner.Run() requires the SolidWorks COM build (SW_INTEROP).");
#endif
        }
    }
}
