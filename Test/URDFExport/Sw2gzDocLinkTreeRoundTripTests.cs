/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

D1 guards that Sw2gzDoc round-trips a non-trivial link tree (A → B → C)
through DataContract serialization without loss. Sw2gzCreateRobotPmp.cs
no longer re-seeds Robot.Links from the assembly when a tree is already
present, so the persisted tree must survive verbatim across save/load.
*/
using System.Collections.Generic;
using SW2GZ.Build.Model;
using SW2GZ.URDFExport;
using Xunit;

namespace SW2GZ.Test.URDFExport
{
    public class Sw2gzDocLinkTreeRoundTripTests
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void ChainOfThreeLinks_SurvivesXmlRoundTrip()
        {
            var src = new Sw2gzDoc();
            src.Robot.Links = new List<LinkDef>
            {
                new LinkDef { Name = "A", ParentName = "",  ComponentIds = new List<string> { "a-1@asm" } },
                new LinkDef { Name = "B", ParentName = "A", ComponentIds = new List<string> { "b-1@asm" } },
                new LinkDef { Name = "C", ParentName = "B", ComponentIds = new List<string> { "c-1@asm", "c-2@asm" } },
            };

            string xml = Sw2gzDocCodec.ToXmlString(src);
            Sw2gzDoc dst = Sw2gzDocCodec.FromXmlString(xml);

            Assert.NotNull(dst);
            Assert.Equal(3, dst.Robot.Links.Count);

            // Tree shape preserved in order.
            Assert.Equal("A", dst.Robot.Links[0].Name);
            Assert.Equal("",  dst.Robot.Links[0].ParentName);
            Assert.Equal("B", dst.Robot.Links[1].Name);
            Assert.Equal("A", dst.Robot.Links[1].ParentName);
            Assert.Equal("C", dst.Robot.Links[2].Name);
            Assert.Equal("B", dst.Robot.Links[2].ParentName);

            // Component-id payloads preserved.
            Assert.Equal(new[] { "a-1@asm" }, dst.Robot.Links[0].ComponentIds);
            Assert.Equal(new[] { "b-1@asm" }, dst.Robot.Links[1].ComponentIds);
            Assert.Equal(new[] { "c-1@asm", "c-2@asm" }, dst.Robot.Links[2].ComponentIds);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AssetArticulationSensorCollision_SurviveXmlRoundTrip()
        {
            var src = new Sw2gzDoc { Mode = Sw2gzMode.Asset };
            src.Asset.BodyPart = "door-1@asm";
            src.Asset.IsStatic = false;
            src.Asset.JointType = "revolute";
            src.Asset.JointAxisX = 0; src.Asset.JointAxisY = 0; src.Asset.JointAxisZ = 1;
            src.Asset.JointLower = -1.0; src.Asset.JointUpper = 2.0;
            src.Asset.SensorKind = "camera";
            src.Asset.SensorTopic = "/door/cam";
            src.Asset.Collision = "box";

            Sw2gzDoc dst = Sw2gzDocCodec.FromXmlString(Sw2gzDocCodec.ToXmlString(src));

            Assert.NotNull(dst);
            Assert.Equal("revolute", dst.Asset.JointType);
            Assert.Equal(1.0, dst.Asset.JointAxisZ);
            Assert.Equal(-1.0, dst.Asset.JointLower);
            Assert.Equal(2.0, dst.Asset.JointUpper);
            Assert.Equal("camera", dst.Asset.SensorKind);
            Assert.Equal("/door/cam", dst.Asset.SensorTopic);
            Assert.Equal("box", dst.Asset.Collision);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void LegacyAssetDoc_DefaultsNewFields()
        {
            // A doc XML saved before the A1/A2/A3 fields existed: only the old
            // asset fields present. New fields must reseed to safe defaults, not
            // null/0, via [OnDeserializing].
            string legacy =
                "<Sw2gzDoc xmlns=\"http://schemas.datacontract.org/2004/07/SW2GZ.URDFExport\" " +
                "xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                "<Asset><BodyPart>w-1@asm</BodyPart><FrictionMu>0.8</FrictionMu><IsStatic>true</IsStatic></Asset>" +
                "<Mode>Asset</Mode></Sw2gzDoc>";
            Sw2gzDoc dst = Sw2gzDocCodec.FromXmlString(legacy);
            Assert.NotNull(dst);
            Assert.Equal("none", dst.Asset.JointType);
            Assert.Equal("none", dst.Asset.SensorKind);
            Assert.Equal("mesh", dst.Asset.Collision);
            Assert.Equal(1.0, dst.Asset.JointAxisZ);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void EmptyLinks_RoundTripsAsEmpty()
        {
            var src = new Sw2gzDoc();
            // No Robot.Links populated. This is the seed-needed case the
            // Create-Robot PMP detects on first open.

            string xml = Sw2gzDocCodec.ToXmlString(src);
            Sw2gzDoc dst = Sw2gzDocCodec.FromXmlString(xml);

            Assert.NotNull(dst);
            Assert.Empty(dst.Robot.Links);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void WorldSceneSettings_SurviveXmlRoundTrip()
        {
            var src = new Sw2gzDoc { Mode = Sw2gzMode.World };
            src.World.Ground = "floor-1";
            src.World.Scene.SunAzimuthDeg = 42.5;
            src.World.Scene.CastShadows = false;
            src.World.Scene.Sky = true;
            src.World.Scene.GravityZ = -1.62;          // moon
            src.World.Scene.UseGeo = true;
            src.World.Scene.Latitude = 12.25;

            string xml = Sw2gzDocCodec.ToXmlString(src);
            Sw2gzDoc dst = Sw2gzDocCodec.FromXmlString(xml);

            Assert.NotNull(dst.World.Scene);
            Assert.Equal(42.5, dst.World.Scene.SunAzimuthDeg);
            Assert.False(dst.World.Scene.CastShadows);
            Assert.True(dst.World.Scene.Sky);
            Assert.Equal(-1.62, dst.World.Scene.GravityZ);
            Assert.True(dst.World.Scene.UseGeo);
            Assert.Equal(12.25, dst.World.Scene.Latitude);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void WorldFriction_RoundTripsAndBridgesToExportConfig()
        {
            var src = new Sw2gzDoc { Mode = Sw2gzMode.World };
            src.World.Scene.Friction = 0.35;

            Sw2gzDoc dst = Sw2gzDocCodec.FromXmlString(Sw2gzDocCodec.ToXmlString(src));
            Assert.Equal(0.35, dst.World.Scene.Friction);

            // Bridge threads Scene.Friction → cfg.WorldFriction (what the exporter reads).
            var cfg = Sw2gzDocToExportConfig.Bridge(dst, new ExportMetaInput());
            Assert.Equal(0.35, cfg.WorldFriction);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void WorldLights_RoundTripAndMapToSdfLights()
        {
            var src = new Sw2gzDoc { Mode = Sw2gzMode.World };
            src.World.Scene.Lights.Add(new Sw2gzLightConfig
            { Type = "point", X = 1, Y = 2, Z = 3, Intensity = 0.7, Range = 8 });
            src.World.Scene.Lights.Add(new Sw2gzLightConfig { Type = "spot" });

            Sw2gzDoc dst = Sw2gzDocCodec.FromXmlString(Sw2gzDocCodec.ToXmlString(src));
            Assert.Equal(2, dst.World.Scene.Lights.Count);
            Assert.Equal("point", dst.World.Scene.Lights[0].Type);
            Assert.Equal(3.0, dst.World.Scene.Lights[0].Z);

            var sdf = dst.World.Scene.ToExtraLights();
            Assert.Equal(2, sdf.Count);
            Assert.Equal("light1", sdf[0].Name);
            Assert.Equal("spot", sdf[1].Type);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void WorldSceneClone_DeepCopiesLights()
        {
            var src = new Sw2gzWorldSceneConfig();
            src.Lights.Add(new Sw2gzLightConfig { Type = "point", Intensity = 1.0 });
            var clone = src.Clone();
            clone.Lights[0].Intensity = 0.1;   // mutate the clone
            Assert.Equal(1.0, src.Lights[0].Intensity);   // original unchanged
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void LegacyDoc_WithoutSceneElement_DeserializesWithDefaults()
        {
            // Simulate a checkpoint saved before World.Scene existed: strip the
            // <Scene> element from the serialized XML, then load it.
            string xml = Sw2gzDocCodec.ToXmlString(new Sw2gzDoc { Mode = Sw2gzMode.World });
            string stripped = System.Text.RegularExpressions.Regex.Replace(
                xml, "<Scene>.*?</Scene>", "", System.Text.RegularExpressions.RegexOptions.Singleline);

            Sw2gzDoc dst = Sw2gzDocCodec.FromXmlString(stripped);

            Assert.NotNull(dst.World.Scene);                 // OnDeserializing reseeds it
            Assert.True(dst.World.Scene.CastShadows);         // default
            Assert.Equal(-9.8, dst.World.Scene.GravityZ);     // default
        }
    }
}
