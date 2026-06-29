/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

World exporter orchestration, COM-free via a fake IMeshTessellator. Covers the
seam the failed first attempt could not test: picks → meshes + a scene .sdf.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.SwSurface.Abstractions;
using SW2GZ.URDFExport;
using Xunit;

namespace SW2GZ.Writers.Tests
{
    public class Sw2gzWorldExporterTests : IDisposable
    {
        private readonly string _dir;

        public Sw2gzWorldExporterTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "sw2gz_world_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        // Fake tessellator: returns a trivial triangle for any name in `known`,
        // throws for the rest (to exercise the skip-on-failure path).
        private sealed class FakeTess : IMeshTessellator
        {
            private readonly HashSet<string> _known;
            public FakeTess(params string[] known) => _known = new HashSet<string>(known);
            public MeshData Tessellate(string componentPathName, TessellationLod lod)
            {
                if (!_known.Contains(componentPathName))
                    throw new InvalidOperationException("no bodies: " + componentPathName);
                return new MeshData(
                    new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) },
                    new[] { 0, 1, 2 }, null);
            }
        }

        private Sw2gzExportConfig Cfg(string ground, params string[] assets)
        {
            var c = new Sw2gzExportConfig
            {
                Mode = SW2GZ.Ros2.ExportMode.SdfWorld,
                PackageName = "my_env",
                WorldGround = ground,
                WorldAssets = new List<string>(assets),
            };
            return c;
        }

        private string Sdf() => File.ReadAllText(Path.Combine(_dir, "my_env", "my_env.sdf"));

        [Fact]
        public void Export_WritesSelfContainedSdfPlusMeshes()
        {
            var rep = Sw2gzWorldExporter.Export(
                new FakeTess("floor-1", "rack-1"), Cfg("floor-1", "rack-1"), _dir, 0, 0, 0);

            Assert.False(rep.HasErrors);
            Assert.True(File.Exists(Path.Combine(_dir, "my_env", "my_env.sdf")));
            Assert.True(File.Exists(Path.Combine(_dir, "my_env", "meshes", "floor_1.dae")));
            Assert.True(File.Exists(Path.Combine(_dir, "my_env", "meshes", "rack_1.dae")));
        }

        [Fact]
        public void Export_GroundPicked_NoDefaultGroundPlane()
        {
            Sw2gzWorldExporter.Export(new FakeTess("floor-1"), Cfg("floor-1"), _dir, 0, 0, 0);
            Assert.DoesNotContain("ground_plane", Sdf());
            Assert.Contains("<model name=\"floor_1\">", Sdf());
        }

        [Fact]
        public void Export_NoGround_EmitsDefaultGroundPlane()
        {
            Sw2gzWorldExporter.Export(new FakeTess("rack-1"), Cfg("", "rack-1"), _dir, 0, 0, 0);
            Assert.Contains("ground_plane", Sdf());
        }

        [Fact]
        public void Export_UntessellatableComponent_SkippedWithWarning()
        {
            // ground tessellates; asset does not → asset skipped, export still succeeds.
            var rep = Sw2gzWorldExporter.Export(
                new FakeTess("floor-1"), Cfg("floor-1", "broken-asm"), _dir, 0, 0, 0);

            Assert.False(rep.HasErrors);
            Assert.Single(rep.Warnings);
            Assert.False(File.Exists(Path.Combine(_dir, "my_env", "meshes", "broken_asm.dae")));
            Assert.Contains("<model name=\"floor_1\">", Sdf());
            Assert.DoesNotContain("broken_asm", Sdf());
        }

        [Fact]
        public void Export_MeshesHaveNormalsForGzLighting()
        {
            Sw2gzWorldExporter.Export(new FakeTess("floor-1"), Cfg("floor-1"), _dir, 0, 0, 0);
            string dae = File.ReadAllText(Path.Combine(_dir, "my_env", "meshes", "floor_1.dae"));
            Assert.Contains("semantic=\"NORMAL\"", dae);
            Assert.Contains("g0-norm", dae);
        }

        [Fact]
        public void Export_RecentersSceneAboutOrigin()
        {
            // FakeTess far from origin → exporter must shift verts so the AABB
            // center lands at the origin (Gz default camera looks at 0,0,0).
            var rep = Sw2gzWorldExporter.Export(new FarTess(100f), Cfg("floor-1"), _dir, 0, 0, 0);
            Assert.False(rep.HasErrors);
            string dae = File.ReadAllText(Path.Combine(_dir, "my_env", "meshes", "floor_1.dae"));
            // The far offset (100) must not appear verbatim in the position array.
            Assert.DoesNotContain("100.5", dae);
            Assert.DoesNotContain("99.5", dae);
        }

        private sealed class FarTess : IMeshTessellator
        {
            private readonly float _o;
            public FarTess(float offset) => _o = offset;
            public MeshData Tessellate(string n, TessellationLod lod) => new MeshData(
                new[] { new Vector3(_o, _o, _o), new Vector3(_o + 1, _o, _o), new Vector3(_o, _o + 1, _o) },
                new[] { 0, 1, 2 }, null);
        }

        [Fact]
        public void Export_EmitsPerAssetMaterial()
        {
            // FakeTess returns null color → exporter falls back to neutral gray.
            Sw2gzWorldExporter.Export(new FakeTess("floor-1"), Cfg("floor-1"), _dir, 0, 0, 0);
            Assert.Contains("<material>", Sdf());
            Assert.Contains("<diffuse>0.8 0.8 0.8 1</diffuse>", Sdf());
        }

        [Fact]
        public void Export_PlacesFloorAtZeroAlongUpAxis()
        {
            // Default up = +Y. FarTess spans Y in [100,101]; floor (min Y) must
            // shift to 0, so the mesh's Y positions become 0 and 1 (not ~100).
            Sw2gzWorldExporter.Export(new FarTess(100f), Cfg("floor-1"), _dir, 0, 0, 0);
            string dae = File.ReadAllText(Path.Combine(_dir, "my_env", "meshes", "floor_1.dae"));
            // No vertex should still carry the ~100 up-axis offset.
            Assert.DoesNotContain("100", dae.Substring(dae.IndexOf("g0-pos-array")));
        }

        [Fact]
        public void Export_EmitsFramedGuiCamera()
        {
            // A scene that tessellates must get a <gui> with a non-origin camera
            // so `gz sim` opens looking at the assets, not empty space.
            Sw2gzWorldExporter.Export(new FarTess(100f), Cfg("floor-1"), _dir, 0, 0, 0);
            string sdf = Sdf();
            Assert.Contains("<gui", sdf);
            Assert.Contains("<camera_pose>", sdf);
            // Iso default frames from above the floor → camera Z must be > 0.
            string pose = sdf.Substring(sdf.IndexOf("<camera_pose>") + "<camera_pose>".Length);
            pose = pose.Substring(0, pose.IndexOf("</camera_pose>"));
            string[] p = pose.Split(' ');
            Assert.True(double.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture) > 0,
                "camera should sit above the ground plane, got Z=" + p[2]);
        }

        [Fact]
        public void Export_TopView_LooksStraightDown()
        {
            var c = Cfg("floor-1");
            c.WorldInitialView = "top";
            Sw2gzWorldExporter.Export(new FakeTess("floor-1"), c, _dir, 0, 0, 0);
            string sdf = Sdf();
            Assert.Contains("<camera_pose>", sdf);
            // Top view: camera over origin (x≈0, y≈0), pitched ~+pi/2 (looking down).
            string pose = sdf.Substring(sdf.IndexOf("<camera_pose>") + "<camera_pose>".Length);
            pose = pose.Substring(0, pose.IndexOf("</camera_pose>"));
            string[] p = pose.Split(' ');
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            Assert.Equal(0.0, double.Parse(p[0], ci), 3);
            Assert.Equal(0.0, double.Parse(p[1], ci), 3);
            Assert.True(double.Parse(p[4], ci) > 1.4, "pitch should be ~pi/2 looking down");
        }

        [Fact]
        public void Export_SensorPluginsEnabled_EmitWorldSystemPlugins()
        {
            var c = Cfg("floor-1");
            c.WorldSensorPlugins = new Sw2gzWorldSensorsConfig { Sensors = true, Imu = true };
            Sw2gzWorldExporter.Export(new FakeTess("floor-1"), c, _dir, 0, 0, 0);
            string sdf = Sdf();
            Assert.Contains("gz-sim-sensors-system", sdf);
            Assert.Contains("gz-sim-imu-system", sdf);
            // World mode never places per-model <sensor> blocks.
            Assert.DoesNotContain("<sensor", sdf);
        }

        [Fact]
        public void Export_KeyboardTeleopEnabled_EmitsKeyPublisherAndTriggeredPublisher()
        {
            var c = Cfg("floor-1");
            c.WorldSensorPlugins = new Sw2gzWorldSensorsConfig { KeyPublisher = true, TriggeredPublisher = true };
            Sw2gzWorldExporter.Export(new FakeTess("floor-1"), c, _dir, 0, 0, 0);
            string sdf = Sdf();
            Assert.Contains("KeyPublisher", sdf);
            Assert.Contains("gz-sim-triggered-publisher-system", sdf);
            Assert.Contains("topic=\"/cmd_vel\"", sdf);
        }

        [Fact]
        public void Export_DefaultSensorPlugins_EmitBaselineOnly()
        {
            var c = Cfg("floor-1");   // default WorldSensorPlugins
            Sw2gzWorldExporter.Export(new FakeTess("floor-1"), c, _dir, 0, 0, 0);
            string sdf = Sdf();
            Assert.Contains("gz-sim-user-commands-system", sdf);
            Assert.Contains("gz-sim-scene-broadcaster-system", sdf);
            Assert.DoesNotContain("gz-sim-sensors-system", sdf);
            Assert.DoesNotContain("<sensor", sdf);
        }

        [Fact]
        public void Export_PhysicsAndRotationFlowThrough()
        {
            var c = Cfg("floor-1");
            c.WorldPhysicsEngine = "bullet";
            c.WorldMaxStepSize = 0.002;
            Sw2gzWorldExporter.Export(new FakeTess("floor-1"), c, _dir, 1.5708, 0, 1.5708);

            string sdf = Sdf();
            Assert.Contains("type=\"bullet\"", sdf);
            Assert.Contains("<max_step_size>0.002</max_step_size>", sdf);
            Assert.Contains("<pose>0 0 0 1.5708 0 1.5708</pose>", sdf);
        }
    }
}
