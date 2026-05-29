/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Acceptance harness: one fact per known v1 export bug. Each fact runs
Sw2gzPipeline with Moq SW Surface and asserts that the emitted files
*/
using System;
using System.IO;
using System.Numerics;
using Moq;
using SW2GZ.Build;
using SW2GZ.Math;
using SW2GZ.SwSurface.Abstractions;
using SW2GZ.URDFExport;
using SW2GZ.Validate;
using Xunit;

namespace SW2GZ.Acceptance.Tests
{
    public class BugAcceptanceTests : IDisposable
    {
        private readonly string _tmp;

        public BugAcceptanceTests()
        {
            _tmp = Path.Combine(Path.GetTempPath(), "sw2gz_acc_" + Guid.NewGuid());
        }

        public void Dispose()
        {
            if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true);
        }

        private (string Root, ValidationReport Report, string SanitizedPkg) RunPipeline(string rawName = "good_pkg")
        {
            var mass = new Mock<IMassProperties>();
            mass.Setup(m => m.Get(It.IsAny<string>()))
                .Returns(new MassProps(1.0, Vector3.Zero, Matrix3.Identity));

            var walker = new Mock<IAssemblyWalker>();
            walker.Setup(w => w.WalkActive()).Returns(new[]
            {
                new LinkSpec("base_link", new[] { "/p/base.SLDPRT" }),
                new LinkSpec("arm1",      new[] { "/p/arm1.SLDPRT" }),
            });

            var tess = new Mock<IMeshTessellator>();
            tess.Setup(t => t.Tessellate(It.IsAny<string>(), It.IsAny<TessellationLod>()))
                .Returns(new MeshData(
                    new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY },
                    new[] { 0, 1, 2 }, null));

            var report = new Sw2gzPipeline(mass.Object, walker.Object, tess.Object)
                .Run(_tmp, rawName, "A", "a@b", "Apache-2.0");

