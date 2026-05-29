/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.IO;
using SW2GZ.Validate;
using Xunit;

namespace SW2GZ.Validate.Tests
{
    public class OutputValidatorTests
    {
        [Fact]
        public void Run_GoodPackage_NoErrors()
        {
            var dir = SyntheticPackage.GoodMinimal();
            try
            {
                var report = new OutputValidator().Run(dir, "good_pkg");
                Assert.False(report.HasErrors);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Run_BadPackageName_EmitsPkg001()
        {
            var dir = SyntheticPackage.GoodMinimal();
            try
            {
                var report = new OutputValidator().Run(dir, "Bad-Name");
                Assert.Contains(report.Errors, i => i.Code == "PKG001");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Run_MissingMesh_EmitsMsh001()
        {
            var dir = SyntheticPackage.GoodMinimal();
            try
            {
                File.Delete(Path.Combine(dir, "meshes", "base_link.dae"));
                var report = new OutputValidator().Run(dir, "good_pkg");
                Assert.Contains(report.Errors, i => i.Code == "MSH001");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Run_GardenVersionedPlugin_EmitsPlg001()
        {
            var dir = SyntheticPackage.GoodMinimal();
            try
            {
                string urdfPath = Path.Combine(dir, "urdf", "good_pkg.urdf.xacro");
                var contents = File.ReadAllText(urdfPath).Replace("gz-sim-physics-system", "gz-sim8-physics-system");
                File.WriteAllText(urdfPath, contents);
                var report = new OutputValidator().Run(dir, "good_pkg");
                Assert.Contains(report.Errors, i => i.Code == "PLG001");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Run_WrongRos2ControlClassName_EmitsPlg002()
        {
            var dir = SyntheticPackage.GoodMinimal();
            try
            {
                string urdfPath = Path.Combine(dir, "urdf", "good_pkg.urdf.xacro");
                var contents = File.ReadAllText(urdfPath).Replace("gz_ros2_control::GazeboSimROS2ControlPlugin", "gz_ros2_control::system");
                File.WriteAllText(urdfPath, contents);
                var report = new OutputValidator().Run(dir, "good_pkg");
                Assert.Contains(report.Errors, i => i.Code == "PLG002");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Run_MalformedUrdf_EmitsUrdf001()
        {
            var dir = SyntheticPackage.GoodMinimal();
            try
            {
                string urdfPath = Path.Combine(dir, "urdf", "good_pkg.urdf.xacro");
                File.WriteAllText(urdfPath, "<robot><link></robot>");
                var report = new OutputValidator().Run(dir, "good_pkg");
                Assert.Contains(report.Errors, i => i.Code == "URDF001");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Run_EmptyGeometry_EmitsUrdf002_Bug9()
        {
            var dir = SyntheticPackage.GoodMinimal();
            try
            {
                string urdfPath = Path.Combine(dir, "urdf", "good_pkg.urdf.xacro");
                var contents = "<robot name=\"good_pkg\"><link><visual><geometry></geometry></visual></link></robot>";
                File.WriteAllText(urdfPath, contents);
                var report = new OutputValidator().Run(dir, "good_pkg");
                Assert.Contains(report.Errors, i => i.Code == "URDF002");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Run_MissingUrdfFile_SkipsUrdfChecksButStillFlagsPackageName()
        {
            // No urdf/<name>.urdf.xacro on disk — only package name check runs.
            var dir = Path.Combine(Path.GetTempPath(), "sw2gz_empty_" + System.Guid.NewGuid());
            Directory.CreateDirectory(dir);
            try
            {
                var report = new OutputValidator().Run(dir, "Bad-Name");
                Assert.Contains(report.Errors, i => i.Code == "PKG001");
                Assert.DoesNotContain(report.Errors, i => i.Code == "URDF001");
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
