/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Collections.Generic;
using SW2GZ.Ros2;
using SW2GZ.URDFExport;
using Xunit;

namespace SW2GZ.Test.Writers
{
    public class TestRos2Package : WriterTestBase
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void WritesFullPackageTree()
        {
            new Ros2Package(new Ros2Package.Options
            {
                PackageName = "test_robot_description",
                JointNames = new List<string> { "j1" },
                Profile = new TargetProfile(),
                UrdfBodyXml = "<link name=\"base_link\"/>",
            }).Write(TempDir);

            Assert.True(Exists("package.xml"));
            Assert.True(Exists("CMakeLists.txt"));
            Assert.True(Exists("README.md"));
            Assert.True(Exists(".gitignore"));
            Assert.True(Exists("urdf/test_robot_description.urdf.xacro"));
            Assert.True(Exists("urdf/inc/ros2_control.xacro"));
            Assert.True(Exists("urdf/inc/gz.xacro"));
            Assert.True(Exists("config/controllers.yaml"));
            Assert.True(Exists("config/ros_gz_bridge.yaml"));
            Assert.True(Exists("config/rviz.rviz"));
            Assert.True(Exists("launch/display.launch.py"));
            Assert.True(Exists("launch/gz_sim.launch.py"));
            Assert.True(Exists("launch/ros2_control.launch.py"));
            Assert.True(Exists("worlds/empty.sdf"));
            Assert.True(Exists("meshes/visual"));
            Assert.True(Exists("meshes/collision"));
        }
    }
}
