/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System;
using System.IO;
using System.Numerics;
using Moq;
using SW2GZ.Build;
using SW2GZ.Build.Urdf;
using SW2GZ.Exceptions;
using SW2GZ.Math;
using SW2GZ.SwSurface.Abstractions;
using SW2GZ.URDFExport;
using Xunit;

namespace SW2GZ.Integration.Tests
{
    public class Sw2gzPipelineTests
    {
        private static MeshData TinyMesh()
        {
            // unit tetrahedron centered near the origin
            var verts = new[]
            {
                new Vector3(0, 0, 0),
                new Vector3(1, 0, 0),
                new Vector3(0, 1, 0),
                new Vector3(0, 0, 1),
            };
            var tris = new int[] { 0, 2, 1,   0, 1, 3,   0, 3, 2,   1, 2, 3 };
            return new MeshData(verts, tris, null);
        }

        [Fact]
        public void Run_TwoLinks_WritesFullPackageTree()
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
            walker.Setup(w => w.WalkMates()).Returns(Array.Empty<MateSpec>());

            var tess = new Mock<IMeshTessellator>();
            tess.Setup(t => t.Tessellate(It.IsAny<string>(), It.IsAny<TessellationLod>()))
                .Returns(TinyMesh());

            var tmp = Path.Combine(Path.GetTempPath(), "sw2gz_pipe_" + Guid.NewGuid());
            try
            {
                var report = new Sw2gzPipeline(mass.Object, walker.Object, tess.Object)
                    .Run(tmp, "test_pkg", "A", "a@b", "Apache-2.0");

                // v2.0 layout: <tmp>/<pkg>_ws/src/<pkg>/...
                string workspaceDir = Path.Combine(tmp, "test_pkg_ws");
                string root = Path.Combine(workspaceDir, "src", "test_pkg");
                Assert.False(report.HasErrors, string.Join("; ", System.Linq.Enumerable.Select(report.Errors, e => e.Code + " " + e.Message)));

                Assert.True(Directory.Exists(workspaceDir), "workspace root <pkg>_ws missing");
                Assert.True(Directory.Exists(Path.Combine(workspaceDir, "src")), "<pkg>_ws/src missing");
                Assert.True(File.Exists(Path.Combine(root, "package.xml")));
                Assert.True(File.Exists(Path.Combine(root, "CMakeLists.txt")));
                Assert.True(File.Exists(Path.Combine(root, "urdf", "test_pkg.urdf.xacro")));
                Assert.True(File.Exists(Path.Combine(root, "urdf", "inc", "gz.xacro")));
                Assert.True(File.Exists(Path.Combine(root, "urdf", "inc", "ros2_control.xacro")));
                Assert.True(File.Exists(Path.Combine(root, "urdf", "inc", "materials.xacro")));
                Assert.True(File.Exists(Path.Combine(root, "worlds", "empty.sdf")));
                Assert.True(File.Exists(Path.Combine(root, "launch", "gz_sim.launch.py")));
                Assert.True(File.Exists(Path.Combine(root, "launch", "display.launch.py")));
                Assert.True(File.Exists(Path.Combine(root, "launch", "ros2_control.launch.py")));
                Assert.True(File.Exists(Path.Combine(root, "config", "controllers.yaml")));
                Assert.True(File.Exists(Path.Combine(root, "config", "ros_gz_bridge.yaml")));
                Assert.True(File.Exists(Path.Combine(root, "meshes", "base_link.dae")));
                Assert.True(File.Exists(Path.Combine(root, "meshes", "base_link_collision.stl")));
                Assert.True(File.Exists(Path.Combine(root, "meshes", "arm1.dae")));
                Assert.True(File.Exists(Path.Combine(root, "meshes", "arm1_collision.stl")));
            }
            finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
        }

        [Fact]
        public void Run_ModelOnly_OmitsControlAndPluginFiles()
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
            walker.Setup(w => w.WalkMates()).Returns(new[]
            {
                new MateSpec("shoulder", MateKind.Revolute, Pose.Identity, Vector3.UnitZ,
                    -1.0, 1.0, 0, 0, UrdfCmdInterface.Position, "base_link", "arm1"),
            });

