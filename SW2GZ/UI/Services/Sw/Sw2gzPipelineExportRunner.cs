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

            // Robot mode v2 — the robot export pipeline was removed for a clean
            // rebuild. This runner only ever drove the Robot Package path.
            throw new NotSupportedException(
                "Robot mode export is not implemented yet (removed for the v2 rebuild).");
        }
    }
}
