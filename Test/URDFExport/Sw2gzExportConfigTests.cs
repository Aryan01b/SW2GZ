/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

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
            Assert.Empty(config.Links);
        }

        [Fact]
        public void RoundTrip_PreservesJoints()
        {
            var config = new Sw2gzExportConfig();
            config.Joints.Add(new SW2GZ.Build.Model.JointDef
            {
                Name = "wheel_left_joint",
                ParentLink = "base_link",
                ChildLink = "wheel_left",
                Type = SW2GZ.Build.Urdf.UrdfJointType.Continuous,
                Axis = SW2GZ.Build.Model.JointAxisPreset.PlusY,
                LimitEffort = 50,
                LimitVelocity = 3,
                Interface = SW2GZ.Build.Urdf.UrdfCmdInterface.Velocity,
            });

            string xml = Sw2gzConfigCodec.ToXmlString(config);
            Sw2gzExportConfig restored = Sw2gzConfigCodec.FromXmlString(xml);

            Assert.Single(restored.Joints);
            Assert.Equal("wheel_left_joint", restored.Joints[0].Name);
            Assert.Equal("base_link", restored.Joints[0].ParentLink);
            Assert.Equal("wheel_left", restored.Joints[0].ChildLink);
            Assert.Equal(SW2GZ.Build.Urdf.UrdfJointType.Continuous, restored.Joints[0].Type);
            Assert.Equal(SW2GZ.Build.Model.JointAxisPreset.PlusY, restored.Joints[0].Axis);
            Assert.Equal(50, restored.Joints[0].LimitEffort);
            Assert.Equal(SW2GZ.Build.Urdf.UrdfCmdInterface.Velocity, restored.Joints[0].Interface);
        }

        [Fact]
        public void RoundTrip_PreservesLinks()
        {
            var config = new Sw2gzExportConfig();
            config.Links.Add(new SW2GZ.Build.Model.LinkDef
            {
                Name = "base_link",
                ParentName = "",
                ComponentIds = { "chassis-1@robot", "motor-1@robot" },
            });
            config.Links.Add(new SW2GZ.Build.Model.LinkDef { Name = "wheel_left", ParentName = "base_link" });

            string xml = Sw2gzConfigCodec.ToXmlString(config);
            Sw2gzExportConfig restored = Sw2gzConfigCodec.FromXmlString(xml);

            Assert.Equal(2, restored.Links.Count);
            Assert.Equal("base_link", restored.Links[0].Name);
            Assert.Equal("", restored.Links[0].ParentName);
            Assert.Equal(2, restored.Links[0].ComponentIds.Count);
            Assert.Equal("motor-1@robot", restored.Links[0].ComponentIds[1]);
            Assert.Equal("base_link", restored.Links[1].ParentName);
        }
    }
}
