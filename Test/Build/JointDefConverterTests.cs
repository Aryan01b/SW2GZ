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
        [Fact]
        public void ToUrdfJoint_MapsStructuralFields_DefaultsForDroppedFields()
        {
            var def = new JointDef
            {
                Name = "drive", ParentLink = "base", ChildLink = "wheel",
                Type = UrdfJointType.Revolute, MateName = "Concentric1",
                AxisX = 0, AxisY = 0, AxisZ = 1,
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
        public void SetAxis_NormalizesDirection()
        {
            var def = new JointDef();
            def.SetAxis(new Vector3(0, 5, 0));
            Assert.True(def.HasAxis);
            Assert.Equal(0, def.AxisX, 5);
            Assert.Equal(1, def.AxisY, 5);
            Assert.Equal(0, def.AxisZ, 5);
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
