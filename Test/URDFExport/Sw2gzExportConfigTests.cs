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
                Type = SW2GZ.Build.Urdf.UrdfJointType.Revolute,
                MateName = "Concentric1",
                AxisX = 0, AxisY = 1, AxisZ = 0,
                LimitLower = -1.5,
                LimitUpper = 1.5,
            });

            string xml = Sw2gzConfigCodec.ToXmlString(config);
            Sw2gzExportConfig restored = Sw2gzConfigCodec.FromXmlString(xml);

            Assert.Single(restored.Joints);
            Assert.Equal("wheel_left_joint", restored.Joints[0].Name);
            Assert.Equal("base_link", restored.Joints[0].ParentLink);
            Assert.Equal("wheel_left", restored.Joints[0].ChildLink);
            Assert.Equal(SW2GZ.Build.Urdf.UrdfJointType.Revolute, restored.Joints[0].Type);
            Assert.Equal("Concentric1", restored.Joints[0].MateName);
            Assert.Equal(1.0, restored.Joints[0].AxisY, 5);
            Assert.Equal(-1.5, restored.Joints[0].LimitLower);
            Assert.Equal(1.5, restored.Joints[0].LimitUpper);
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

        [Fact]
        public void RoundTrip_PreservesStackProfile()
        {
            var config = new Sw2gzExportConfig
            {
                Stacks = new SW2GZ.Ros2.StackProfile
                {
                    GzSim = true,
                    Actuation = SW2GZ.Ros2.ActuationBackend.GzPlugin,
                    SensorsEnabled = true,
                },
            };

            string xml = Sw2gzConfigCodec.ToXmlString(config);
            Sw2gzExportConfig restored = Sw2gzConfigCodec.FromXmlString(xml);

            Assert.NotNull(restored.Stacks);
            Assert.True(restored.Stacks.GzSim);
            Assert.Equal(SW2GZ.Ros2.ActuationBackend.GzPlugin, restored.Stacks.Actuation);
            Assert.True(restored.Stacks.SensorsEnabled);
        }

        [Fact]
        public void Default_StacksIsFullStack()
        {
            // A fresh config must default to the full stack so unconfigured assemblies
            // export exactly as before this refactor.
            var config = new Sw2gzExportConfig();
            Assert.Equal(SW2GZ.Ros2.ActuationBackend.Ros2Control, config.Stacks.Actuation);
        }

        [Fact]
        public void Deserialize_LegacyXmlWithoutStacks_DefaultsToFullStack()
        {
            // Simulates a config saved before the Stacks field existed: the <Stacks>
            // element is absent. DataContractSerializer skips ctor/initializers, so this
            // is the case that must still yield a non-null full-stack default.
            string legacyXml =
                "<Sw2gzExportConfig xmlns=\"\" xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                "<PackageName>legacy_bot</PackageName></Sw2gzExportConfig>";

            Sw2gzExportConfig restored = Sw2gzConfigCodec.FromXmlString(legacyXml);

            Assert.NotNull(restored.Stacks);
            Assert.Equal(SW2GZ.Ros2.ActuationBackend.Ros2Control, restored.Stacks.Actuation);
            Assert.True(restored.Stacks.GzSim);
        }
    }
}
