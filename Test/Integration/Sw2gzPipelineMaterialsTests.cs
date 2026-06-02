/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P5 — end-to-end: pipeline routes IAppearanceSource results to both the
URDF body XML (per-link <material> ref) and inc/materials.xacro (full
material defs).
*/
using System;
using System.IO;
using System.Numerics;
using Moq;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Math;
using SW2GZ.SwSurface.Abstractions;
using SW2GZ.URDFExport;
using Xunit;

namespace SW2GZ.Integration.Tests
{
    public class Sw2gzPipelineMaterialsTests
    {
        private static MeshData TinyMesh()
        {
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
        public void Run_AppearanceSourceProvidesMaterial_WritesMaterialsXacroAndUrdfRef()
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
            walker.Setup(w => w.WalkMates()).Returns(System.Array.Empty<SW2GZ.Build.MateSpec>());

            var tess = new Mock<IMeshTessellator>();
            tess.Setup(t => t.Tessellate(It.IsAny<string>(), It.IsAny<TessellationLod>()))
                .Returns(TinyMesh());

            var appearances = new Mock<IAppearanceSource>();
            appearances.Setup(a => a.GetMaterial("/p/base.SLDPRT"))
                .Returns(new MaterialDef("steel", 0.7, 0.7, 0.7, 1.0));
            appearances.Setup(a => a.GetMaterial("/p/arm1.SLDPRT"))
                .Returns((MaterialDef?)null);

            var tmp = Path.Combine(Path.GetTempPath(), "sw2gz_pipe_mat_" + Guid.NewGuid());
            try
            {
                new Sw2gzPipeline(mass.Object, walker.Object, tess.Object, appearances.Object)
                    .Run(tmp, "mat_pkg", "A", "a@b", "Apache-2.0");

                string root = Path.Combine(tmp, "mat_pkg_ws", "src", "mat_pkg");
                string matsPath = Path.Combine(root, "urdf", "inc", "materials.xacro");
                string urdfPath = Path.Combine(root, "urdf", "mat_pkg.urdf.xacro");

                Assert.True(File.Exists(matsPath));
                string mats = File.ReadAllText(matsPath);
                Assert.Contains("<material name=\"steel\">", mats);
                Assert.Contains("<color rgba=\"0.7 0.7 0.7 1\"/>", mats);

                Assert.True(File.Exists(urdfPath));
                string urdf = File.ReadAllText(urdfPath);
                Assert.Contains("<material name=\"steel\"/>", urdf);
            }
            finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
        }
    }
}
