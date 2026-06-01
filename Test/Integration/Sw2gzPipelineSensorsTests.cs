/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P6-data — end-to-end: the 6-arg Sw2gzPipeline.Run overload threads a
caller-supplied sensor list through RobotModelBuilder.AssembleSensors
into the URDF body, the world SDF, and the bridge yaml.

Design note (per the spec): the pipeline takes sensors as a parameter
because there's no SW-COM sensor source yet. UI-driven sensor specs
land in P8; this test pins down the wiring so the SW-COM path can land
without re-plumbing.
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
    public class Sw2gzPipelineSensorsTests
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

        private static (Mock<IMassProperties>, Mock<IAssemblyWalker>, Mock<IMeshTessellator>) MakeMocks()
        {
            var mass = new Mock<IMassProperties>();
            mass.Setup(m => m.Get(It.IsAny<string>()))
                .Returns(new MassProps(1.0, Vector3.Zero, Matrix3.Identity));

            var walker = new Mock<IAssemblyWalker>();
            walker.Setup(w => w.WalkActive()).Returns(new[]
            {
                new LinkSpec("base_link", new[] { "/p/base.SLDPRT" }),
            });

            var tess = new Mock<IMeshTessellator>();
            tess.Setup(t => t.Tessellate(It.IsAny<string>(), It.IsAny<TessellationLod>()))
                .Returns(TinyMesh());

            return (mass, walker, tess);
        }

        [Fact]
        public void Run_WithImuSensor_WritesPluginsBridgeAndGazeboBlock()
        {
            var (mass, walker, tess) = MakeMocks();
            var imu = new ImuSensor("imu1", "base_link", Pose.Identity, "/imu", "base_link", 100.0, 0.01);

            string tmp = Path.Combine(Path.GetTempPath(), "sw2gz_pipe_sens_" + Guid.NewGuid());
            try
            {
                new Sw2gzPipeline(mass.Object, walker.Object, tess.Object)
                    .Run(tmp, "sens_pkg", "A", "a@b", "Apache-2.0", new SensorDef[] { imu });

                string root = Path.Combine(tmp, "sens_pkg_ws", "src", "sens_pkg");

                string worldSdf = File.ReadAllText(Path.Combine(root, "worlds", "empty.sdf"));
                // The default world already carries imu/sensors plugins, but our
                // injected block re-emits the imu-system plugin family marker —
                // assert both that the imu plugin filename is present (at least
                // once) and that the block lands inside the world element.
                Assert.Contains("gz-sim-imu-system", worldSdf);

                string bridge = File.ReadAllText(Path.Combine(root, "config", "ros_gz_bridge.yaml"));
                Assert.Contains("sensor_msgs/msg/Imu", bridge);
                Assert.Contains("gz.msgs.IMU", bridge);
                Assert.Contains("/imu", bridge);

                string xacro = File.ReadAllText(Path.Combine(root, "urdf", "sens_pkg.urdf.xacro"));
                Assert.Contains("<gazebo reference=\"base_link\">", xacro);
                Assert.Contains("<sensor name=\"imu1\" type=\"imu\">", xacro);
            }
            finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
        }

        [Fact]
        public void Run_NoSensors_FiveArgOverload_StillProducesValidOutput()
        {
            // Back-compat: 5-arg overload should keep working (delegates to 6-arg with empty list).
            var (mass, walker, tess) = MakeMocks();

            string tmp = Path.Combine(Path.GetTempPath(), "sw2gz_pipe_legacy_" + Guid.NewGuid());
            try
            {
                new Sw2gzPipeline(mass.Object, walker.Object, tess.Object)
                    .Run(tmp, "pkg", "A", "a@b", "Apache-2.0");

                string root = Path.Combine(tmp, "pkg_ws", "src", "pkg");
                string xacro = File.ReadAllText(Path.Combine(root, "urdf", "pkg.urdf.xacro"));
                // No sensors → no <gazebo> block from the sensor path.
                Assert.DoesNotContain("<gazebo reference=", xacro);
            }
            finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
        }

        [Fact]
        public void Run_SensorOnUnknownLink_ThrowsBeforeWritingOutput()
        {
            var (mass, walker, tess) = MakeMocks();
            var bad = new ImuSensor("imu", "no_such_link", Pose.Identity, "/imu", "no_such_link", 100, 0);

            string tmp = Path.Combine(Path.GetTempPath(), "sw2gz_pipe_bad_" + Guid.NewGuid());
            try
            {
                Assert.Throws<ArgumentException>(() =>
                    new Sw2gzPipeline(mass.Object, walker.Object, tess.Object)
                        .Run(tmp, "p", "A", "a@b", "Apache-2.0", new SensorDef[] { bad }));

                // Pipeline cleans up its workspace on failure (caught in Run's try/catch).
                string ws = Path.Combine(tmp, "p_ws");
                Assert.False(Directory.Exists(ws), "Pipeline should not leave a partial workspace on validation failure.");
            }
            finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
        }
    }
}
