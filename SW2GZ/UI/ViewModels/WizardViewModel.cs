/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — the wizard shell view-model. Owns the five step VMs in order, tracks
the current step, and drives Back/Next with the per-step CanAdvance() gate.
Maintains each step's IsActive/IsComplete flags for the rail, plus Progress
(0..1) and StepCounter ("Step 2 of 5") for the footer.

Constructor takes the service interfaces (DI). A convenience ctor wires the
Null* services so the wizard is constructable for design-time + unit tests.
*/
using System;
using System.Collections.Generic;
using SW2GZ.Build.Model;
using SW2GZ.UI.Mvvm;
using SW2GZ.UI.Services;

namespace SW2GZ.UI.ViewModels
{
    public sealed class WizardViewModel : ObservableObject
    {
        private readonly IReadOnlyList<StepViewModelBase> _steps;
        private int _currentStepIndex;

        public WizardViewModel(
            IFolderBrowserService folderBrowser,
            IViewportSelectionService viewportSelection,
            IThemeService themeService,
            IExportRunner exportRunner,
            IReadOnlyList<LinkDto> links = null,
            int jointCount = 0,
            RobotModel previewModel = null,
            IReadOnlyList<JointDto> joints = null)
        {
            folderBrowser ??= new NullFolderBrowserService();
            viewportSelection ??= new NullViewportSelectionService();
            ThemeService = themeService ?? new NullThemeService();
            exportRunner ??= new NullExportRunner();

            // Link names are reused by the Materials + Sensors steps for their
            // per-link rows / attachment combos.
            var linkNames = new List<string>();
            if (links != null)
                foreach (LinkDto l in links)
                    linkNames.Add(l.Name);

            // jointCount stays for back-compat callers; if a joint DTO list is
            // supplied it is the source of truth for the Joints step + count.
            int effectiveJointCount = joints != null ? joints.Count : jointCount;

            ModeStep = new ModeStepViewModel();
            TargetsStep = new TargetsStepViewModel();
            OutputStep = new OutputStepViewModel(folderBrowser);
            LinksStep = new LinksStepViewModel(links, viewportSelection);
            JointsStep = new JointsStepViewModel(joints);
            CollisionStep = new CollisionStepViewModel();
            MaterialsStep = new MaterialsStepViewModel(linkNames);
            SensorsStep = new SensorsStepViewModel(linkNames);
            ControllersStep = new ControllersStepViewModel();
            ReviewStep = new ReviewStepViewModel(
                ModeStep, TargetsStep, OutputStep, LinksStep, exportRunner,
                effectiveJointCount, previewModel,
                JointsStep, CollisionStep, MaterialsStep, SensorsStep, ControllersStep);

            _steps = new StepViewModelBase[]
            {
                ModeStep, TargetsStep, OutputStep, LinksStep,
                JointsStep, CollisionStep, MaterialsStep, SensorsStep, ControllersStep,
                ReviewStep,
            };

            NextCommand = new RelayCommand(MoveNext, CanMoveNext);
            BackCommand = new RelayCommand(MoveBack, CanMoveBack);

            // Re-evaluate Next whenever any step's advanceability changes.
            foreach (StepViewModelBase step in _steps)
                step.AdvanceabilityChanged += (s, e) => NextCommand.RaiseCanExecuteChanged();

            UpdateStepStates();
        }

        /// Convenience ctor — all Null* services, no link data. For design-time
        /// + unit tests that only exercise navigation.
        public WizardViewModel()
            : this(new NullFolderBrowserService(), new NullViewportSelectionService(),
                   new NullThemeService(), new NullExportRunner())
        {
        }

        public ModeStepViewModel ModeStep { get; }
        public TargetsStepViewModel TargetsStep { get; }
        public OutputStepViewModel OutputStep { get; }
        public LinksStepViewModel LinksStep { get; }
        public JointsStepViewModel JointsStep { get; }
        public CollisionStepViewModel CollisionStep { get; }
        public MaterialsStepViewModel MaterialsStep { get; }
        public SensorsStepViewModel SensorsStep { get; }
        public ControllersStepViewModel ControllersStep { get; }
        public ReviewStepViewModel ReviewStep { get; }
        public IThemeService ThemeService { get; }

        public IReadOnlyList<StepViewModelBase> Steps => _steps;

        public int StepCount => _steps.Count;

        public int CurrentStepIndex
        {
            get => _currentStepIndex;
            private set
            {
                if (SetProperty(ref _currentStepIndex, value))
                {
                    OnPropertyChanged(nameof(CurrentStep));
                    OnPropertyChanged(nameof(Progress));
                    OnPropertyChanged(nameof(StepCounter));
                    OnPropertyChanged(nameof(IsFirstStep));
                    OnPropertyChanged(nameof(IsLastStep));
                    UpdateStepStates();
                    NextCommand.RaiseCanExecuteChanged();
                    BackCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public StepViewModelBase CurrentStep => _steps[_currentStepIndex];

        public bool IsFirstStep => _currentStepIndex == 0;
        public bool IsLastStep => _currentStepIndex == _steps.Count - 1;

        /// 0..1 — fraction of steps reached (step N of M => N/M).
        public double Progress => (double)(_currentStepIndex + 1) / _steps.Count;

        public string StepCounter => $"Step {_currentStepIndex + 1} of {_steps.Count}";

        public RelayCommand NextCommand { get; }
        public RelayCommand BackCommand { get; }

        private bool CanMoveNext() => !IsLastStep && CurrentStep.CanAdvance();

        private bool CanMoveBack() => !IsFirstStep;

        private void MoveNext()
        {
            if (!CanMoveNext())
                return;
            CurrentStepIndex = _currentStepIndex + 1;
        }

        private void MoveBack()
        {
            if (!CanMoveBack())
                return;
            CurrentStepIndex = _currentStepIndex - 1;
        }

        // Active = current step; Complete = any step before the current one.
        private void UpdateStepStates()
        {
            for (int i = 0; i < _steps.Count; i++)
            {
                _steps[i].IsActive = i == _currentStepIndex;
                _steps[i].IsComplete = i < _currentStepIndex;
            }
        }
    }
}
