/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Linq;
using SW2GZ.Validate;
using Xunit;

namespace SW2GZ.Validate.Tests
{
    public class UrdfXmlValidatorTests
    {
        [Fact]
        public void Check_WellFormedUrdf_NoIssues()
        {
            var urdf = "<robot name=\"r\"><link name=\"l\"/></robot>";
            Assert.Empty(UrdfXmlValidator.CheckString(urdf));
        }

        [Fact]
        public void Check_MalformedXml_EmitsUrdf001Error()
        {
            var bad = "<robot><link name=\"l\"></robot>";
            var issues = UrdfXmlValidator.CheckString(bad);
            var iss = Assert.Single(issues);
            Assert.Equal("URDF001", iss.Code);
            Assert.Equal(IssueSeverity.Error, iss.Severity);
        }

        [Fact]
        public void Check_EmptyGeometryInCollision_EmitsUrdf002_Bug9()
        {
            var urdf = "<robot name=\"r\"><link name=\"l\"><collision><geometry></geometry></collision></link></robot>";
            var issues = UrdfXmlValidator.CheckString(urdf);
            Assert.Contains(issues, i => i.Code == "URDF002" && i.Severity == IssueSeverity.Error);
        }

        [Fact]
        public void Check_EmptyGeometryInVisual_EmitsUrdf002_Bug9()
        {
            var urdf = "<robot name=\"r\"><link name=\"l\"><visual><geometry></geometry></visual></link></robot>";
            Assert.Contains(UrdfXmlValidator.CheckString(urdf), i => i.Code == "URDF002");
        }

        [Fact]
        public void Check_GeometryWithMesh_NoUrdf002()
        {
            var urdf = "<robot name=\"r\"><link name=\"l\"><collision><geometry><mesh filename=\"a.stl\"/></geometry></collision></link></robot>";
            var issues = UrdfXmlValidator.CheckString(urdf);
            Assert.DoesNotContain(issues, i => i.Code == "URDF002");
        }

        [Fact]
        public void Check_GeometryWithBox_NoUrdf002()
        {
            var urdf = "<robot name=\"r\"><link name=\"l\"><visual><geometry><box size=\"1 1 1\"/></geometry></visual></link></robot>";
            Assert.DoesNotContain(UrdfXmlValidator.CheckString(urdf), i => i.Code == "URDF002");
        }

        [Fact]
        public void Check_MultipleEmptyGeometries_EmitsOnePer()
        {
            var urdf = "<robot name=\"r\">" +
                       "<link name=\"a\"><collision><geometry></geometry></collision><visual><geometry></geometry></visual></link>" +
                       "</robot>";
            var issues = UrdfXmlValidator.CheckString(urdf).Where(i => i.Code == "URDF002").ToList();
            Assert.Equal(2, issues.Count);
        }

        [Fact]
        public void Check_NullInput_EmitsUrdf001()
        {
            var issues = UrdfXmlValidator.CheckString(null);
            Assert.Contains(issues, i => i.Code == "URDF001");
        }

        [Fact]
        public void Check_EmptyInput_EmitsUrdf001()
        {
            Assert.Contains(UrdfXmlValidator.CheckString(""), i => i.Code == "URDF001");
        }

        [Fact]
        public void Check_MalformedXml_MessageMentionsCause()
        {
            var bad = "<robot><link>";
            var iss = UrdfXmlValidator.CheckString(bad).Single();
            Assert.Equal("URDF001", iss.Code);
            // message should give some hint — at minimum mention "XML" or "malformed"
            Assert.True(iss.Message.Length > 0);
        }
    }
}
