/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Collections.Generic;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using Xunit;

namespace SW2GZ.Test.Build
{
    public class JointDefValidatorTests
    {
        [Fact]
        public void CleanFixedJoint_NoWarnings()
        {
            var joints = new List<JointDef>
            {
                new JointDef { Name = "j", Type = UrdfJointType.Fixed },
            };
            Assert.Empty(JointDefValidator.Validate(joints));
        }

        [Fact]
        public void MovingJointWithNoAxis_Warns()
        {
            var joints = new List<JointDef>
            {
                new JointDef { Name = "drive", Type = UrdfJointType.Revolute },  // axis 0,0,0
            };
            Assert.Contains(JointDefValidator.Validate(joints), w => w.Contains("drive") && w.Contains("axis"));
        }

        [Fact]
        public void RevoluteLowerExceedsUpper_Warns()
        {
            var joints = new List<JointDef>
            {
                new JointDef { Name = "drive", Type = UrdfJointType.Revolute, AxisZ = 1,
                               LimitLower = 2, LimitUpper = 1 },
            };
            Assert.Contains(JointDefValidator.Validate(joints), w => w.Contains("drive") && w.Contains("limit"));
        }

        [Fact]
        public void ValidRevolute_NoWarnings()
        {
            var joints = new List<JointDef>
            {
                new JointDef { Name = "drive", Type = UrdfJointType.Revolute, AxisZ = 1,
                               LimitLower = -1, LimitUpper = 1 },
            };
            Assert.Empty(JointDefValidator.Validate(joints));
        }

        [Fact]
        public void Null_ReturnsEmpty()
        {
            Assert.Empty(JointDefValidator.Validate(null));
        }
    }
}
