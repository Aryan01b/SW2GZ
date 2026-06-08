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
            const string legacyXml =
@"<?xml version=""1.0"" encoding=""utf-16""?>
<Sw2gzDoc xmlns:i=""http://www.w3.org/2001/XMLSchema-instance"">
  <Robot>
    <Links>
      <LinkDef>
        <Name>base_link</Name>
        <ParentName/>
        <ComponentIds><string>base-1@asm</string></ComponentIds>
      </LinkDef>
    </Links>
    <Joints>
      <JointDef>
        <Name>j_legacy</Name>
        <ParentLink>base_link</ParentLink>
        <ChildLink>arm</ChildLink>
        <Type>Revolute</Type>
        <MateName>OldMate</MateName>
        <AxisX>1</AxisX>
        <AxisY>0</AxisY>
        <AxisZ>0</AxisZ>
        <LimitLower>-1</LimitLower>
        <LimitUpper>1</LimitUpper>
      </JointDef>
    </Joints>
  </Robot>
</Sw2gzDoc>";

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
