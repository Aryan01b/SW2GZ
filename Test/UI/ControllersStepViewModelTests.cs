/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8+ — Controllers step VM tests: default controller, name mapping, and the
minimal ControlSpec built with joint names + the always-on broadcaster.
*/
using System.Collections.Generic;
using SW2GZ.Build.Model;
using SW2GZ.UI.ViewModels;
using Xunit;

namespace SW2GZ.Test.UI
{
    public class ControllersStepViewModelTests
    {
        [Fact]
        public void DefaultsToJointTrajectory()
        {
            var vm = new ControllersStepViewModel();
            Assert.Equal(WizardControllerType.JointTrajectory, vm.SelectedController);
            Assert.Equal("joint_trajectory_controller", vm.ControllerName);
            Assert.True(vm.JointStateBroadcaster);
            Assert.True(vm.CanAdvance());
        }

        [Fact]
        public void ControllerNameTracksSelection()
        {
            var vm = new ControllersStepViewModel();
            vm.SelectedController = WizardControllerType.Velocity;
            Assert.Contains("Velocity", vm.ControllerName);
            vm.SelectedController = WizardControllerType.None;
            Assert.Equal("(none)", vm.ControllerName);
        }

        [Fact]
        public void BuildControlReturnsSpecWithJointsAndBroadcaster()
        {
            var vm = new ControllersStepViewModel();
            var joints = new List<string> { "j1", "j2" };
            ControlSpec spec = vm.BuildControl(joints);
            Assert.Equal(joints, spec.JointNames);
            Assert.Equal(ControlSpec.DefaultJointStateBroadcaster, spec.DefaultController);
        }

        [Fact]
        public void BuildControlToleratesNullJoints()
        {
            ControlSpec spec = new ControllersStepViewModel().BuildControl(null);
            Assert.Empty(spec.JointNames);
        }
    }
}
