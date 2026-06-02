/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8+ — Step 5. Edit the extracted joints: override type, command interface and
limits. Populated from a caller-supplied JointDto list (the VM never touches
COM). Editing is optional, so CanAdvance is always true; per-joint limit
inconsistencies surface as advisory ValidationMessages (warn-not-block).
BuildJoints() re-emits the edited UrdfJoint list for the export model.
*/
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SW2GZ.Build.Urdf;

namespace SW2GZ.UI.ViewModels
{
    public sealed class JointsStepViewModel : StepViewModelBase
    {
        private JointEditViewModel _selectedJoint;

        public JointsStepViewModel(IReadOnlyList<JointDto> joints)
            : base("Joints", "Type & limits")
        {
            Joints = new ObservableCollection<JointEditViewModel>();
            if (joints != null)
                foreach (JointDto dto in joints)
                    Joints.Add(new JointEditViewModel(dto));

            _selectedJoint = Joints.FirstOrDefault();
        }

        /// Convenience overload: populate straight from extracted UrdfJoints.
        public JointsStepViewModel(IReadOnlyList<UrdfJoint> joints)
            : this(joints?.Select(JointDto.From).ToList())
        {
        }

        public ObservableCollection<JointEditViewModel> Joints { get; }

        public JointEditViewModel SelectedJoint
        {
            get => _selectedJoint;
            set => SetProperty(ref _selectedJoint, value);
        }

        public int JointCount => Joints.Count;

        /// Count of joints currently flagging an inconsistent limit range.
        public int InvalidLimitCount => Joints.Count(j => j.HasValidationMessage);

        // Joints are optional to edit — never block advancing.
        public override bool CanAdvance() => true;

        /// Re-emit the edited joints (overrides applied onto the originals).
        public IReadOnlyList<UrdfJoint> BuildJoints() =>
            Joints.Select(j => j.BuildJoint()).ToList();
    }
}
