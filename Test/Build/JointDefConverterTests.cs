/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Collections.Generic;
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Test.Build
{
    public class JointDefConverterTests
    {
        [Theory]
        [InlineData(JointAxisPreset.PlusX, 1f, 0f, 0f)]
        [InlineData(JointAxisPreset.MinusX, -1f, 0f, 0f)]
        [InlineData(JointAxisPreset.PlusY, 0f, 1f, 0f)]
        [InlineData(JointAxisPreset.MinusY, 0f, -1f, 0f)]
        [InlineData(JointAxisPreset.PlusZ, 0f, 0f, 1f)]
        [InlineData(JointAxisPreset.MinusZ, 0f, 0f, -1f)]
        [InlineData(JointAxisPreset.None, 0f, 0f, 0f)]
        public void AxisVector_MapsPresets(JointAxisPreset preset, float x, float y, float z)
        {
            Assert.Equal(new Vector3(x, y, z), JointDefConverter.AxisVector(preset));
        }

        [Theory]
        [InlineData(1f, 0f, 0f, JointAxisPreset.PlusX)]
        [InlineData(-0.98f, 0.1f, 0.05f, JointAxisPreset.MinusX)]
        [InlineData(0.02f, 0.99f, 0f, JointAxisPreset.PlusY)]
        [InlineData(0f, -1f, 0.01f, JointAxisPreset.MinusY)]
        [InlineData(0.1f, 0.1f, 0.97f, JointAxisPreset.PlusZ)]
        [InlineData(0f, 0f, -1f, JointAxisPreset.MinusZ)]
        [InlineData(0f, 0f, 0f, JointAxisPreset.None)]
        public void SnapToPreset_PicksDominantSignedAxis(float x, float y, float z, JointAxisPreset expected)
        {
            Assert.Equal(expected, JointDefConverter.SnapToPreset(new Vector3(x, y, z)));
        }

        [Fact]
        public void ToUrdfJoint_MapsStructuralFields_DefaultsForDroppedFields()
        {
            var def = new JointDef
            {
                Name = "drive", ParentLink = "base", ChildLink = "wheel",
                Type = UrdfJointType.Revolute, Axis = JointAxisPreset.PlusZ,
                LimitLower = -1.5, LimitUpper = 1.5,
            };

            UrdfJoint j = JointDefConverter.ToUrdfJoint(def);

            Assert.Equal("drive", j.Name);
            Assert.Equal(UrdfJointType.Revolute, j.Type);
            Assert.Equal("base", j.ParentLink);
            Assert.Equal("wheel", j.ChildLink);
            Assert.Equal(Pose.Identity, j.Origin);                 // conversion deferred
            Assert.Equal(new Vector3(0, 0, 1), j.Axis);
            Assert.Equal(-1.5, j.LimitLower);
            Assert.Equal(1.5, j.LimitUpper);
            // Dropped (structural-only): converter supplies neutral defaults.
            Assert.Equal(0.0, j.LimitEffort);
            Assert.Equal(0.0, j.LimitVelocity);
            Assert.Equal(UrdfCmdInterface.Position, j.Interface);
        }

        [Fact]
        public void ToUrdfJoints_ConvertsAllInOrder()
        {
            var defs = new List<JointDef>
            {
                new JointDef { Name = "a", ChildLink = "x" },
                new JointDef { Name = "b", ChildLink = "y" },
            };

            List<UrdfJoint> joints = JointDefConverter.ToUrdfJoints(defs);

            Assert.Equal(2, joints.Count);
            Assert.Equal("a", joints[0].Name);
            Assert.Equal("b", joints[1].Name);
        }
    }
}
