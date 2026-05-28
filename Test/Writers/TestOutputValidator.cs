/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.IO;
using SW2GZ.URDFExport;
using Xunit;

namespace SW2GZ.Test.Writers
{
    public class TestOutputValidator : WriterTestBase
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void XmlWellFormednessPassesForValidFile()
        {
            File.WriteAllText(Path.Combine(TempDir, "ok.xml"), "<root><a/></root>");
            var result = OutputValidator.ValidateXmlWellFormedness(Path.Combine(TempDir, "ok.xml"));
            Assert.True(result.Ok);
            Assert.Empty(result.Errors);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void XmlWellFormednessFailsForMalformed()
        {
            File.WriteAllText(Path.Combine(TempDir, "bad.xml"), "<root><a></root>");
            var result = OutputValidator.ValidateXmlWellFormedness(Path.Combine(TempDir, "bad.xml"));
            Assert.False(result.Ok);
            Assert.NotEmpty(result.Errors);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ValidateDirectoryFindsErrorsInUrdfAndSdf()
        {
            File.WriteAllText(Path.Combine(TempDir, "good.urdf"), "<robot><link/></robot>");
            File.WriteAllText(Path.Combine(TempDir, "bad.sdf"), "<sdf><model></sdf>");
            var result = OutputValidator.ValidateDirectoryXml(TempDir);
            Assert.False(result.Ok);
            Assert.Single(result.Errors);
            Assert.Contains("bad.sdf", result.Errors[0]);
        }
    }
}
