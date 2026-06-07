using System.Collections.Generic;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Ros2;
using SW2GZ.URDFExport;
using Xunit;

namespace SW2GZ.Tests.URDFExport
{
    public class Sw2gzDocToExportConfigTests
    {
        [Theory]
        [InlineData(Sw2gzMode.Robot, ExportMode.RobotPackage)]
        [InlineData(Sw2gzMode.World, ExportMode.SdfWorld)]
        [InlineData(Sw2gzMode.Asset, ExportMode.SdfModel)]
        public void Maps_Mode(Sw2gzMode src, ExportMode dst)
        {
            Assert.Equal(dst, Sw2gzDocToExportConfig.MapMode(src));
        }

        [Fact]
        public void Bridge_Copies_Meta_And_LinksJoints()
        {
            var doc = new Sw2gzDoc { Mode = Sw2gzMode.Robot };
            doc.Robot.Links.Add(new LinkDef
            {
                Name = "base", ParentName = "",
                ComponentIds = new List<string> { "base<1>" },
            });
            doc.Robot.Joints.Add(new JointDef
            {
                Name = "j1", ParentLink = "base", ChildLink = "arm",
                Type = UrdfJointType.Revolute, MateName = "Concentric1",
                AxisX = 1.0, LimitLower = -1.0, LimitUpper = 1.0,
            });
            var meta = new ExportMetaInput
            {
                OutputFolder = "C:/out", PackageName = "full_arm",
                Author = "Aryan", Email = "a@b", License = "MIT",
            };

            var cfg = Sw2gzDocToExportConfig.Bridge(doc, meta);

            Assert.Equal(ExportMode.RobotPackage, cfg.Mode);
            Assert.Equal("C:/out", cfg.OutputFolder);
            Assert.Equal("full_arm", cfg.PackageName);
            Assert.Equal("Aryan", cfg.Author);
            Assert.Equal("a@b", cfg.Email);
            Assert.Equal("MIT", cfg.License);
            Assert.Single(cfg.Links);
            Assert.Equal("base", cfg.Links[0].Name);
            Assert.Single(cfg.Joints);
            Assert.Equal(UrdfJointType.Revolute, cfg.Joints[0].Type);
            Assert.Equal(-1.0, cfg.Joints[0].LimitLower);
            // Defensive clone — mutating source must not change bridged copy.
            doc.Robot.Links[0].ComponentIds.Add("late<1>");
            Assert.DoesNotContain("late<1>", cfg.Links[0].ComponentIds);
        }

        [Fact]
        public void Bridge_Null_Meta_Tolerated()
        {
            var doc = new Sw2gzDoc();
            var cfg = Sw2gzDocToExportConfig.Bridge(doc, null);
            Assert.Equal("", cfg.OutputFolder);
            Assert.Empty(cfg.Links);
        }
    }
}
