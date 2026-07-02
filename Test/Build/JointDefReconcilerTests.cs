/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Collections.Generic;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using Xunit;

namespace SW2GZ.Writers.Tests
{
    public class JointDefReconcilerTests
    {
        [Fact]
        public void Reconcile_NewLink_CreatesDefaultFixedJoint()
        {
            var links = new List<LinkDef>
            {
                new LinkDef { Name = "base_link", ParentName = "" },
                new LinkDef { Name = "arm_link",  ParentName = "base_link" },
            };

            List<JointDef> result = JointDefReconciler.Reconcile(new List<JointDef>(), links);

            JointDef j = Assert.Single(result);
            Assert.Equal("base_link_to_arm_link", j.Name);
            Assert.Equal("base_link", j.ParentLink);
            Assert.Equal("arm_link", j.ChildLink);
            Assert.Equal(UrdfJointType.Fixed, j.Type);
        }

        [Fact]
        public void Reconcile_ExistingPairPreserved_KeepsUserEdits()
        {
            var links = new List<LinkDef>
            {
                new LinkDef { Name = "base_link", ParentName = "" },
                new LinkDef { Name = "arm_link",  ParentName = "base_link" },
            };
            var existing = new List<JointDef>
            {
                new JointDef
                {
                    Name = "shoulder", ParentLink = "base_link", ChildLink = "arm_link",
                    Type = UrdfJointType.Revolute, AxisZ = 1, LimitLower = -1.0, LimitUpper = 1.0,
                },
            };

            List<JointDef> result = JointDefReconciler.Reconcile(existing, links);

            JointDef j = Assert.Single(result);
            Assert.Same(existing[0], j);
            Assert.Equal("shoulder", j.Name);
            Assert.Equal(UrdfJointType.Revolute, j.Type);
            Assert.Equal(1.0, j.AxisZ);
        }

        [Fact]
        public void Reconcile_RemovedLink_DropsItsJoint()
        {
            var links = new List<LinkDef>
            {
                new LinkDef { Name = "base_link", ParentName = "" },
                new LinkDef { Name = "arm_link",  ParentName = "base_link" },
            };
            var existing = new List<JointDef>
            {
                new JointDef { Name = "base_link_to_arm_link", ParentLink = "base_link", ChildLink = "arm_link", Type = UrdfJointType.Revolute },
                new JointDef { Name = "arm_link_to_wrist_link", ParentLink = "arm_link", ChildLink = "wrist_link", Type = UrdfJointType.Continuous },
            };

            List<JointDef> result = JointDefReconciler.Reconcile(existing, links);

            JointDef j = Assert.Single(result);
            Assert.Equal("arm_link", j.ChildLink);
        }

        [Fact]
        public void Reconcile_RootOnlyLink_ReturnsEmptyJointList()
        {
            var links = new List<LinkDef> { new LinkDef { Name = "base_link", ParentName = "" } };

            List<JointDef> result = JointDefReconciler.Reconcile(new List<JointDef>(), links);

            Assert.Empty(result);
        }

        [Fact]
        public void Reconcile_NullExistingAndNullLinks_DoesNotThrow()
        {
            Assert.Empty(JointDefReconciler.Reconcile(null, null));
            Assert.Empty(JointDefReconciler.Reconcile(null, new List<LinkDef>()));
        }
    }
}
