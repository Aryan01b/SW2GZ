/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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

            List<JointDef> joints = JointSeeder.Sync(links, null);

            JointDef j = Assert.Single(joints);
            Assert.Equal("wheel", j.ChildLink);
            Assert.Equal("base", j.ParentLink);
            Assert.Equal("wheel_joint", j.Name);
            Assert.Equal(UrdfJointType.Fixed, j.Type);
            Assert.False(j.HasAxis);
        }

        [Fact]
        public void PreservesUserEditsOnResync()
        {
            var links = new List<LinkDef> { L("base", ""), L("wheel", "base") };
            var existing = new List<JointDef>
            {
                new JointDef { Name = "drive", ParentLink = "base", ChildLink = "wheel",
                               Type = UrdfJointType.Revolute, AxisRef = "Axis1", AxisY = 1,
                               LimitLower = -1, LimitUpper = 1 },
            };

            JointDef j = Assert.Single(JointSeeder.Sync(links, existing));

            Assert.Equal("drive", j.Name);
            Assert.Equal(UrdfJointType.Revolute, j.Type);
            Assert.Equal("Axis1", j.AxisRef);
            Assert.Equal(1.0, j.AxisY, 5);
            Assert.Equal(-1, j.LimitLower);
        }

        [Fact]
        public void ReparentingUpdatesParentLink_KeepsEdits()
        {
            // wheel was under base; user re-parents it under arm in the Links step.
            var links = new List<LinkDef> { L("base", ""), L("arm", "base"), L("wheel", "arm") };
            var existing = new List<JointDef>
            {
                new JointDef { Name = "drive", ParentLink = "base", ChildLink = "wheel",
                               Type = UrdfJointType.Revolute },
            };

            JointDef wheelJoint = JointSeeder.Sync(links, existing).Single(j => j.ChildLink == "wheel");

            Assert.Equal("arm", wheelJoint.ParentLink);
            Assert.Equal(UrdfJointType.Revolute, wheelJoint.Type); // edit preserved
        }

        [Fact]
        public void DropsJointWhoseChildIsGone()
        {
            var links = new List<LinkDef> { L("base", ""), L("wheel", "base") };
            var existing = new List<JointDef>
            {
                new JointDef { Name = "ghost_joint", ParentLink = "base", ChildLink = "ghost" },
                new JointDef { Name = "wheel_joint", ParentLink = "base", ChildLink = "wheel" },
            };

            List<JointDef> joints = JointSeeder.Sync(links, existing);

            Assert.DoesNotContain(joints, j => j.ChildLink == "ghost");
            Assert.Contains(joints, j => j.ChildLink == "wheel");
        }

        [Fact]
        public void DropsJointWhenChildBecomesRoot()
        {
            // wheel is re-rooted (parent cleared) — its joint should disappear.
            var links = new List<LinkDef> { L("wheel", "") };
            var existing = new List<JointDef>
            {
                new JointDef { Name = "wheel_joint", ParentLink = "base", ChildLink = "wheel" },
            };

            Assert.Empty(JointSeeder.Sync(links, existing));
        }

        [Fact]
        public void NullLinks_ReturnsEmpty()
        {
            Assert.Empty(JointSeeder.Sync(null, null));
        }

        // ── mate-derived axis + type ──────────────────────────────────────────

        [Fact]
        public void NewJoint_TakesAxisAndTypeFromMate()
        {
            // wheel's components are "wheel_c"; base's are "base_c".
            var links = new List<LinkDef> { L("base", ""), L("wheel", "base") };
            var mates = new List<MateAxis>
            {
                new MateAxis("base_c", "wheel_c", new Vector3(0, 0.99f, 0.01f), MateKind.Revolute),
            };

            JointDef j = Assert.Single(JointSeeder.Sync(links, null, mates));

            Assert.Equal(UrdfJointType.Revolute, j.Type);
            Assert.True(j.HasAxis);
            Assert.Equal(1.0, j.AxisY, 2);                 // normalized mate direction (~+Y)
            Assert.Equal(0.0, j.AxisX, 2);
        }

        [Fact]
        public void NoMatchingMate_StaysFixedNone()
        {
            var links = new List<LinkDef> { L("base", ""), L("wheel", "base") };
            var mates = new List<MateAxis>
            {
                new MateAxis("other_c", "stranger_c", new Vector3(1, 0, 0), MateKind.Revolute),
            };

            JointDef j = Assert.Single(JointSeeder.Sync(links, null, mates));

            Assert.Equal(UrdfJointType.Fixed, j.Type);
            Assert.False(j.HasAxis);
        }

        [Fact]
        public void MateDoesNotOverrideExistingUserEdit()
        {
            var links = new List<LinkDef> { L("base", ""), L("wheel", "base") };
            var existing = new List<JointDef>
            {
                new JointDef { Name = "drive", ParentLink = "base", ChildLink = "wheel",
                               Type = UrdfJointType.Prismatic, AxisX = 1 },
            };
            var mates = new List<MateAxis>
            {
                new MateAxis("base_c", "wheel_c", new Vector3(0, 0, 1), MateKind.Revolute),
            };

            JointDef j = Assert.Single(JointSeeder.Sync(links, existing, mates));

            Assert.Equal(UrdfJointType.Prismatic, j.Type);     // user edit preserved
            Assert.Equal(1.0, j.AxisX, 5);
            Assert.Equal(0.0, j.AxisZ, 5);
        }
    }
}
