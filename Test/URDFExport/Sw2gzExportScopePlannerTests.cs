using System.Linq;
using SW2GZ.Build.Model;
using SW2GZ.URDFExport;
using Xunit;

namespace SW2GZ.Tests.URDFExport
{
    public class Sw2gzExportScopePlannerTests
    {
        private static LinkDef L(string name, string parent = "") =>
            new LinkDef { Name = name, ParentName = parent };

        [Fact]
        public void Robot_Counts_And_Files()
        {
            var doc = new Sw2gzDoc { Mode = Sw2gzMode.Robot };
            doc.Robot.Links.Add(L("base"));
            doc.Robot.Links.Add(L("arm", "base"));
            doc.Robot.Joints.Add(new JointDef { Name = "j1" });

            var scope = Sw2gzExportScopePlanner.Plan(doc, "C:/out", "Full Arm");

            Assert.Equal("Robot package (URDF/Xacro)", scope.ModeLabel);
            Assert.Equal(2, scope.LinkCount);
            Assert.Equal(1, scope.JointCount);
            Assert.Equal("full_arm", scope.PackageName);
            Assert.Contains("full_arm_ws", scope.WorkspaceRoot);
            Assert.Contains(scope.Files, f => f.Contains("package.xml"));
            Assert.Contains(scope.Files, f => f.Contains("urdf/full_arm.urdf.xacro"));
            Assert.Contains(scope.Files, f => f.Contains("gz_sim.launch.py"));
            Assert.Contains(scope.Files, f => f.Contains("meshes/"));
        }

        [Fact]
        public void World_Counts_Use_Assets()
        {
            var doc = new Sw2gzDoc { Mode = Sw2gzMode.World };
            doc.World.Ground = "ground<1>";
            doc.World.Assets.Add("tree<1>");
            doc.World.Assets.Add("rock<1>");

            var scope = Sw2gzExportScopePlanner.Plan(doc, "C:/out", "myworld");

            Assert.Equal("Gz world (SDF world)", scope.ModeLabel);
            Assert.Equal(2, scope.LinkCount);
            Assert.Contains(scope.Files, f => f.Contains("worlds/myworld.sdf"));
            Assert.Contains(scope.Files, f => f.Contains("models/ground"));
        }

        [Fact]
        public void Asset_Single_Body()
        {
            var doc = new Sw2gzDoc { Mode = Sw2gzMode.Asset };
            doc.Asset.BodyPart = "prop.SLDPRT";

            var scope = Sw2gzExportScopePlanner.Plan(doc, "C:/out", "prop");

            Assert.Equal("Gz asset (SDF model)", scope.ModeLabel);
            Assert.Contains(scope.Files, f => f.EndsWith("model.config"));
            Assert.Contains(scope.Files, f => f.EndsWith("model.sdf"));
            Assert.Contains(scope.Files, f => f.Contains("meshes/"));
        }

        [Fact]
        public void Sanitises_Bad_Package_Name()
        {
            var doc = new Sw2gzDoc();
            var scope = Sw2gzExportScopePlanner.Plan(doc, "C:/out", "MY ROBOT");
            // Sanitiser lowercases + replaces spaces with underscores; exact
            // output is owned by PackageNameSanitizer.Sanitise — only assert
            // it ran (no uppercase or whitespace left).
            Assert.DoesNotContain(" ", scope.PackageName);
            Assert.Equal(scope.PackageName, scope.PackageName.ToLowerInvariant());
        }
    }
}
