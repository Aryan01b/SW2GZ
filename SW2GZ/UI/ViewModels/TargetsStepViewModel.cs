/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — Step 2. ROS 2 distro selector with auto-paired (read-only) Gz version.
v2.1 only ships Jazzy + Harmonic, so IsSupported is true for Jazzy alone;
every other distro shows SupportNote and blocks Next (CanAdvance == IsSupported).
*/
namespace SW2GZ.UI.ViewModels
{
    public sealed class TargetsStepViewModel : StepViewModelBase
    {
        private Ros2Distro _selectedDistro = Ros2Distro.Jazzy;

        public TargetsStepViewModel() : base("Targets", "ROS distro & Gz") { }

        public Ros2Distro SelectedDistro
        {
            get => _selectedDistro;
            set
            {
                if (SetProperty(ref _selectedDistro, value))
                {
                    OnPropertyChanged(nameof(GzVersion));
                    OnPropertyChanged(nameof(IsSupported));
                    OnPropertyChanged(nameof(SupportNote));
                    OnPropertyChanged(nameof(TargetSummary));
                    OnAdvanceabilityChanged();
                }
            }
        }

        /// Gz Sim version paired to the selected distro. Read-only in the UI.
        public string GzVersion => PairGz(_selectedDistro);

        /// True only for the v2.1-supported combination (Jazzy + Harmonic).
        public bool IsSupported => _selectedDistro == Ros2Distro.Jazzy;

        /// Empty when supported; otherwise a not-yet-supported explanation.
        public string SupportNote =>
            IsSupported
                ? string.Empty
                : $"{_selectedDistro} + {GzVersion} is not yet supported in this release. " +
                  "v2.1 targets ROS 2 Jazzy + Gz Harmonic only.";

        /// "Jazzy + Harmonic" — used by the Review step summary.
        public string TargetSummary => $"{_selectedDistro} + {GzVersion}";

        public override bool CanAdvance() => IsSupported;

        // ROS 2 ↔ Gz Sim pairing (REP 2000): Humble→Fortress, Jazzy→Harmonic,
        // Kilted→Ionic, Rolling→Ionic.
        private static string PairGz(Ros2Distro distro)
        {
            switch (distro)
            {
                case Ros2Distro.Humble: return "Fortress";
                case Ros2Distro.Jazzy: return "Harmonic";
                case Ros2Distro.Kilted: return "Ionic";
                case Ros2Distro.Rolling: return "Ionic";
                default: return "Harmonic";
            }
        }
    }
}
