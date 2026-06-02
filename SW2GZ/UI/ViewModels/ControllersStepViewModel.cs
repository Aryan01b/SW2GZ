/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8+ — Step 9. ros2_control selection. The joint_state_broadcaster is always
included (it publishes /joint_states and every ros2_control stack needs it),
so JointStateBroadcaster is a fixed checked flag. The user picks one primary
controller via WizardControllerType; ControllerName maps the enum to the
canonical ros2_controllers package controller name.

The existing ControlSpec record is intentionally minimal (joint names +
default controller) and is NOT reshaped here — BuildControl() returns it with
the broadcaster as the default controller, exactly as the pipeline expects.
The chosen primary controller is carried on this VM (ControllerName) for the
Review summary; threading it through ControlSpec is a backend follow-up.
*/
using System.Collections.Generic;
using SW2GZ.Build.Model;

namespace SW2GZ.UI.ViewModels
{
    public enum WizardControllerType { JointTrajectory, Position, Velocity, Effort, None }

    public sealed class ControllersStepViewModel : StepViewModelBase
    {
        private static readonly WizardControllerType[] _controllerOptions =
            (WizardControllerType[])System.Enum.GetValues(typeof(WizardControllerType));

        private WizardControllerType _selectedController = WizardControllerType.JointTrajectory;

        public ControllersStepViewModel() : base("Controllers", "ros2_control") { }

        /// Always on — every ros2_control stack publishes joint states.
        public bool JointStateBroadcaster => true;

        public IReadOnlyList<WizardControllerType> ControllerOptions => _controllerOptions;

        public WizardControllerType SelectedController
        {
            get => _selectedController;
            set
            {
                if (SetProperty(ref _selectedController, value))
                    OnPropertyChanged(nameof(ControllerName));
            }
        }

        /// Canonical ros2_controllers controller name for the current pick.
        public string ControllerName => MapName(_selectedController);

        /// Note shown in the view — the broadcaster is non-optional.
        public string BroadcasterNote =>
            "joint_state_broadcaster is always included (publishes /joint_states).";

        public override bool CanAdvance() => true;

        /// Build the minimal ControlSpec the pipeline consumes. The primary
        /// controller choice rides on ControllerName for Review; ControlSpec is
        /// left at its v2.1 shape (broadcaster default).
        public ControlSpec BuildControl(IReadOnlyList<string> jointNames) =>
            new ControlSpec(jointNames ?? new List<string>(),
                            ControlSpec.DefaultJointStateBroadcaster);

        public static string MapName(WizardControllerType type)
        {
            switch (type)
            {
                case WizardControllerType.JointTrajectory: return "joint_trajectory_controller";
                case WizardControllerType.Position: return "position_controllers/JointGroupPositionController";
                case WizardControllerType.Velocity: return "velocity_controllers/JointGroupVelocityController";
                case WizardControllerType.Effort: return "effort_controllers/JointGroupEffortController";
                case WizardControllerType.None: return "(none)";
                default: return "joint_trajectory_controller";
            }
        }
    }
}
