/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

D2 — JointDef.OriginX/Y/Z + HasOrigin auto-detect fields:

  1. ROUND-TRIP: a JointDef with the new fields set survives
     DataContract serialization via Sw2gzDocCodec verbatim (values + flag).
  2. LEGACY LOAD: a Sw2gzDoc XML payload written before D2 (no OriginX/Y/Z
     elements, no HasOrigin element) must deserialize without throwing, and
     the new fields must default to 0 / false. This guards saved docs
     created by earlier wizard versions.
*/
using System.Collections.Generic;
using System.Numerics;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.URDFExport;
using Xunit;

namespace SW2GZ.Test.Build.Model
{
    public class JointDefAutoFieldsTests
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void OriginFields_RoundTripThroughSw2gzDocCodec()
        {
            var src = new Sw2gzDoc();
            src.Robot.Links = new List<LinkDef>
            {
                new LinkDef { Name = "base_link", ParentName = "",          ComponentIds = new List<string> { "base-1@asm" } },
                new LinkDef { Name = "arm",       ParentName = "base_link", ComponentIds = new List<string> { "arm-1@asm" } },
            };
            var j = new JointDef
            {
                Name       = "j1",
                ParentLink = "base_link",
                ChildLink  = "arm",
                Type       = UrdfJointType.Revolute,
                MateName   = "Concentric1",
                LimitLower = -1.5,
                LimitUpper = 1.5,
            };
            j.SetAxis(new Vector3(0, 0, 1));
            j.SetOrigin(new Vector3(0.123f, -0.456f, 0.789f));
            src.Robot.Joints = new List<JointDef> { j };

            string xml = Sw2gzDocCodec.ToXmlString(src);
            Sw2gzDoc dst = Sw2gzDocCodec.FromXmlString(xml);

            Assert.NotNull(dst);
            Assert.Single(dst.Robot.Joints);
            JointDef dj = dst.Robot.Joints[0];

            Assert.Equal("j1", dj.Name);
            Assert.Equal(UrdfJointType.Revolute, dj.Type);
            Assert.Equal("Concentric1", dj.MateName);
            Assert.True(dj.HasOrigin);
            Assert.Equal(0.123, dj.OriginX, 5);
            Assert.Equal(-0.456, dj.OriginY, 5);
            Assert.Equal(0.789, dj.OriginZ, 5);
            Assert.Equal(0, dj.AxisX, 5);
            Assert.Equal(0, dj.AxisY, 5);
            Assert.Equal(1, dj.AxisZ, 5);
            Assert.Equal(-1.5, dj.LimitLower);
            Assert.Equal(1.5, dj.LimitUpper);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void LegacyXmlWithoutOriginFields_DeserializesWithDefaults()
        {
            // Pre-D2 payload — JointDef has no OriginX/Y/Z, no HasOrigin,
            // no MatePointX/Y/Z, no HasMatePoint. RefCsName / RefAxisName
            // also absent. Must round-trip with HasOrigin == false and the
            // numeric origin fields == 0. Element order mirrors the
            // DataContract default ordering so the parser accepts it.
            // Sw2gzDocCodec writes/reads UTF-8 (see Sw2gzDocCodec.ToXmlString),
            // so the XML declaration must match — the .NET XmlReader rejects
            // declared-encoding/actual-stream-encoding mismatches outright.
            // Build the legacy XML by round-tripping a doc through the codec
            // first, then stripping out the new D2 elements. This guarantees
            // the namespace + DataContract element ordering match whatever
            // the live serializer expects today, instead of hand-typing.
            var seed = new Sw2gzDoc();
            seed.Robot.Links = new List<LinkDef>
            {
                new LinkDef { Name = "base_link", ParentName = "", ComponentIds = new List<string> { "base-1@asm" } },
                new LinkDef { Name = "arm",       ParentName = "base_link", ComponentIds = new List<string> { "arm-1@asm" } },
            };
            var seedJoint = new JointDef
            {
                Name       = "j_legacy",
                ParentLink = "base_link",
                ChildLink  = "arm",
                Type       = UrdfJointType.Revolute,
                MateName   = "OldMate",
                LimitLower = -1.0,
                LimitUpper =  1.0,
            };
            seedJoint.SetAxis(new Vector3(1, 0, 0));
            seed.Robot.Joints = new List<JointDef> { seedJoint };
            string legacyXml = Sw2gzDocCodec.ToXmlString(seed);

            // Strip every D2-introduced element so the parser sees a payload
            // that looks like it was written by the pre-D2 wizard. Drops:
            //   - HasOrigin / OriginX/Y/Z       (D2 auto-detect fields)
            //   - HasMatePoint / MatePointX/Y/Z (legacy mate-point fields)
            //   - RefCsName / RefAxisName       (D2 of the ref-CS plan)
            // The remaining XML must still round-trip with the new fields
            // defaulting to 0 / false.
            string[] toStrip =
            {
                "HasOrigin", "OriginX", "OriginY", "OriginZ",
                "HasMatePoint", "MatePointX", "MatePointY", "MatePointZ",
                "RefCsName", "RefAxisName",
            };
            foreach (string elem in toStrip)
                legacyXml = System.Text.RegularExpressions.Regex.Replace(
                    legacyXml, @"<" + elem + @"[^>]*/>|<" + elem + @">[^<]*</" + elem + ">", "");

            Sw2gzDoc dst = Sw2gzDocCodec.FromXmlString(legacyXml);
            Assert.NotNull(dst);
            Assert.Single(dst.Robot.Joints);
            JointDef dj = dst.Robot.Joints[0];

            // New auto-detect fields default to 0 / false.
            Assert.False(dj.HasOrigin);
            Assert.Equal(0.0, dj.OriginX);
            Assert.Equal(0.0, dj.OriginY);
            Assert.Equal(0.0, dj.OriginZ);

            // Legacy MatePoint fields also default to 0 / false.
            Assert.False(dj.HasMatePoint);
            Assert.Equal(0.0, dj.MatePointX);
            Assert.Equal(0.0, dj.MatePointY);
            Assert.Equal(0.0, dj.MatePointZ);

            // Originally-stored fields preserved.
            Assert.Equal("j_legacy", dj.Name);
            Assert.Equal(UrdfJointType.Revolute, dj.Type);
            Assert.Equal("OldMate", dj.MateName);
            Assert.Equal(1.0, dj.AxisX);
            Assert.Equal(0.0, dj.AxisY);
            Assert.Equal(0.0, dj.AxisZ);
            Assert.Equal(-1.0, dj.LimitLower);
            Assert.Equal(1.0, dj.LimitUpper);

            // OnDeserialized-coerced strings stay non-null even when absent.
            Assert.Equal(string.Empty, dj.RefCsName);
            Assert.Equal(string.Empty, dj.RefAxisName);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void SetOriginAndClearOrigin_ToggleFlag()
        {
            var j = new JointDef();
            Assert.False(j.HasOrigin);

            j.SetOrigin(new Vector3(1, 2, 3));
            Assert.True(j.HasOrigin);
            Assert.Equal(1.0, j.OriginX);
            Assert.Equal(2.0, j.OriginY);
            Assert.Equal(3.0, j.OriginZ);

            j.ClearOrigin();
            Assert.False(j.HasOrigin);
            Assert.Equal(0.0, j.OriginX);
            Assert.Equal(0.0, j.OriginY);
            Assert.Equal(0.0, j.OriginZ);
        }
    }
}
