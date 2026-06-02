/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8+ — Step 8. The dedicated sensors page: a master list with add/remove plus a
detail panel (Kind, attached link, pose xyz+rpy, topic, gz_frame_id, update
rate, and Kind-specific params). AvailableLinks feeds the link combo. Sensors
are optional, so CanAdvance is always true; validation surfaces advisory
warnings (duplicate names, missing link, non-positive update rate).

Add seeds a new IMU with a unique default name attached to the first available
link, then applies sensible topic / gz_frame_id defaults. BuildSensors()
materializes the concrete SensorDef list for the export model.
*/
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SW2GZ.Build.Model;
using SW2GZ.UI.Mvvm;

namespace SW2GZ.UI.ViewModels
{
    public sealed class SensorsStepViewModel : StepViewModelBase
    {
        private SensorEditViewModel _selectedSensor;

        public SensorsStepViewModel(IReadOnlyList<string> availableLinks)
            : base("Sensors", "Add & configure")
        {
            AvailableLinks = availableLinks != null
                ? new List<string>(availableLinks)
                : new List<string>();
            Sensors = new ObservableCollection<SensorEditViewModel>();

            AddSensorCommand = new RelayCommand(AddSensor);
            RemoveSensorCommand = new RelayCommand(RemoveSensor, () => _selectedSensor != null);
        }

        public ObservableCollection<SensorEditViewModel> Sensors { get; }

        public IReadOnlyList<string> AvailableLinks { get; }

        public IReadOnlyList<SensorKind> SensorKindOptions =>
            (SensorKind[])System.Enum.GetValues(typeof(SensorKind));

        public SensorEditViewModel SelectedSensor
        {
            get => _selectedSensor;
            set
            {
                if (SetProperty(ref _selectedSensor, value))
                    RemoveSensorCommand.RaiseCanExecuteChanged();
            }
        }

        public int SensorCount => Sensors.Count;

        /// Names appearing on more than one sensor (advisory — flagged, not blocked).
        public IReadOnlyList<string> DuplicateNames =>
            Sensors.GroupBy(s => s.Name)
                   .Where(g => g.Count() > 1)
                   .Select(g => g.Key)
                   .ToList();

        public bool HasDuplicateNames => DuplicateNames.Count > 0;

        /// Sensors with an invalid configuration (no link, or update rate <= 0).
        public int InvalidSensorCount =>
            Sensors.Count(s => !s.UpdateRateValid || string.IsNullOrWhiteSpace(s.AttachedLink));

        public RelayCommand AddSensorCommand { get; }
        public RelayCommand RemoveSensorCommand { get; }

        // Sensors optional — never block advancing.
        public override bool CanAdvance() => true;

        public IReadOnlyList<SensorDef> BuildSensors() =>
            Sensors.Select(s => s.BuildSensor()).ToList();

        private void AddSensor()
        {
            var sensor = new SensorEditViewModel(UniqueName("imu"), SensorKind.Imu)
            {
                AttachedLink = AvailableLinks.FirstOrDefault() ?? string.Empty,
            };
            sensor.ApplyDefaults();

            // Keep the topic in sync with name/kind until the user hand-edits it,
            // and re-evaluate validity/duplicate flags as the detail panel changes.
            sensor.NameChanged += (s, e) => OnSensorMetaChanged();
            sensor.KindChanged += (s, e) => OnSensorMetaChanged();
            sensor.AttachmentChanged += (s, e) => OnSensorMetaChanged();
            sensor.ValidityChanged += (s, e) => OnPropertyChanged(nameof(InvalidSensorCount));

            Sensors.Add(sensor);
            SelectedSensor = sensor;
            OnPropertyChanged(nameof(SensorCount));
            RaiseFlags();
        }

        private void RemoveSensor()
        {
            if (_selectedSensor == null)
                return;
            int idx = Sensors.IndexOf(_selectedSensor);
            Sensors.Remove(_selectedSensor);
            SelectedSensor = Sensors.Count > 0
                ? Sensors[idx < Sensors.Count ? idx : Sensors.Count - 1]
                : null;
            OnPropertyChanged(nameof(SensorCount));
            RaiseFlags();
        }

        private void OnSensorMetaChanged()
        {
            OnPropertyChanged(nameof(InvalidSensorCount));
            RaiseFlags();
        }

        private void RaiseFlags()
        {
            OnPropertyChanged(nameof(DuplicateNames));
            OnPropertyChanged(nameof(HasDuplicateNames));
            OnPropertyChanged(nameof(InvalidSensorCount));
        }

        private string UniqueName(string baseName)
        {
            string candidate = baseName;
            int n = 1;
            while (Sensors.Any(s => s.Name == candidate))
                candidate = baseName + "_" + (++n);
            return candidate;
        }
    }
}
