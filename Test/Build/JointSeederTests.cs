/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Collections.Generic;
using System.Linq;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using Xunit;

namespace SW2GZ.Test.Build
{
    public class JointSeederTests
    {
        private static LinkDef L(string name, string parent) =>
            new LinkDef { Name = name, ParentName = parent, ComponentIds = new List<string> { name + "_c" } };

        [Fact]
        public void SeedsOneJointPerNonRootLink_RootHasNone()
        {
            var links = new List<LinkDef> { L("base", ""), L("wheel", "base") };

            JointDef j = Assert.Single(JointSeeder.Sync(links, null));

            Assert.Equal("wheel", j.ChildLink);
            Assert.Equal("base", j.ParentLink);
            Assert.Equal("base_wheel_joint", j.Name);     // <parent>_<child>_joint
            Assert.Equal(UrdfJointType.Fixed, j.Type);    // until a mate is assigned
        }

        [Fact]
        public void PreservesAssignmentsButNameTracksTree()
        {
            var links = new List<LinkDef> { L("base", ""), L("wheel", "base") };
            var existing = new List<JointDef>
            {
                new JointDef { Name = "stale_name", ParentLink = "base", ChildLink = "wheel",
                               Type = UrdfJointType.Revolute, MateName = "Concentric1", AxisY = 1 },
            };

            JointDef j = Assert.Single(JointSeeder.Sync(links, existing));

            Assert.Equal("base_wheel_joint", j.Name);     // name follows the tree
            Assert.Equal(UrdfJointType.Revolute, j.Type); // assignment preserved
            Assert.Equal("Concentric1", j.MateName);
        }

        [Fact]
        public void ReparentingUpdatesParentLink_KeepsEdits()
        {
            var links = new List<LinkDef> { L("base", ""), L("arm", "base"), L("wheel", "arm") };
            var existing = new List<JointDef>
            {
                new JointDef { Name = "drive", ParentLink = "base", ChildLink = "wheel",
                               Type = UrdfJointType.Revolute },
            };

            JointDef wheelJoint = JointSeeder.Sync(links, existing).Single(j => j.ChildLink == "wheel");

            Assert.Equal("arm", wheelJoint.ParentLink);
            Assert.Equal(UrdfJointType.Revolute, wheelJoint.Type);
        }

        [Fact]
        public void DropsJointWhoseChildIsGone()
        {
            var links = new List<LinkDef> { L("base", ""), L("wheel", "base") };
            var existing = new List<JointDef>
            {
                new JointDef { Name = "ghost_joint", ParentLink = "base", ChildLink = "ghost" },
                new JointDef { Name = "base_wheel_joint", ParentLink = "base", ChildLink = "wheel" },
            };

            List<JointDef> joints = JointSeeder.Sync(links, existing);

            Assert.DoesNotContain(joints, j => j.ChildLink == "ghost");
            Assert.Contains(joints, j => j.ChildLink == "wheel");
        }

        [Fact]
        public void NullLinks_ReturnsEmpty()
        {
            Assert.Empty(JointSeeder.Sync(null, null));
        }

        [Theory]
        [InlineData(MateKind.Fixed, UrdfJointType.Fixed)]
        [InlineData(MateKind.Continuous, UrdfJointType.Continuous)]
        [InlineData(MateKind.Revolute, UrdfJointType.Revolute)]
        [InlineData(MateKind.Prismatic, UrdfJointType.Prismatic)]
        [InlineData(MateKind.Planar, UrdfJointType.Planar)]
        [InlineData(MateKind.Floating, UrdfJointType.Floating)]
        public void ToJointType_MapsMateKind(MateKind kind, UrdfJointType expected)
        {
            Assert.Equal(expected, JointSeeder.ToJointType(kind));
        }

        [Fact]
        public void JointName_IsParentChildJoint()
        {
            Assert.Equal("base_link_wheel_joint", JointSeeder.JointName("base_link", "wheel"));
        }
    }
}
