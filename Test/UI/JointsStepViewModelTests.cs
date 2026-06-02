/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8+ — Joints step VM tests. Populate from extracted joints, verify edits flow
into BuildJoints(), and that an inconsistent revolute limit is flagged but
does not block advancing.
*/
using System.Collections.Generic;
using System.Numerics;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using SW2GZ.UI.ViewModels;
using Xunit;

namespace SW2GZ.Test.UI
{
    public class JointsStepViewModelTests
    {
        private static JointDto Joint(
            string name, UrdfJointType type = UrdfJointType.Revolute,
            double? lower = -1.0, double? upper = 1.0) =>
            new JointDto(name, type, "base_link", "link1", Pose.Identity,
                         new Vector3(0, 0, 1), lower, upper, 100.0, 2.0, UrdfCmdInterface.Position);

        [Fact]
        public void PopulatesFromJointDtos()
        {
            var vm = new JointsStepViewModel(new List<JointDto> { Joint("j1"), Joint("j2") });
            Assert.Equal(2, vm.JointCount);
            Assert.Equal("j1", vm.Joints[0].Name);
            Assert.Equal("base_link", vm.Joints[0].ParentLink);
            Assert.Same(vm.Joints[0], vm.SelectedJoint);
        }

        [Fact]
        public void AlwaysAdvanceable()
        {
            Assert.True(new JointsStepViewModel((IReadOnlyList<JointDto>)null).CanAdvance());
        }

        [Fact]
        public void EditingTypeAndLimitsReflectedInBuildJoints()
        {
            var vm = new JointsStepViewModel(new List<JointDto> { Joint("j1") });
            vm.Joints[0].Type = UrdfJointType.Prismatic;
            vm.Joints[0].Interface = UrdfCmdInterface.Velocity;
            vm.Joints[0].LimitLower = -0.5;
            vm.Joints[0].LimitUpper = 0.75;

            IReadOnlyList<UrdfJoint> built = vm.BuildJoints();
            Assert.Single(built);
            Assert.Equal(UrdfJointType.Prismatic, built[0].Type);
            Assert.Equal(UrdfCmdInterface.Velocity, built[0].Interface);
            Assert.Equal(-0.5, built[0].LimitLower);
            Assert.Equal(0.75, built[0].LimitUpper);
            // Origin + axis carried through unchanged.
            Assert.Equal(new Vector3(0, 0, 1), built[0].Axis);
        }

        [Fact]
        public void RevoluteWithLowerAboveUpperIsFlaggedButNotBlocking()
        {
            var vm = new JointsStepViewModel(new List<JointDto>
            {
                Joint("j1", UrdfJointType.Revolute, lower: 2.0, upper: 1.0),
            });
            Assert.True(vm.Joints[0].HasValidationMessage);
            Assert.Equal(1, vm.InvalidLimitCount);
            Assert.True(vm.CanAdvance()); // warn-not-block
        }

        [Fact]
        public void FixedJointHasNoLimitsAndNoFlag()
        {
            var vm = new JointsStepViewModel(new List<JointDto>
            {
                Joint("j1", UrdfJointType.Fixed, lower: 2.0, upper: 1.0),
            });
            Assert.False(vm.Joints[0].HasLimits);
            Assert.False(vm.Joints[0].HasValidationMessage);
        }

        [Fact]
        public void ConstructsFromUrdfJointList()
        {
            var joint = new UrdfJoint("jx", UrdfJointType.Continuous, "a", "b",
                Pose.Identity, new Vector3(1, 0, 0), null, null, 50.0, 3.0, UrdfCmdInterface.Effort);
            var vm = new JointsStepViewModel(new List<UrdfJoint> { joint });
            Assert.Equal(1, vm.JointCount);
            Assert.Equal("jx", vm.Joints[0].Name);
            Assert.Equal(UrdfJointType.Continuous, vm.Joints[0].Type);
        }
    }
}
