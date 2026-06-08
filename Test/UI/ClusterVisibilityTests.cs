using System.Collections.Generic;
using System.Numerics;
using SW2GZ.Build.Model;
using SW2GZ.UI.Ribbon;
using SW2GZ.URDFExport;
using Xunit;

namespace SW2GZ.Tests.UI
{
    public class ClusterVisibilityTests
    {
        [Theory]
        [InlineData(Sw2gzMode.Robot, RibbonCluster.Robot, true)]
        [InlineData(Sw2gzMode.Robot, RibbonCluster.World, false)]
        [InlineData(Sw2gzMode.Robot, RibbonCluster.Asset, false)]
        [InlineData(Sw2gzMode.World, RibbonCluster.Robot, false)]
        [InlineData(Sw2gzMode.World, RibbonCluster.World, true)]
        [InlineData(Sw2gzMode.World, RibbonCluster.Asset, false)]
        [InlineData(Sw2gzMode.Asset, RibbonCluster.Robot, false)]
        [InlineData(Sw2gzMode.Asset, RibbonCluster.World, false)]
        [InlineData(Sw2gzMode.Asset, RibbonCluster.Asset, true)]
        public void ModeClusters_Visible_OnlyOnMatch(
            Sw2gzMode mode, RibbonCluster cluster, bool expected)
        {
            Assert.Equal(expected, ClusterVisibility.IsVisible(mode, cluster));
        }

        [Theory]
        [InlineData(Sw2gzMode.Robot)]
        [InlineData(Sw2gzMode.World)]
        [InlineData(Sw2gzMode.Asset)]
        public void Common_AlwaysVisible(Sw2gzMode mode)
        {
            Assert.True(ClusterVisibility.IsVisible(mode, RibbonCluster.Common));
        }

        // ---- IsRobotReady — Preview ribbon button gate ----

        [Fact]
        public void IsRobotReady_NullRobot_False()
        {
            Assert.False(ClusterVisibility.IsRobotReady(null));
        }

        [Fact]
        public void IsRobotReady_EmptyRobot_False()
        {
            var robot = new Sw2gzRobotConfig();
            Assert.False(ClusterVisibility.IsRobotReady(robot));
        }

        [Fact]
        public void IsRobotReady_LinkWithComponents_NoJoints_False()
        {
            var robot = new Sw2gzRobotConfig
            {
                Links = new List<LinkDef>
                {
                    new LinkDef
                    {
                        Name = "base_link",
                        ComponentIds = new List<string> { "part1-1" },
                    },
                },
                Joints = new List<JointDef>(),
            };
            Assert.False(ClusterVisibility.IsRobotReady(robot));
        }

        [Fact]
        public void IsRobotReady_JointWithoutOrigin_False()
        {
            var joint = new JointDef
            {
                Name = "j1",
                ParentLink = "base_link",
                ChildLink = "link1",
            };
            // HasOrigin defaults to false; verify the predicate honours it.
            var robot = new Sw2gzRobotConfig
            {
                Links = new List<LinkDef>
                {
                    new LinkDef
                    {
                        Name = "base_link",
                        ComponentIds = new List<string> { "part1-1" },
                    },
                },
                Joints = new List<JointDef> { joint },
            };
            Assert.False(ClusterVisibility.IsRobotReady(robot));
        }

        [Fact]
        public void IsRobotReady_LinkWithComponents_PlusJointWithOrigin_True()
        {
            var joint = new JointDef
            {
                Name = "j1",
                ParentLink = "base_link",
                ChildLink = "link1",
            };
            joint.SetOrigin(new Vector3(0, 0, 0));
            var robot = new Sw2gzRobotConfig
            {
                Links = new List<LinkDef>
                {
                    new LinkDef
                    {
                        Name = "base_link",
                        ComponentIds = new List<string> { "part1-1" },
                    },
                },
                Joints = new List<JointDef> { joint },
            };
            Assert.True(ClusterVisibility.IsRobotReady(robot));
        }

        [Fact]
        public void IsRobotReady_LinkExistsButEmptyComponents_False()
        {
            var joint = new JointDef { Name = "j1" };
            joint.SetOrigin(new Vector3(0, 0, 0));
            var robot = new Sw2gzRobotConfig
            {
                Links = new List<LinkDef>
                {
                    new LinkDef { Name = "base_link", ComponentIds = new List<string>() },
                },
                Joints = new List<JointDef> { joint },
            };
            Assert.False(ClusterVisibility.IsRobotReady(robot));
        }
    }
}
