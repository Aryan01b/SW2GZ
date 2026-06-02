/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — Step 1. Picks the export mode (Robot Package / SDF Model / SDF World).
Always advanceable: every mode is valid, so CanAdvance() is unconditionally
true.
*/
using SW2GZ.Ros2;

namespace SW2GZ.UI.ViewModels
{
    public sealed class ModeStepViewModel : StepViewModelBase
    {
        private ExportMode _selectedMode = ExportMode.RobotPackage;

        public ModeStepViewModel() : base("Mode", "What to generate") { }

        public ExportMode SelectedMode
        {
            get => _selectedMode;
            set => SetProperty(ref _selectedMode, value);
        }

        public override bool CanAdvance() => true;
    }
}