            // v2.0 layout: <_tmp>/<pkg>_ws/src/<pkg>/. Sanitized name unknown until
            // we look at the workspace dir (<pkg>_ws ends in "_ws").
            string workspaceDir = Directory.GetDirectories(_tmp)[0];
            string srcDir = Path.Combine(workspaceDir, "src");
            string sanitizedPkgDir = Directory.GetDirectories(srcDir)[0];
            return (sanitizedPkgDir, report, Path.GetFileName(sanitizedPkgDir));
        }

        // --- Bug 1 - gz.xacro must not be empty placeholder ---------------

        [Fact]
        public void Bug01_GzXacro_IsNonEmptyAndDeclaresPluginBlock()
        {
            var (root, _, _) = RunPipeline();
            var content = File.ReadAllText(Path.Combine(root, "urdf", "inc", "gz.xacro"));
            Assert.Contains("<gazebo>", content);
            Assert.Contains("<plugin filename=\"gz_ros2_control-system\"", content);
            Assert.Contains("<parameters>", content);
        }

        // --- Bug 2 - package name sanitization (hyphens to underscores) ---

        [Fact]
        public void Bug02_HyphenatedPackageName_GetsSanitized()
        {
            var (root, _, pkg) = RunPipeline("arm-2dof_description");
            // ament regex: ^[a-z][a-z0-9_]*[a-z0-9]$
            Assert.DoesNotContain("-", pkg);
            Assert.True(Directory.Exists(root));
        }

        // --- Bug 3 - ros2_control.xacro uses literal $(find pkg), no $(arg pkg) ---

        [Fact]
        public void Bug03_Ros2ControlXacro_UsesLiteralFindPkg_NoArgPkg()
        {
            var (root, _, pkg) = RunPipeline();
            var content = File.ReadAllText(Path.Combine(root, "urdf", "inc", "ros2_control.xacro"));
            Assert.Contains($"$(find {pkg})/config/controllers.yaml", content);
            Assert.DoesNotContain("$(arg pkg)", content);
            Assert.DoesNotContain("find-pkg-share", content);
        }

        // --- Bug 4 - world uses unversioned Harmonic plugin filenames -----

        [Fact]
        public void Bug04_World_UsesUnversionedHarmonicPlugins()
        {
            var (root, _, _) = RunPipeline();
            var sdf = File.ReadAllText(Path.Combine(root, "worlds", "empty.sdf"));
            Assert.Contains("gz-sim-physics-system", sdf);
            Assert.DoesNotContain("gz-sim8-", sdf);
            Assert.DoesNotContain("gz-sim7-", sdf);
        }

        // --- Bug 5 - gz_sim.launch.py sets GZ_SIM_SYSTEM_PLUGIN_PATH -----

        [Fact]
        public void Bug05_GzSimLaunch_SetsSystemPluginPath()
        {
            var (root, _, _) = RunPipeline();
            var py = File.ReadAllText(Path.Combine(root, "launch", "gz_sim.launch.py"));
            Assert.Contains("GZ_SIM_SYSTEM_PLUGIN_PATH", py);
            Assert.Contains("get_package_prefix('gz_ros2_control')", py);
        }

        // --- Bug 6 - spawn -name matches bridge gz_topic_name ------------

        [Fact]
        public void Bug06_SpawnNameMatchesBridgeJointStateTopic()
        {
            var (root, _, pkg) = RunPipeline();
            var py = File.ReadAllText(Path.Combine(root, "launch", "gz_sim.launch.py"));
            var bridge = File.ReadAllText(Path.Combine(root, "config", "ros_gz_bridge.yaml"));
            Assert.Contains($"'-name', '{pkg}'", py);
            Assert.Contains($"/world/empty/model/{pkg}/joint_state", bridge);
        }

        // --- Bug 7 - parameter_bridge launched in gz_sim.launch.py -------

        [Fact]
        public void Bug07_GzSimLaunch_StartsParameterBridge()
        {
            var (root, _, _) = RunPipeline();
            var py = File.ReadAllText(Path.Combine(root, "launch", "gz_sim.launch.py"));
            Assert.Contains("parameter_bridge", py);
            Assert.Contains("ros_gz_bridge.yaml", py);
        }

        // --- Bug 8 - gz_ros2_control plugin uses GazeboSimROS2ControlPlugin ---

        [Fact]
        public void Bug08_GzPluginXacro_UsesCorrectClassName()
        {
            var (root, _, _) = RunPipeline();
            var content = File.ReadAllText(Path.Combine(root, "urdf", "inc", "gz.xacro"));
            Assert.Contains("gz_ros2_control::GazeboSimROS2ControlPlugin", content);
            Assert.DoesNotContain("gz_ros2_control::system\"", content);
        }

        // --- Bug 9 - every <geometry> has a child (mesh / box / etc.) ----

        [Fact]
        public void Bug09_EveryGeometryHasContent()
        {
            var (root, report, pkg) = RunPipeline();
            var urdf = File.ReadAllText(Path.Combine(root, "urdf", $"{pkg}.urdf.xacro"));
            // Sw2gzPipeline emits <mesh filename="package://pkg/meshes/<name>.dae"/> inside every visual + collision.
            Assert.Contains("<mesh filename=\"package://", urdf);
            // OutputValidator would have caught empty geometry. Confirm no URDF002 in report.
            Assert.DoesNotContain(report.Errors, e => e.Code == "URDF002");
            // Mesh files exist on disk
            Assert.True(File.Exists(Path.Combine(root, "meshes", "base_link.dae")));
            Assert.True(File.Exists(Path.Combine(root, "meshes", "base_link_collision.stl")));
        }

        // --- Bug 10 - joint warning when continuous + position interface --
        // The pipeline doesn't currently emit joints (deferred to v2.1). The warning
        // lives in JointBuilder.Build; here we verify the warning path exists by
        // exercising JointBuilder directly with a synthetic mate.

        [Fact]
        public void Bug10_JointBuilder_WarnsOnContinuousPositionInterface()
        {
            var mate = new MateSpec("j1", MateKind.Continuous,
                Pose.Identity, Vector3.UnitZ, null, null, 10, 1.0, SW2GZ.Build.Urdf.UrdfCmdInterface.Position);
            var parent = new SW2GZ.Build.Urdf.UrdfLink("a", 1, Vector3.Zero, Matrix3.Identity, null, null, "", "");
            var child  = new SW2GZ.Build.Urdf.UrdfLink("b", 1, Vector3.Zero, Matrix3.Identity, null, null, "", "");

            var (_, warnings) = JointBuilder.Build(mate, parent, child);
            Assert.Contains(warnings, w => w.Contains("continuous", StringComparison.OrdinalIgnoreCase)
                                        && w.Contains("position", StringComparison.OrdinalIgnoreCase));
        }

        // --- Bug 11 - composed launch: gz_sim.launch.py wires bridge + spawn ---

        [Fact]
        public void Bug11_GzSimLaunch_IsSelfContained()
        {
            var (root, _, _) = RunPipeline();
            var py = File.ReadAllText(Path.Combine(root, "launch", "gz_sim.launch.py"));
            // Self-contained: includes the gz sim include, the spawn node, the bridge, and the RSP.
            Assert.Contains("IncludeLaunchDescription", py);
            Assert.Contains("ros_gz_sim", py);
            Assert.Contains("create", py);                    // spawn executable
            Assert.Contains("parameter_bridge", py);
            Assert.Contains("robot_state_publisher", py);
        }
    }
}
