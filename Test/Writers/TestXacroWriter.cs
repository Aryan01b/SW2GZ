/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using SW2GZ.Ros2;
using Xunit;

namespace SW2GZ.Writers.Tests
{
    public class TestXacroWriter
    {
        private const string SampleBody =
            "<link name=\"base_link\"/>\n" +
            "<joint name=\"j1\" type=\"revolute\"/>\n";

        [Fact]
        public void Write_IncludesGzXacro()
        {
            var xml = XacroWriter.Write("r", SampleBody);
            Assert.Contains("<xacro:include filename=\"inc/gz.xacro\"/>", xml);
        }

        [Fact]
        public void Write_IncludesRos2ControlXacro()
        {
            var xml = XacroWriter.Write("r", SampleBody);
            Assert.Contains("<xacro:include filename=\"inc/ros2_control.xacro\"/>", xml);
        }

        [Fact]
        public void Write_IncludesMaterialsXacro()
        {
            var xml = XacroWriter.Write("r", SampleBody);
            Assert.Contains("<xacro:include filename=\"inc/materials.xacro\"/>", xml);
        }

        [Fact]
        public void Write_OpensRobotElementWithRobotNameAndXacroNamespace()
        {
            var xml = XacroWriter.Write("my_robot", SampleBody);
            Assert.Contains("<robot name=\"my_robot\" xmlns:xacro=\"http://www.ros.org/wiki/xacro\">", xml);
        }

        [Fact]
        public void Write_EmbedsTheBodyXml()
        {
            var xml = XacroWriter.Write("r", SampleBody);
            Assert.Contains("<link name=\"base_link\"/>", xml);
            Assert.Contains("<joint name=\"j1\" type=\"revolute\"/>", xml);
        }

        [Fact]
        public void Write_StripsEmptyNameMaterialTags_Minor()
        {
            var body =
                "<link name=\"l\"/>\n" +
                "<material name=\"\"/>\n" +
                "<material name=\"silver\"/>\n";
            var xml = XacroWriter.Write("r", body);
            Assert.DoesNotContain("<material name=\"\"/>", xml);
            Assert.Contains("<material name=\"silver\"/>", xml);
        }

        [Fact]
        public void Write_StripsEmptyNameMaterialTags_WithWhitespace()
        {
            var body = "<material name=\"   \"/>\n<material name=\"ok\"/>\n";
            var xml = XacroWriter.Write("r", body);
            Assert.DoesNotContain("name=\"   \"", xml);
            Assert.Contains("<material name=\"ok\"/>", xml);
        }

        [Fact]
        public void Write_StartsWithXmlProlog()
        {
            var xml = XacroWriter.Write("r", SampleBody);
            Assert.StartsWith("<?xml version=\"1.0\"?>", xml.TrimStart());
        }

        [Fact]
        public void Write_ClosesRobotElement()
        {
            var xml = XacroWriter.Write("r", SampleBody);
            Assert.Contains("</robot>", xml);
        }

        [Fact]
        public void Write_NullRobotName_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => XacroWriter.Write(null, SampleBody));
        }

        [Fact]
        public void Write_WhitespaceRobotName_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => XacroWriter.Write("  ", SampleBody));
        }

        [Fact]
        public void Write_NullBody_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => XacroWriter.Write("r", null));
        }
    }
}
