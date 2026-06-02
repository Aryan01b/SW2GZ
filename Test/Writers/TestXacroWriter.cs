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

        // Fix 3: the empty-name <material> stripping regex was removed as dead code.
        // UrdfSerializer no longer emits empty-name materials (names are sanitized
        // to non-empty and the element is only written when MaterialName != null),
        // so the body XML is now passed through verbatim. The writer must NOT alter
        // material tags in the body.
        [Fact]
        public void Write_PassesMaterialTagsThroughVerbatim()
        {
            var body =
                "<link name=\"l\"/>\n" +
                "<material name=\"silver\"/>\n";
            var xml = XacroWriter.Write("r", body);
            Assert.Contains("<material name=\"silver\"/>", xml);
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
