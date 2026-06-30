/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Gz;
using SW2GZ.Math;
using SW2GZ.SwSurface.Abstractions;
using SW2GZ.URDFExport;
using Xunit;

namespace SW2GZ.Writers.Tests
{
    public class Sw2gzAssetExporterTests : IDisposable
    {
        private readonly string _dir;
        public Sw2gzAssetExporterTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "sw2gz_asset_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private sealed class FakeTess : IMeshTessellator
        {
            private readonly System.Drawing.Color? _c;
            public FakeTess(System.Drawing.Color? c = null) => _c = c;
            public MeshData Tessellate(string n, TessellationLod lod) => new MeshData(
                new[] { new Vector3(0, 0, 2), new Vector3(1, 0, 2), new Vector3(0, 1, 2) },
                new[] { 0, 1, 2 }, _c);
        }

        private Sw2gzExportConfig Cfg(string part = "widget-1") => new Sw2gzExportConfig
        {
            Mode = SW2GZ.Ros2.ExportMode.SdfModel,
            PackageName = "my_asset",
            AssetBodyPart = part,
            AssetFrictionMu = 0.8,
            AssetIsStatic = true,
        };

        private string Sdf() => File.ReadAllText(Path.Combine(_dir, "my_asset", "model.sdf"));

        [Fact]
        public void Export_WritesModelDirWithConfigSdfAndMesh()
        {
            var rep = Sw2gzAssetExporter.Export(new FakeTess(), Cfg(), _dir, Matrix3.Identity);
            Assert.False(rep.HasErrors);
            Assert.True(File.Exists(Path.Combine(_dir, "my_asset", "model.config")));
            Assert.True(File.Exists(Path.Combine(_dir, "my_asset", "model.sdf")));
            Assert.True(File.Exists(Path.Combine(_dir, "my_asset", "meshes", "my_asset.dae")));
            Assert.Contains("<model name=\"my_asset\">", Sdf());
            Assert.Contains("<static>true</static>", Sdf());
            Assert.Contains("<uri>meshes/my_asset.dae</uri>", Sdf());
        }

        [Fact]
        public void Export_EmitsMaterialFromPartColour()
        {
            Sw2gzAssetExporter.Export(
                new FakeTess(System.Drawing.Color.FromArgb(255, 51, 102, 153)), Cfg(), _dir, Matrix3.Identity);
            Assert.Contains("<material>", Sdf());
            Assert.Contains("<diffuse>0.2 0.4 0.6 1</diffuse>", Sdf());
        }

        [Fact]
        public void Export_EmitsFrictionAndNoInertialWhenStatic()
        {
            Sw2gzAssetExporter.Export(new FakeTess(), Cfg(), _dir, Matrix3.Identity);
            Assert.Contains("<mu>0.8</mu>", Sdf());
            Assert.DoesNotContain("<inertial>", Sdf());
        }

        [Fact]
        public void Export_DynamicEmitsInertial()
        {
            var c = Cfg(); c.AssetIsStatic = false;
            Sw2gzAssetExporter.Export(new FakeTess(), c, _dir, Matrix3.Identity);
            Assert.DoesNotContain("<static>true</static>", Sdf());
            Assert.Contains("<inertial>", Sdf());
        }

        [Fact]
        public void Export_NoBodyPart_Throws()
        {
            var c = Cfg("");
            Assert.Throws<SW2GZ.Exceptions.Sw2gzExportException>(
                () => Sw2gzAssetExporter.Export(new FakeTess(), c, _dir, Matrix3.Identity));
        }

        [Fact]
        public void Write_OmitsStaticWhenDynamic()
        {
            var s = SdfAssetModelWriter.Write(new SdfAssetModelInput("a", "a.dae", IsStatic: false, Mass: 1.0));
            Assert.DoesNotContain("<static>", s);
            Assert.Contains("<inertial>", s);
        }

        // ── A1: articulated asset (1-DOF joint to world) ──────────────────────

        [Fact]
        public void Write_JointNone_EmitsNoJoint()
        {
            var s = SdfAssetModelWriter.Write(new SdfAssetModelInput("a", "a.dae"));   // default none
            Assert.DoesNotContain("<joint", s);
        }

        [Fact]
        public void Write_Revolute_EmitsJointToWorldWithAxisAndLimit()
        {
            var s = SdfAssetModelWriter.Write(new SdfAssetModelInput("a", "a.dae",
                IsStatic: false, Mass: 1.0, JointType: "revolute",
                JointAxisX: 0, JointAxisY: 0, JointAxisZ: 1, JointLower: -1, JointUpper: 1));
            Assert.Contains("<joint name=\"joint\" type=\"revolute\">", s);
            Assert.Contains("<parent>world</parent>", s);
            Assert.Contains("<child>link</child>", s);
            Assert.Contains("<xyz>0 0 1</xyz>", s);
            Assert.Contains("<lower>-1</lower><upper>1</upper>", s);
        }

