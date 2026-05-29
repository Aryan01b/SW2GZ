/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SW2GZ.Ros2;
using SW2GZ.URDFExport;
using SW2GZ.Test.Writers;
using Xunit;

namespace SW2GZ.Test.Golden
{
    public class TestGoldenRobotPackage : WriterTestBase
    {
        private static readonly string GoldenRoot =
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Golden", "expected"));

        private static readonly bool UpdateMode =
            Environment.GetEnvironmentVariable("SW2GZ_UPDATE_GOLDENS") == "1";

        [Fact]
        [Trait("Category", "Unit")]
        public void GoldenRoundTrip_HarmonicJazzy()
        {
            // v2.0 lock: single distro/gz pairing (Jazzy + Harmonic). Fortress/Ionic combos removed.
            new Ros2Package(new Ros2Package.Options
            {
                PackageName = "three_dof_arm_description",
                Maintainer = "aryan",
                MaintainerEmail = "aryan@example.com",
                License = "Apache-2.0",
                JointNames = new List<string> { "shoulder", "elbow", "wrist" },
                Profile = new TargetProfile { Mode = ExportMode.RobotPackage },
                UrdfBodyXml = "<link name=\"base_link\"/>",
            }).Write(TempDir);

            string expectedDir = Path.Combine(GoldenRoot, "harmonic_jazzy");
            if (UpdateMode)
            {
                if (Directory.Exists(expectedDir)) Directory.Delete(expectedDir, recursive: true);
                CopyDir(TempDir, expectedDir);
                return;
            }

            Assert.True(Directory.Exists(expectedDir),
                $"Golden dir missing: {expectedDir}. Run with SW2GZ_UPDATE_GOLDENS=1 to populate.");
            AssertDirsMatch(expectedDir, TempDir);
        }

        private static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(from.Length).TrimStart(Path.DirectorySeparatorChar);
                string dst = Path.Combine(to, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(f, dst, overwrite: true);
            }
        }

        private static void AssertDirsMatch(string expected, string actual)
        {
            var expectedFiles = Directory.EnumerateFiles(expected, "*", SearchOption.AllDirectories)
                .Select(f => f.Substring(expected.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/'))
                .OrderBy(s => s).ToList();
            var actualFiles = Directory.EnumerateFiles(actual, "*", SearchOption.AllDirectories)
                .Select(f => f.Substring(actual.Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/'))
                .OrderBy(s => s).ToList();
            Assert.Equal(expectedFiles, actualFiles);
            foreach (var rel in expectedFiles)
            {
                var e = File.ReadAllText(Path.Combine(expected, rel.Replace('/', Path.DirectorySeparatorChar))).Replace("\r\n", "\n");
                var a = File.ReadAllText(Path.Combine(actual,   rel.Replace('/', Path.DirectorySeparatorChar))).Replace("\r\n", "\n");
                Assert.True(e == a, $"Golden mismatch in {rel}");
            }
        }
    }
}
