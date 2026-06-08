/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

D2 guards that JointDef.RefCsName + RefAxisName round-trip through
DataContract serialization, AND that JointDef payloads serialized
before D2 (i.e. without these fields) still deserialize, with the
fields defaulting to empty strings.
*/
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using SW2GZ.Build.Model;
using Xunit;

namespace SW2GZ.Test.Build.Model
{
    public class JointDefRefGeometryTests
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void NewJointDef_HasEmptyRefGeometryNames()
        {
            var j = new JointDef();
            Assert.Equal(string.Empty, j.RefCsName);
            Assert.Equal(string.Empty, j.RefAxisName);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void DataContractRoundTrip_PreservesRefGeometryNames()
        {
            var src = new JointDef
            {
                Name = "j_shoulder",
                ParentLink = "base_link",
                ChildLink  = "shoulder",
                RefCsName   = "joint1_cs",
                RefAxisName = "joint1_axis",
            };

            var ser = new DataContractSerializer(typeof(JointDef));
            using var ms = new MemoryStream();
            ser.WriteObject(ms, src);
            ms.Position = 0;
            var dst = (JointDef)ser.ReadObject(ms);

            Assert.Equal("joint1_cs",   dst.RefCsName);
            Assert.Equal("joint1_axis", dst.RefAxisName);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void LegacyPayloadWithoutFields_DeserializesWithEmptyStrings()
        {
            // Hand-rolled XML in the DataContract shape that pre-D2 JointDef
            // would have written (no RefCsName / RefAxisName elements). The
            // post-D2 deserializer must still accept it and leave the new
            // fields at their defaults — required for existing SW Attribute
            // checkpoints to load.
            const string legacyXml =
                "<JointDef xmlns=\"\" xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                  "<AxisX>0</AxisX><AxisY>0</AxisY><AxisZ>1</AxisZ>" +
                  "<ChildLink>shoulder</ChildLink>" +
                  "<HasMatePoint>false</HasMatePoint>" +
                  "<LimitLower i:nil=\"true\"/>" +
                  "<LimitUpper i:nil=\"true\"/>" +
                  "<MateName>concentric1</MateName>" +
                  "<MatePointX>0</MatePointX><MatePointY>0</MatePointY><MatePointZ>0</MatePointZ>" +
                  "<Name>j0</Name>" +
                  "<ParentLink>base_link</ParentLink>" +
                  "<Type>Revolute</Type>" +
                "</JointDef>";

            var ser = new DataContractSerializer(typeof(JointDef));
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(legacyXml));
            var dst = (JointDef)ser.ReadObject(ms);

            Assert.Equal("j0", dst.Name);
            Assert.Equal("concentric1", dst.MateName);
            // The new fields default to empty.
            Assert.Equal(string.Empty, dst.RefCsName);
            Assert.Equal(string.Empty, dst.RefAxisName);
        }
    }
}
