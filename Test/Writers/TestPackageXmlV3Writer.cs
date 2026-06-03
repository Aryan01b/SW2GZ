/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using SW2GZ.Ros2;
using Xunit;

namespace SW2GZ.Writers.Tests
{
    public class TestPackageXmlV3Writer
    {
        private static PackageXmlInput Min(string name = "my_pkg") =>
            new PackageXmlInput(name, "0.0.1", "test", "Aryan", "a@b", "Apache-2.0");

        [Fact]
        public void Write_EmitsHarmonicDependencies()
        {
            var xml = PackageXmlV3Writer.Write(Min());
            Assert.Contains("<exec_depend>ros_gz_sim</exec_depend>", xml);
            Assert.Contains("<exec_depend>gz_ros2_control</exec_depend>", xml);
            Assert.Contains("<exec_depend>ros_gz_bridge</exec_depend>", xml);
        }

        [Fact]
        public void Write_GzMode_EmitsLeanDeps()
        {
            var xml = PackageXmlV3Writer.Write(new PackageXmlInput(
                "asset_pkg", "0.0.1", "d", "m", "e@x", "MIT") { GzMode = true });
            Assert.Contains("<exec_depend>ros_gz_sim</exec_depend>", xml);
            Assert.DoesNotContain("ros2_control", xml);
            Assert.DoesNotContain("gz_ros2_control", xml);
            Assert.DoesNotContain("ros_gz_bridge", xml);
        }

        [Fact]
        public void Write_DoesNotEmitFortressOrIgnPackages()
        {
            var xml = PackageXmlV3Writer.Write(Min());
            Assert.DoesNotContain("ros_ign_gazebo", xml);
            Assert.DoesNotContain("ros_ign_bridge", xml);
            Assert.DoesNotContain("ign_ros2_control", xml);
        }

        [Fact]
        public void Write_EmitsCoreDeps()
        {
            var xml = PackageXmlV3Writer.Write(Min());
            Assert.Contains("<buildtool_depend>ament_cmake</buildtool_depend>", xml);
            Assert.Contains("<exec_depend>robot_state_publisher</exec_depend>", xml);
            Assert.Contains("<exec_depend>joint_state_publisher_gui</exec_depend>", xml);
            Assert.Contains("<exec_depend>xacro</exec_depend>", xml);
            Assert.Contains("<exec_depend>rviz2</exec_depend>", xml);
            Assert.Contains("<exec_depend>ros2_control</exec_depend>", xml);
            Assert.Contains("<exec_depend>ros2_controllers</exec_depend>", xml);
        }

        [Fact]
        public void Write_EmitsFormat3()
        {
            var xml = PackageXmlV3Writer.Write(Min());
            Assert.Contains("<package format=\"3\">", xml);
        }

        [Fact]
        public void Write_EmitsMetadata()
        {
            var xml = PackageXmlV3Writer.Write(new PackageXmlInput(
                "arm_2dof_description", "1.2.3", "My arm", "Aryan Arlikar", "a@b.com", "MIT"));
            Assert.Contains("<name>arm_2dof_description</name>", xml);
            Assert.Contains("<version>1.2.3</version>", xml);
            Assert.Contains("<description>My arm</description>", xml);
            Assert.Contains("<maintainer email=\"a@b.com\">Aryan Arlikar</maintainer>", xml);
            Assert.Contains("<license>MIT</license>", xml);
        }

        [Fact]
        public void Write_EmitsAmentCmakeBuildType()
        {
            var xml = PackageXmlV3Writer.Write(Min());
            Assert.Contains("<build_type>ament_cmake</build_type>", xml);
        }

        [Fact]
        public void Write_StartsWithXmlProlog()
        {
            var xml = PackageXmlV3Writer.Write(Min());
            Assert.StartsWith("<?xml", xml.TrimStart());
        }

        [Fact]
        public void Write_NullInput_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => PackageXmlV3Writer.Write(null));
        }

        [Fact]
        public void Write_NullPackageName_Throws()
        {
            Assert.Throws<System.ArgumentException>(() =>
                PackageXmlV3Writer.Write(new PackageXmlInput(null, "0.0.1", "d", "m", "e@x", "MIT")));
        }

        [Fact]
        public void Write_WhitespacePackageName_Throws()
        {
            Assert.Throws<System.ArgumentException>(() =>
                PackageXmlV3Writer.Write(new PackageXmlInput("  ", "0.0.1", "d", "m", "e@x", "MIT")));
        }
    }
}
