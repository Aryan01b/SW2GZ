/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — base for the five wizard steps. Carries the rail metadata (Title /
Subtitle), the active/complete flags the rail binds to, and the CanAdvance()
validation gate the WizardViewModel queries before enabling Next.
*/
using SW2GZ.UI.Mvvm;

namespace SW2GZ.UI.ViewModels
{
    public abstract class StepViewModelBase : ObservableObject
    {
        private string _title;
        private string _subtitle;
        private bool _isActive;
        private bool _isComplete;

        protected StepViewModelBase(string title, string subtitle)
        {
            _title = title;
            _subtitle = subtitle;
        }

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string Subtitle
        {
            get => _subtitle;
            set => SetProperty(ref _subtitle, value);
        }

        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        public bool IsComplete
        {
            get => _isComplete;
            set => SetProperty(ref _isComplete, value);
        }

        /// Validation gate: the wizard's Next button is disabled while this
        /// returns false. Steps raise OnAdvanceabilityChanged() so the wizard
        /// can re-query and refresh Next's CanExecute.
        public abstract bool CanAdvance();

        /// Raised when something changed that may flip CanAdvance(). The wizard
        /// subscribes to refresh the Next command.
        public event System.EventHandler AdvanceabilityChanged;

        protected void OnAdvanceabilityChanged() =>
            AdvanceabilityChanged?.Invoke(this, System.EventArgs.Empty);
    }
}
