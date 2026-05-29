/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using SW2GZ.Gz;
using Xunit;

namespace SW2GZ.Writers.Tests
{
    public class TestGzPluginTags
    {
        [Fact]
        public void Write_EmitsCorrectPluginClassName_Bug8()
        {
            var xml = GzPluginTags.WriteGzRos2ControlXacro("my_pkg");
            Assert.Contains("gz_ros2_control-system", xml);
            Assert.Contains("gz_ros2_control::GazeboSimROS2ControlPlugin", xml);
        }

        [Fact]
        public void Write_EmitsParametersWithFindPackage_Bug1()
        {
            var xml = GzPluginTags.WriteGzRos2ControlXacro("my_pkg");
            Assert.Contains("<parameters>$(find my_pkg)/config/controllers.yaml</parameters>", xml);
        }

        [Fact]
        public void Write_IsNonEmpty_Bug1()
        {
            var xml = GzPluginTags.WriteGzRos2ControlXacro("my_pkg");
            Assert.Contains("<gazebo>", xml);
            Assert.Contains("</gazebo>", xml);
        }

        [Fact]
        public void Write_RobotElementOpensXacroNamespace()
        {
            var xml = GzPluginTags.WriteGzRos2ControlXacro("my_pkg");
            Assert.Contains("<robot xmlns:xacro=\"http://www.ros.org/wiki/xacro\">", xml);
        }

        [Fact]
        public void Write_StartsWithXmlProlog()
        {
            var xml = GzPluginTags.WriteGzRos2ControlXacro("my_pkg");
            Assert.StartsWith("<?xml version=\"1.0\"?>", xml.TrimStart());
        }

        [Fact]
        public void Write_NullPackageName_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => GzPluginTags.WriteGzRos2ControlXacro(null));
        }

        [Fact]
        public void Write_EmptyPackageName_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => GzPluginTags.WriteGzRos2ControlXacro("   "));
        }

        [Fact]
        public void Write_DifferentPackageName_InterpolatesIntoFindCall()
        {
            var xml = GzPluginTags.WriteGzRos2ControlXacro("arm_2dof_description");
            Assert.Contains("$(find arm_2dof_description)", xml);
            Assert.DoesNotContain("$(find my_pkg)", xml);
        }
    }
}
