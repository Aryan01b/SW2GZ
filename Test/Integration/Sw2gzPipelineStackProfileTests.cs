/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Task 3 — StackProfile threaded through Sw2gzPipeline.Run. These tests pin the
behaviour-preserving contract: the Default() profile reproduces the legacy
full-stack output, and ModelOnly() reproduces the legacy `modelOnly:true`
output. Mock setup mirrors Sw2gzPipelineTests.cs (one tiny link, no mates).
*/
using System;
using System.IO;
using System.Numerics;
using Moq;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Math;
using SW2GZ.Ros2;
using SW2GZ.SwSurface.Abstractions;
using SW2GZ.URDFExport;
using Xunit;

namespace SW2GZ.Integration.Tests
{
    public class Sw2gzPipelineStackProfileTests
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

        private static string NewTmp() =>
            Path.Combine(Path.GetTempPath(), "sw2gz_prof_" + Guid.NewGuid());

        // Builds a pipeline that produces one tiny link with no mates.
        private static Sw2gzPipeline MakePipeline()
        {
            var mass = new Mock<IMassProperties>();
            mass.Setup(m => m.Get(It.IsAny<string>()))
                .Returns(new MassProps(1.0, Vector3.Zero, Matrix3.Identity));

            var walker = new Mock<IAssemblyWalker>();
            walker.Setup(w => w.WalkActive()).Returns(new[]
            {
                new LinkSpec("base_link", new[] { "/p/base.SLDPRT" }),
            });
            walker.Setup(w => w.WalkMates()).Returns(Array.Empty<MateSpec>());

            var tess = new Mock<IMeshTessellator>();
            tess.Setup(t => t.Tessellate(It.IsAny<string>(), It.IsAny<TessellationLod>()))
                .Returns(TinyMesh());

            return new Sw2gzPipeline(mass.Object, walker.Object, tess.Object);
        }

        [Fact]
        public void DefaultProfile_EmitsFullStack()
        {
            string tmp = NewTmp();
            try
            {
                MakePipeline().Run(tmp, "prof_pkg", "A", "a@b", "Apache-2.0",
                    System.Array.Empty<SensorDef>(), StackProfile.Default());

                string pkg = Path.Combine(tmp, "prof_pkg_ws", "src", "prof_pkg");
                Assert.True(File.Exists(Path.Combine(pkg, "urdf", "inc", "ros2_control.xacro")));
                Assert.True(File.Exists(Path.Combine(pkg, "urdf", "inc", "gz.xacro")));
                Assert.True(File.Exists(Path.Combine(pkg, "config", "controllers.yaml")));
                Assert.True(File.Exists(Path.Combine(pkg, "config", "ros_gz_bridge.yaml")));
            }
            finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
        }

        [Fact]
        public void ModelOnlyProfile_OmitsControlAndPlugins()
        {
            string tmp = NewTmp();
            try
            {
                MakePipeline().Run(tmp, "bare_pkg", "A", "a@b", "MIT",
                    System.Array.Empty<SensorDef>(), StackProfile.ModelOnly());

                string pkg = Path.Combine(tmp, "bare_pkg_ws", "src", "bare_pkg");
                Assert.False(File.Exists(Path.Combine(pkg, "urdf", "inc", "ros2_control.xacro")));
                Assert.False(File.Exists(Path.Combine(pkg, "urdf", "inc", "gz.xacro")));
                Assert.False(File.Exists(Path.Combine(pkg, "config", "controllers.yaml")));
                Assert.False(File.Exists(Path.Combine(pkg, "config", "ros_gz_bridge.yaml")));
                Assert.True(File.Exists(Path.Combine(pkg, "urdf", "bare_pkg.urdf.xacro")));
                Assert.True(File.Exists(Path.Combine(pkg, "worlds", "empty.sdf")));
            }
            finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
        }
    }
}
