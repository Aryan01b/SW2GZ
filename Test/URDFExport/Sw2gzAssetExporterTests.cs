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
    }
}
