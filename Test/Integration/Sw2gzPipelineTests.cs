/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System;
using System.IO;
using System.Numerics;
using Moq;
using SW2GZ.Build;
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
        public void Run_LinkWithoutMaterial_ThrowsBeforeWriting()
        {
            var mass = new Mock<IMassProperties>();
            mass.Setup(m => m.Get("/p/bad")).Throws(new MaterialMissingException("bad"));

            var walker = new Mock<IAssemblyWalker>();
            walker.Setup(w => w.WalkActive()).Returns(new[] { new LinkSpec("bad", new[] { "/p/bad" }) });

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
    }
}