            var tess = new Mock<IMeshTessellator>();
            tess.Setup(t => t.Tessellate(It.IsAny<string>(), It.IsAny<TessellationLod>()))
                .Returns(TinyMesh());

            var tmp = Path.Combine(Path.GetTempPath(), "sw2gz_pipe_" + Guid.NewGuid());
            try
            {
                var report = new Sw2gzPipeline(mass.Object, walker.Object, tess.Object)
                    .Run(tmp, "model_pkg", "A", "a@b", "MIT",
                         Array.Empty<SW2GZ.Build.Model.SensorDef>(), modelOnly: true);
                Assert.False(report.HasErrors,
                    string.Join("; ", System.Linq.Enumerable.Select(report.Errors, e => e.Code + " " + e.Message)));

                string root = Path.Combine(tmp, "model_pkg_ws", "src", "model_pkg");

                // Present: the bare model + spawn launch.
                Assert.True(File.Exists(Path.Combine(root, "urdf", "model_pkg.urdf.xacro")));
                Assert.True(File.Exists(Path.Combine(root, "urdf", "inc", "materials.xacro")));
                Assert.True(File.Exists(Path.Combine(root, "launch", "gz_sim.launch.py")));
                Assert.True(File.Exists(Path.Combine(root, "launch", "display.launch.py")));
                Assert.True(File.Exists(Path.Combine(root, "meshes", "base_link.dae")));

                // Absent: all control + gz-plugin scaffolding.
                Assert.False(File.Exists(Path.Combine(root, "urdf", "inc", "ros2_control.xacro")));
                Assert.False(File.Exists(Path.Combine(root, "urdf", "inc", "gz.xacro")));
                Assert.False(File.Exists(Path.Combine(root, "launch", "ros2_control.launch.py")));
                Assert.False(File.Exists(Path.Combine(root, "config", "controllers.yaml")));
                Assert.False(File.Exists(Path.Combine(root, "config", "ros_gz_bridge.yaml")));

                // The xacro must not reference the dropped includes, and the joint
                // still appears (the body is unchanged).
                string xacro = File.ReadAllText(Path.Combine(root, "urdf", "model_pkg.urdf.xacro"));
                Assert.DoesNotContain("ros2_control.xacro", xacro);
                Assert.DoesNotContain("gz.xacro", xacro);
                Assert.Contains("<joint name=\"shoulder\" type=\"revolute\">", xacro);

                // The spawn launch must not load the ros2_control system plugin.
                string gzLaunch = File.ReadAllText(Path.Combine(root, "launch", "gz_sim.launch.py"));
                Assert.DoesNotContain("gz_ros2_control", gzLaunch);
                Assert.Contains("ros_gz_sim", gzLaunch);
            }
            finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
        }

        [Fact]
        public void Run_LinkWithoutMaterial_ThrowsBeforeWriting()
        {
            var mass = new Mock<IMassProperties>();
            mass.Setup(m => m.Get("/p/bad")).Throws(new MaterialMissingException("bad"));

            var walker = new Mock<IAssemblyWalker>();
            walker.Setup(w => w.WalkActive()).Returns(new[] { new LinkSpec("bad", new[] { "/p/bad" }) });
            walker.Setup(w => w.WalkMates()).Returns(Array.Empty<MateSpec>());

            var tess = new Mock<IMeshTessellator>();

            var tmp = Path.Combine(Path.GetTempPath(), "sw2gz_pipe_" + Guid.NewGuid());
            Assert.Throws<MaterialMissingException>(() =>
                new Sw2gzPipeline(mass.Object, walker.Object, tess.Object)
                    .Run(tmp, "bad_pkg", "A", "a@b", "MIT"));

            Assert.False(Directory.Exists(tmp), "No files should be written when pre-export fails.");
        }

        [Fact]
        public void Run_SanitizesPackageName()
        {
            var mass = new Mock<IMassProperties>();
            mass.Setup(m => m.Get(It.IsAny<string>())).Returns(new MassProps(1.0, Vector3.Zero, Matrix3.Identity));
            var walker = new Mock<IAssemblyWalker>();
            walker.Setup(w => w.WalkActive()).Returns(new[] { new LinkSpec("base", new[] { "/p/b.SLDPRT" }) });
            walker.Setup(w => w.WalkMates()).Returns(Array.Empty<MateSpec>());
            var tess = new Mock<IMeshTessellator>();
            tess.Setup(t => t.Tessellate(It.IsAny<string>(), It.IsAny<TessellationLod>())).Returns(TinyMesh());

            var tmp = Path.Combine(Path.GetTempPath(), "sw2gz_pipe_" + Guid.NewGuid());
            try
            {
                new Sw2gzPipeline(mass.Object, walker.Object, tess.Object)
                    .Run(tmp, "Bad-Name", "A", "a@b", "MIT");

                // sanitized to "bad_name"; v2.0 layout = <tmp>/bad_name_ws/src/bad_name/
                Assert.True(Directory.Exists(Path.Combine(tmp, "bad_name_ws", "src", "bad_name")));
                Assert.False(Directory.Exists(Path.Combine(tmp, "Bad-Name_ws")));
            }
            finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
        }

        [Fact]
        public void Run_RevoluteMate_EmitsJointInUrdfAndControllersYaml()
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
            walker.Setup(w => w.WalkMates()).Returns(new[]
            {
                new MateSpec("shoulder", MateKind.Revolute, Pose.Identity, Vector3.UnitZ,
                    -1.0, 1.0, 10, 1.0, UrdfCmdInterface.Position, "base_link", "arm1"),
            });

            var tess = new Mock<IMeshTessellator>();
            tess.Setup(t => t.Tessellate(It.IsAny<string>(), It.IsAny<TessellationLod>()))
                .Returns(TinyMesh());

            var tmp = Path.Combine(Path.GetTempPath(), "sw2gz_pipe_" + Guid.NewGuid());
            try
            {
                var report = new Sw2gzPipeline(mass.Object, walker.Object, tess.Object)
                    .Run(tmp, "joint_pkg", "A", "a@b", "MIT");
                Assert.False(report.HasErrors,
                    string.Join("; ", System.Linq.Enumerable.Select(report.Errors, e => e.Code + " " + e.Message)));

                string root = Path.Combine(tmp, "joint_pkg_ws", "src", "joint_pkg");

                string urdf = File.ReadAllText(Path.Combine(root, "urdf", "joint_pkg.urdf.xacro"));
                Assert.Contains("<joint name=\"shoulder\" type=\"revolute\">", urdf);
                Assert.Contains("<parent link=\"base_link\"/>", urdf);
                Assert.Contains("<child link=\"arm1\"/>", urdf);

                string controllers = File.ReadAllText(Path.Combine(root, "config", "controllers.yaml"));
                Assert.Contains("shoulder", controllers);
            }
            finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
        }

        [Fact]
        public void Run_SdfModel_EmitsGzModelDirEmptyWorldSpawnLaunch_IgnoringActuation()
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
            walker.Setup(w => w.WalkMates()).Returns(new[]
            {
                new MateSpec("shoulder", MateKind.Revolute, Pose.Identity, Vector3.UnitZ,
                    -1.0, 1.0, 0, 0, UrdfCmdInterface.Position, "base_link", "arm1"),
            });
            var tess = new Mock<IMeshTessellator>();
            tess.Setup(t => t.Tessellate(It.IsAny<string>(), It.IsAny<TessellationLod>())).Returns(TinyMesh());

            var tmp = Path.Combine(Path.GetTempPath(), "sw2gz_asset_" + Guid.NewGuid());
            try
            {
                // StackProfile.Default() = full ros2_control stack — the gz ExportMode must
                // override/ignore it (no control files in a gz model package).
                var report = new Sw2gzPipeline(mass.Object, walker.Object, tess.Object)
                    .Run(tmp, "model_pkg", "A", "a@b", "MIT",
                         Array.Empty<SW2GZ.Build.Model.SensorDef>(),
                         SW2GZ.Ros2.StackProfile.Default(), SW2GZ.Ros2.ExportMode.SdfModel);
                Assert.False(report.HasErrors,
                    string.Join("; ", System.Linq.Enumerable.Select(report.Errors, e => e.Code + " " + e.Message)));

                string root = Path.Combine(tmp, "model_pkg_ws", "src", "model_pkg");
                Assert.True(File.Exists(Path.Combine(root, "models", "model_pkg", "model.config")));
                Assert.True(File.Exists(Path.Combine(root, "models", "model_pkg", "model.sdf")));
                Assert.True(File.Exists(Path.Combine(root, "models", "model_pkg", "meshes", "base_link.dae")));
                Assert.True(File.Exists(Path.Combine(root, "worlds", "empty.sdf")));
                Assert.True(File.Exists(Path.Combine(root, "launch", "model_pkg.launch.py")));

                string sdf = File.ReadAllText(Path.Combine(root, "models", "model_pkg", "model.sdf"));
                Assert.Contains("<model name=\"model_pkg\">", sdf);
                Assert.Contains("model://model_pkg/meshes/base_link.dae", sdf);
                Assert.Contains("<joint name=\"shoulder\" type=\"revolute\">", sdf);

                // No URDF / control artifacts, even though StackProfile.Default() was passed.
                Assert.False(Directory.Exists(Path.Combine(root, "urdf")));
                Assert.False(File.Exists(Path.Combine(root, "config", "controllers.yaml")));
                Assert.False(File.Exists(Path.Combine(root, "config", "ros_gz_bridge.yaml")));

                string launch = File.ReadAllText(Path.Combine(root, "launch", "model_pkg.launch.py"));
                Assert.Contains("'create'", launch);
                Assert.DoesNotContain("gz_ros2_control", launch);
            }
            finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
        }

        [Fact]
        public void Run_SdfWorld_EmitsWorldThatIncludesModel()
        {
            var mass = new Mock<IMassProperties>();
            mass.Setup(m => m.Get(It.IsAny<string>()))
                .Returns(new MassProps(1.0, Vector3.Zero, Matrix3.Identity));
            var walker = new Mock<IAssemblyWalker>();
            walker.Setup(w => w.WalkActive()).Returns(new[] { new LinkSpec("base_link", new[] { "/p/base.SLDPRT" }) });
            walker.Setup(w => w.WalkMates()).Returns(Array.Empty<MateSpec>());
            var tess = new Mock<IMeshTessellator>();
            tess.Setup(t => t.Tessellate(It.IsAny<string>(), It.IsAny<TessellationLod>())).Returns(TinyMesh());

            var tmp = Path.Combine(Path.GetTempPath(), "sw2gz_world_" + Guid.NewGuid());
            try
            {
                var report = new Sw2gzPipeline(mass.Object, walker.Object, tess.Object)
                    .Run(tmp, "world_pkg", "A", "a@b", "MIT",
                         Array.Empty<SW2GZ.Build.Model.SensorDef>(),
                         SW2GZ.Ros2.StackProfile.Default(), SW2GZ.Ros2.ExportMode.SdfWorld);
                Assert.False(report.HasErrors);

                string root = Path.Combine(tmp, "world_pkg_ws", "src", "world_pkg");
                Assert.True(File.Exists(Path.Combine(root, "models", "world_pkg", "model.sdf")));
                Assert.True(File.Exists(Path.Combine(root, "worlds", "world_pkg.sdf")));
                Assert.True(File.Exists(Path.Combine(root, "launch", "world_pkg.launch.py")));

                string world = File.ReadAllText(Path.Combine(root, "worlds", "world_pkg.sdf"));
                Assert.Contains("<include>", world);
                Assert.Contains("<uri>model://world_pkg</uri>", world);

                string launch = File.ReadAllText(Path.Combine(root, "launch", "world_pkg.launch.py"));
                Assert.Contains("world_pkg.sdf", launch);
                Assert.DoesNotContain("'create'", launch);
            }
            finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
        }
    }
}
