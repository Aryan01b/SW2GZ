/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System;
using System.IO;
using System.Linq;
using SW2GZ.Validate;
using Xunit;

namespace SW2GZ.Validate.Tests
{
    public class MeshFileCheckerTests
    {
        private static string MakeTempPkg()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"sw2gz_meshcheck_{Guid.NewGuid()}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public void Check_MissingMeshFile_EmitsMsh001()
        {
            var pkg = MakeTempPkg();
            try
            {
                var urdf = "<robot><link><visual><geometry><mesh filename=\"package://pkg/meshes/missing.dae\"/></geometry></visual></link></robot>";
                var iss = Assert.Single(MeshFileChecker.Check(urdf, pkg));
                Assert.Equal("MSH001", iss.Code);
                Assert.Equal(IssueSeverity.Error, iss.Severity);
                Assert.Contains("missing.dae", iss.Message);
            }
            finally { Directory.Delete(pkg, true); }
        }

        [Fact]
        public void Check_PresentMeshFile_NoIssues()
        {
            var pkg = MakeTempPkg();
            var meshesDir = Path.Combine(pkg, "meshes");
            Directory.CreateDirectory(meshesDir);
            File.WriteAllText(Path.Combine(meshesDir, "base_link.dae"), "<COLLADA/>");
            try
            {
                var urdf = "<robot><link><visual><geometry><mesh filename=\"package://pkg/meshes/base_link.dae\"/></geometry></visual></link></robot>";
                Assert.Empty(MeshFileChecker.Check(urdf, pkg));
            }
            finally { Directory.Delete(pkg, true); }
        }

        [Fact]
        public void Check_MultipleMeshesMixed_ReportsOnlyMissing()
        {
            var pkg = MakeTempPkg();
            Directory.CreateDirectory(Path.Combine(pkg, "meshes"));
            File.WriteAllText(Path.Combine(pkg, "meshes", "a.dae"), "x");
            try
            {
                var urdf =
                    "<robot>" +
                    "<link><visual><geometry><mesh filename=\"package://pkg/meshes/a.dae\"/></geometry></visual></link>" +
                    "<link><collision><geometry><mesh filename=\"package://pkg/meshes/missing.stl\"/></geometry></collision></link>" +
                    "</robot>";
                var issues = MeshFileChecker.Check(urdf, pkg).ToList();
                Assert.Single(issues);
                Assert.Contains("missing.stl", issues[0].Message);
            }
            finally { Directory.Delete(pkg, true); }
        }

        [Fact]
        public void Check_NoMeshElements_NoIssues()
        {
            var pkg = MakeTempPkg();
            try
            {
                var urdf = "<robot><link><visual><geometry><box size=\"1 1 1\"/></geometry></visual></link></robot>";
                Assert.Empty(MeshFileChecker.Check(urdf, pkg));
            }
            finally { Directory.Delete(pkg, true); }
        }

        [Fact]
        public void Check_NullUrdf_NoIssues()
        {
            var pkg = MakeTempPkg();
            try { Assert.Empty(MeshFileChecker.Check(null, pkg)); }
            finally { Directory.Delete(pkg, true); }
        }

        [Fact]
        public void Check_NullPackageRoot_Throws()
        {
            Assert.Throws<ArgumentException>(() => MeshFileChecker.Check("<robot/>", null));
        }

        [Fact]
        public void Check_RelativePathInUrl_ResolvesUnderPackageRoot()
        {
            var pkg = MakeTempPkg();
            Directory.CreateDirectory(Path.Combine(pkg, "meshes", "sub"));
            File.WriteAllText(Path.Combine(pkg, "meshes", "sub", "x.dae"), "x");
            try
            {
                var urdf = "<robot><link><visual><geometry><mesh filename=\"package://pkg/meshes/sub/x.dae\"/></geometry></visual></link></robot>";
                Assert.Empty(MeshFileChecker.Check(urdf, pkg));
            }
            finally { Directory.Delete(pkg, true); }
        }
    }
}
