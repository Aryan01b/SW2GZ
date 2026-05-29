/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Collections.Generic;
using SW2GZ.Ros2;
using Xunit;

namespace SW2GZ.Writers.Tests
{
    public class TestRos2ControlWriter
    {
        private static readonly IReadOnlyList<string> Joints = new[] { "j1", "j2" };

        [Fact]
        public void Write_ParametersUseLiteralFindPkg_Bug3()
        {
            var xml = Ros2ControlWriter.Write("my_robot_description", Joints);
            Assert.Contains("$(find my_robot_description)/config/controllers.yaml", xml);
            Assert.DoesNotContain("$(arg pkg)", xml);
            Assert.DoesNotContain("find-pkg-share", xml);
        }

        [Fact]
        public void Write_UsesHarmonicHardwarePluginClass()
        {
            var xml = Ros2ControlWriter.Write("pkg", Joints);
            // Harmonic gz_ros2_control hardware plugin
            Assert.Contains("gz_ros2_control/GazeboSimSystem", xml);
        }

        [Fact]
        public void Write_DoesNotEmitGazeboBlock()
        {
            // The <gazebo><plugin gz_ros2_control-system> block lives in inc/gz.xacro
            // (owned by GzPluginTags.WriteGzRos2ControlXacro). Avoid emitting twice.
            var xml = Ros2ControlWriter.Write("pkg", Joints);
            Assert.DoesNotContain("<gazebo>", xml);
            Assert.DoesNotContain("gz_ros2_control-system", xml);
        }

        [Fact]
        public void Write_EmitsRos2ControlElement()
        {
            var xml = Ros2ControlWriter.Write("pkg", Joints);
            Assert.Contains("<ros2_control name=\"GzSystem\" type=\"system\">", xml);
            Assert.Contains("</ros2_control>", xml);
        }

        [Fact]
        public void Write_EmitsOneJointBlockPerName()
        {
            var xml = Ros2ControlWriter.Write("pkg", Joints);
            Assert.Contains("<joint name=\"j1\">", xml);
            Assert.Contains("<joint name=\"j2\">", xml);
            Assert.Contains("<command_interface name=\"position\"/>", xml);
            Assert.Contains("<state_interface name=\"position\"/>", xml);
            Assert.Contains("<state_interface name=\"velocity\"/>", xml);
        }

        [Fact]
        public void Write_NoJoints_StillProducesValidSkeleton()
        {
            var xml = Ros2ControlWriter.Write("pkg", new string[0]);
            Assert.Contains("<ros2_control", xml);
            Assert.DoesNotContain("<joint", xml);
        }

        [Fact]
        public void Write_NullPackageName_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => Ros2ControlWriter.Write(null, Joints));
        }

        [Fact]
        public void Write_WhitespacePackageName_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => Ros2ControlWriter.Write("  ", Joints));
        }

        [Fact]
        public void Write_NullJointList_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => Ros2ControlWriter.Write("pkg", null));
        }

        [Fact]
        public void Write_StartsWithXmlProlog()
        {
            var xml = Ros2ControlWriter.Write("pkg", Joints);
            Assert.StartsWith("<?xml version=\"1.0\"?>", xml.TrimStart());
        }
    }
}
