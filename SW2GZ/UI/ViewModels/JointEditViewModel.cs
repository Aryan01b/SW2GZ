/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8+ — one editable joint row in the Joints step. Name / parent / child come
from extraction and are read-only; the user may override the joint Type, the
command Interface, and the four limit fields. Origin + Axis are carried
through unchanged from the source DTO so BuildJoint() can re-emit a complete
UrdfJoint.

Validation is warn-not-block: a Revolute/Prismatic joint whose lower limit
exceeds its upper limit surfaces a ValidationMessage but does not stop the
wizard advancing (limits are advisory in v2.1; the structural validators on
the export path are the hard gate).
*/
using System.Numerics;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using SW2GZ.UI.Mvvm;

namespace SW2GZ.UI.ViewModels
{
    public sealed class JointEditViewModel : ObservableObject
    {
        private static readonly UrdfJointType[] _typeOptions =
            (UrdfJointType[])System.Enum.GetValues(typeof(UrdfJointType));
        private static readonly UrdfCmdInterface[] _interfaceOptions =
            (UrdfCmdInterface[])System.Enum.GetValues(typeof(UrdfCmdInterface));

        private readonly Pose _origin;
        private readonly Vector3 _axis;

        private UrdfJointType _type;
        private UrdfCmdInterface _interface;
        private double? _limitLower;
        private double? _limitUpper;
        private double _limitEffort;
        private double _limitVelocity;

        public JointEditViewModel(JointDto dto)
        {
            if (dto == null) throw new System.ArgumentNullException(nameof(dto));
            Name = dto.Name;
            ParentLink = dto.ParentLink;
            ChildLink = dto.ChildLink;
            _origin = dto.Origin ?? Pose.Identity;
            _axis = dto.Axis;
            _type = dto.Type;
            _interface = dto.Interface;
            _limitLower = dto.LimitLower;
            _limitUpper = dto.LimitUpper;
            _limitEffort = dto.LimitEffort;
            _limitVelocity = dto.LimitVelocity;
        }

        public string Name { get; }
        public string ParentLink { get; }
        public string ChildLink { get; }

        public System.Collections.Generic.IReadOnlyList<UrdfJointType> JointTypeOptions => _typeOptions;
        public System.Collections.Generic.IReadOnlyList<UrdfCmdInterface> InterfaceOptions => _interfaceOptions;

        public UrdfJointType Type
        {
            get => _type;
            set
            {
                if (SetProperty(ref _type, value))
                {
                    OnPropertyChanged(nameof(HasLimits));
                    OnPropertyChanged(nameof(ValidationMessage));
                    OnPropertyChanged(nameof(HasValidationMessage));
                }
            }
        }

        public UrdfCmdInterface Interface
        {
            get => _interface;
            set => SetProperty(ref _interface, value);
        }

        public double? LimitLower
        {
            get => _limitLower;
            set
            {
                if (SetProperty(ref _limitLower, value))
                    RaiseValidation();
            }
        }

        public double? LimitUpper
        {
            get => _limitUpper;
            set
            {
                if (SetProperty(ref _limitUpper, value))
                    RaiseValidation();
            }
        }

        public double LimitEffort
        {
            get => _limitEffort;
            set => SetProperty(ref _limitEffort, value);
        }

        public double LimitVelocity
        {
            get => _limitVelocity;
            set => SetProperty(ref _limitVelocity, value);
        }

        /// Revolute/Prismatic joints carry meaningful lower/upper limits;
        /// Fixed/Continuous do not (the view can hide the limit boxes).
        public bool HasLimits =>
            _type == UrdfJointType.Revolute || _type == UrdfJointType.Prismatic;

        /// Non-empty when the limits are inconsistent (lower > upper) for a
        /// limited joint type. Advisory only — see class summary.
        public string ValidationMessage =>
            HasLimits && _limitLower.HasValue && _limitUpper.HasValue && _limitLower > _limitUpper
                ? "Lower limit exceeds upper limit."
                : string.Empty;

        public bool HasValidationMessage => ValidationMessage.Length > 0;

        /// Re-emit a UrdfJoint with the edited overrides applied over the
        /// carried-through origin/axis.
        public UrdfJoint BuildJoint() =>
            new UrdfJoint(Name, _type, ParentLink, ChildLink, _origin, _axis,
                          _limitLower, _limitUpper, _limitEffort, _limitVelocity, _interface);

        private void RaiseValidation()
        {
            OnPropertyChanged(nameof(ValidationMessage));
            OnPropertyChanged(nameof(HasValidationMessage));
        }
    }
}
