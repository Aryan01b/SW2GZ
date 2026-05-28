/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using SW2GZ.Ros2;
using Xunit;

namespace SW2GZ.Test.Writers
{
    public class TestXacroWriter : WriterTestBase
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void WritesXacroAndIncludeFolder()
        {
            new XacroWriter("my_robot", urdfBodyXml: "<link name=\"base_link\"/>").Write(TempDir);
            Assert.True(Exists("my_robot.urdf.xacro"));
            Assert.True(Exists("inc/materials.xacro"));
            Assert.True(Exists("inc/ros2_control.xacro"));
            Assert.True(Exists("inc/gz.xacro"));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void XacroRootIncludesPrefixArgAndIncludes()
        {
            new XacroWriter("my_robot", urdfBodyXml: "<link name=\"base_link\"/>").Write(TempDir);
            var txt = ReadAllText("my_robot.urdf.xacro");
            Assert.Contains("xmlns:xacro=\"http://www.ros.org/wiki/xacro\"", txt);
            Assert.Contains("<xacro:arg name=\"prefix\" default=\"\"/>", txt);
            Assert.Contains("<xacro:arg name=\"use_sim\" default=\"false\"/>", txt);
            Assert.Contains("<xacro:include filename=\"inc/materials.xacro\"/>", txt);
            Assert.Contains("<xacro:include filename=\"inc/ros2_control.xacro\"/>", txt);
            Assert.Contains("<xacro:include filename=\"inc/gz.xacro\"/>", txt);
            Assert.Contains("<link name=\"base_link\"/>", txt);
        }
    }
}
