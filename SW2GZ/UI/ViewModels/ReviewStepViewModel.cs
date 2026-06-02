/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — Step 5. Read-only pre-flight summary derived from the earlier steps,
plus the Finish-Export action. The summary strings (mode, target, package,
link/joint counts, geometry-assigned, validation errors) recompute from the
live step view-models. FinishExport delegates to IExportRunner and stashes
the ExportResult for display.

The VM does NOT assemble a RobotModel from COM data — the SW-side
IExportRunner owns that. ReviewStep receives an optional pre-built preview
RobotModel (built by the COM layer / a test) to hand to the runner; when
null, Finish is a no-op-safe guard (CanExport stays false).
*/
using System;
using SW2GZ.Build.Model;
using SW2GZ.Ros2;
using SW2GZ.UI.Mvvm;
using SW2GZ.UI.Services;

namespace SW2GZ.UI.ViewModels
{
    public sealed class ReviewStepViewModel : StepViewModelBase
    {
        private readonly ModeStepViewModel _mode;
        private readonly TargetsStepViewModel _targets;
        private readonly OutputStepViewModel _output;
        private readonly LinksStepViewModel _links;
        private readonly IExportRunner _exportRunner;
        private readonly int _jointCount;
        private RobotModel _previewModel;
        private ExportResult _lastResult;

        public ReviewStepViewModel(
            ModeStepViewModel mode,
            TargetsStepViewModel targets,
            OutputStepViewModel output,
            LinksStepViewModel links,
            IExportRunner exportRunner,
            int jointCount = 0,
            RobotModel previewModel = null)
            : base("Review & export", "Validate & finish")
        {
            _mode = mode ?? throw new ArgumentNullException(nameof(mode));
            _targets = targets ?? throw new ArgumentNullException(nameof(targets));
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _links = links ?? throw new ArgumentNullException(nameof(links));
            _exportRunner = exportRunner ?? new NullExportRunner();
            _jointCount = jointCount;
            _previewModel = previewModel;

            FinishExportCommand = new RelayCommand(FinishExport, () => CanExport);
        }

        public string ModeSummary => Describe(_mode.SelectedMode);
        public string TargetSummary => _targets.TargetSummary;
        public string PackageName => _output.SanitizedPackageName;
        public int LinkCount => _links.LinkCount;
        public int JointCount => _jointCount;
        public int AssignedGeometryCount => _links.AssignedGeometryCount;

        /// 0 until an export runs; thereafter mirrors the runner's report.
        public int ValidationErrorCount => _lastResult?.ErrorCount ?? 0;

        public ExportResult LastResult
        {
            get => _lastResult;
            private set
            {
                if (SetProperty(ref _lastResult, value))
                    OnPropertyChanged(nameof(ValidationErrorCount));
            }
        }

        /// Optional preview model handed to the runner. Settable so the COM
        /// layer can assemble it after the link tree is finalized.
        public RobotModel PreviewModel
        {
            get => _previewModel;
            set
            {
                if (SetProperty(ref _previewModel, value))
                {
                    OnPropertyChanged(nameof(CanExport));
                    FinishExportCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// Export is allowed once every link has geometry, the package name is
        /// valid, an output folder is set, and a preview model is available.
        public bool CanExport =>
            _previewModel != null &&
            _links.AllLinksHaveGeometry &&
            !string.IsNullOrWhiteSpace(_output.SanitizedPackageName) &&
            !string.IsNullOrWhiteSpace(_output.OutputFolder);

        public RelayCommand FinishExportCommand { get; }

        // Review is terminal — nothing to advance to.
        public override bool CanAdvance() => false;

        private void FinishExport()
        {
            if (!CanExport)
                return;

            LastResult = _exportRunner.Run(_previewModel, _output.OutputFolder, _mode.SelectedMode);
            IsComplete = LastResult != null && LastResult.Success;
        }

        private static string Describe(ExportMode mode)
        {
            switch (mode)
            {
                case ExportMode.RobotPackage: return "Robot Package";
                case ExportMode.SdfModel: return "SDF Model";
                case ExportMode.SdfWorld: return "SDF World";
                default: return mode.ToString();
            }
        }
    }
}
