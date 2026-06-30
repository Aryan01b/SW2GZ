/*
Copyright (c) 2026 Aryan Arlikar. MIT License â€” see CONTRIBUTING.md.

Pure round-trip tests for the wizard checkpoint config + codec. No COM, so
these run in the net8 test project (the SW Attribute storage layer that wraps
this codec is COM-bound and lives in Sw2gzConfigSerialization, untested here).
*/
using SW2GZ.Ros2;
using SW2GZ.URDFExport;
using Xunit;

namespace SW2GZ.Test.URDFExport
{
    public class Sw2gzExportConfigTests
    {
        [Fact]
        public void RoundTrip_PreservesAllFields()
        {
            var config = new Sw2gzExportConfig
            {
                Mode = ExportMode.SdfWorld,
                OutputFolder = @"C:\out\robots",
                PackageName = "My Robot Pkg",
                Author = "Aryan Arlikar",
                Email = "aryan@example.com",
                License = "MIT",
                LastStep = 2,
            };

            string xml = Sw2gzConfigCodec.ToXmlString(config);
            Sw2gzExportConfig restored = Sw2gzConfigCodec.FromXmlString(xml);

            Assert.Equal(ExportMode.SdfWorld, restored.Mode);
            Assert.Equal(@"C:\out\robots", restored.OutputFolder);
            Assert.Equal("My Robot Pkg", restored.PackageName);
            Assert.Equal("Aryan Arlikar", restored.Author);
            Assert.Equal("aryan@example.com", restored.Email);
            Assert.Equal("MIT", restored.License);
            Assert.Equal(2, restored.LastStep);
        }

        [Fact]
        public void ToXmlString_ProducesNonEmptyXml()
        {
            string xml = Sw2gzConfigCodec.ToXmlString(new Sw2gzExportConfig());
            Assert.False(string.IsNullOrWhiteSpace(xml));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void FromXmlString_ReturnsNull_OnEmptyInput(string data)
        {
            Assert.Null(Sw2gzConfigCodec.FromXmlString(data));
        }

        [Fact]
        public void Defaults_AreRobotPackageAndEmptyStrings()
        {
            var config = new Sw2gzExportConfig();
            Assert.Equal(ExportMode.RobotPackage, config.Mode);
            Assert.Equal(string.Empty, config.OutputFolder);
            Assert.Equal(string.Empty, config.PackageName);
            Assert.Equal(0, config.LastStep);
        }
    }
}