        [Fact]
        public void Write_Continuous_IsRevoluteWithNoLimit()
        {
            var s = SdfAssetModelWriter.Write(new SdfAssetModelInput("a", "a.dae",
                IsStatic: false, Mass: 1.0, JointType: "continuous"));
            Assert.Contains("type=\"revolute\"", s);   // SDF has no "continuous"
            Assert.Contains("<axis>", s);
            Assert.DoesNotContain("<limit>", s);
        }

        [Fact]
        public void Write_Prismatic_EmitsPrismaticWithLimit()
        {
            var s = SdfAssetModelWriter.Write(new SdfAssetModelInput("a", "a.dae",
                IsStatic: false, Mass: 1.0, JointType: "prismatic", JointLower: 0, JointUpper: 0.5));
            Assert.Contains("type=\"prismatic\"", s);
            Assert.Contains("<lower>0</lower><upper>0.5</upper>", s);
        }

        [Fact]
        public void Write_Fixed_EmitsFixedJointNoAxis()
        {
            var s = SdfAssetModelWriter.Write(new SdfAssetModelInput("a", "a.dae",
                IsStatic: false, Mass: 1.0, JointType: "fixed"));
            Assert.Contains("type=\"fixed\"", s);
            Assert.DoesNotContain("<axis>", s);
        }

        [Fact]
        public void Export_JointForcesDynamicEvenIfStaticChecked()
        {
            // User left IsStatic=true but picked a joint → exporter must make the
            // model dynamic (a joint to world is invalid on a static model).
            var c = Cfg();
            c.AssetIsStatic = true;
            c.AssetJointType = "revolute";
            Sw2gzAssetExporter.Export(new FakeTess(), c, _dir, Matrix3.Identity);
            Assert.DoesNotContain("<static>true</static>", Sdf());
            Assert.Contains("<joint name=\"joint\" type=\"revolute\">", Sdf());
            Assert.Contains("<inertial>", Sdf());
        }

        [Fact]
        public void Export_DefaultJointNone_ByteIdenticalNoJoint()
        {
            Sw2gzAssetExporter.Export(new FakeTess(), Cfg(), _dir, Matrix3.Identity);
            Assert.DoesNotContain("<joint", Sdf());
            Assert.Contains("<static>true</static>", Sdf());
        }

        // ── A2: sensor-bearing asset ──────────────────────────────────────────

        [Fact]
        public void Export_DefaultSensorNone_NoSensorBlock()
        {
            Sw2gzAssetExporter.Export(new FakeTess(), Cfg(), _dir, Matrix3.Identity);
            Assert.DoesNotContain("<sensor", Sdf());
        }

        [Fact]
        public void Export_CameraSensor_EmitsCameraOnLinkBeforeClose()
        {
            var c = Cfg();
            c.AssetSensorKind = "camera";
            c.AssetSensorTopic = "/asset/cam";
            Sw2gzAssetExporter.Export(new FakeTess(), c, _dir, Matrix3.Identity);
            string sdf = Sdf();
            Assert.Contains("<sensor name=\"sensor\" type=\"camera\">", sdf);
            Assert.Contains("<topic>/asset/cam</topic>", sdf);
            // sensor sits inside the link (before </link>).
            Assert.True(sdf.IndexOf("<sensor", StringComparison.Ordinal)
                      < sdf.IndexOf("</link>", StringComparison.Ordinal));
        }

        [Fact]
        public void Export_LidarSensor_EmitsGpuLidar()
        {
            var c = Cfg();
            c.AssetSensorKind = "gpu_lidar";
            Sw2gzAssetExporter.Export(new FakeTess(), c, _dir, Matrix3.Identity);
            Assert.Contains("type=\"gpu_lidar\"", Sdf());
        }

        [Fact]
        public void Export_ImuSensor_EmitsImu()
        {
            var c = Cfg();
            c.AssetSensorKind = "imu";
            Sw2gzAssetExporter.Export(new FakeTess(), c, _dir, Matrix3.Identity);
            Assert.Contains("type=\"imu\"", Sdf());
        }

        [Fact]
        public void Export_SensorAndJointCombine_ArticulatedSensorProp()
        {
            // A door with a camera: revolute joint + a camera sensor, dynamic.
            var c = Cfg();
            c.AssetJointType = "revolute";
            c.AssetSensorKind = "camera";
            Sw2gzAssetExporter.Export(new FakeTess(), c, _dir, Matrix3.Identity);
            string sdf = Sdf();
            Assert.Contains("type=\"revolute\"", sdf);
            Assert.Contains("type=\"camera\"", sdf);
            Assert.DoesNotContain("<static>true</static>", sdf);
        }

        [Fact]
        public void Write_NullSensor_OmitsSensorBlock()
        {
            var s = SdfAssetModelWriter.Write(new SdfAssetModelInput("a", "a.dae"));   // Sensor defaults null
            Assert.DoesNotContain("<sensor", s);
        }
    }
}
