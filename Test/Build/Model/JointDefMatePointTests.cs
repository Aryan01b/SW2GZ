/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Tests that JointDef.MatePoint state round-trips correctly through the
SetMatePoint/ClearMatePoint helpers and survives serialization (the
DataMember attributes drive the SW2GZ Doc attribute round trip).
*/
using System.IO;
using System.Numerics;
using System.Runtime.Serialization;
using SW2GZ.Build.Model;
using Xunit;

namespace SW2GZ.Test.Build.Model
{
    public class JointDefMatePointTests
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void NewJointDef_HasNoMatePoint()
        {
            var j = new JointDef();
            Assert.False(j.HasMatePoint);
            Assert.Equal(0.0, j.MatePointX);
            Assert.Equal(0.0, j.MatePointY);
            Assert.Equal(0.0, j.MatePointZ);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void SetMatePoint_StoresVectorAndFlagsHasMatePoint()
        {
            var j = new JointDef();
            j.SetMatePoint(new Vector3(1.5f, -2.25f, 3f));

            Assert.True(j.HasMatePoint);
            Assert.Equal(1.5, j.MatePointX, 5);
            Assert.Equal(-2.25, j.MatePointY, 5);
            Assert.Equal(3.0, j.MatePointZ, 5);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ClearMatePoint_ResetsToZeroAndUnflags()
        {
            var j = new JointDef();
            j.SetMatePoint(new Vector3(1, 2, 3));
            j.ClearMatePoint();

            Assert.False(j.HasMatePoint);
            Assert.Equal(0.0, j.MatePointX);
            Assert.Equal(0.0, j.MatePointY);
            Assert.Equal(0.0, j.MatePointZ);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void DataContractRoundTrip_PreservesMatePoint()
        {
            var src = new JointDef
            {
                Name = "j0",
                ParentLink = "base",
                ChildLink = "arm",
            };
            src.SetMatePoint(new Vector3(0.5f, 1.5f, -2.5f));

            // Serialize then deserialize — exercises the DataMember attribute
            // wiring the SW Doc attribute uses for persistence.
            var ser = new DataContractSerializer(typeof(JointDef));
            using var ms = new MemoryStream();
            ser.WriteObject(ms, src);
            ms.Position = 0;
            var dst = (JointDef)ser.ReadObject(ms);

            Assert.True(dst.HasMatePoint);
            Assert.Equal(0.5, dst.MatePointX, 5);
            Assert.Equal(1.5, dst.MatePointY, 5);
            Assert.Equal(-2.5, dst.MatePointZ, 5);
        }
    }
}
