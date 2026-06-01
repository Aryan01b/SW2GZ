using System;
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Math;
using SW2GZ.SwSurface;
using SW2GZ.SwSurface.Abstractions;
using Xunit;

namespace SW2GZ.Build.Tests
{
    public class UnitsScalerTests
    {
        private sealed class StubUnits : IUnitsContext
        {
            public double LengthScale { get; init; } = 1.0;
            public double MassScale { get; init; } = 1.0;
        }

        [Fact]
        public void Scale_Identity_NoChange()
        {
            var mp = new MassProps(2.5,
                new Vector3(0.1f, 0.2f, 0.3f),
                new Matrix3(1, 0.1, 0.2, 0.1, 2, 0.3, 0.2, 0.3, 3));
            var scaled = UnitsScaler.Scale(mp, new IdentityUnitsContext());

            Assert.Equal(2.5, scaled.Mass, 9);
            Assert.Equal(0.1, scaled.ComLocal.X, 5);
            Assert.Equal(0.2, scaled.ComLocal.Y, 5);
            Assert.Equal(0.3, scaled.ComLocal.Z, 5);
            Assert.Equal(1.0, scaled.InertiaAtComLocal.M11, 9);
            Assert.Equal(2.0, scaled.InertiaAtComLocal.M22, 9);
            Assert.Equal(3.0, scaled.InertiaAtComLocal.M33, 9);
            Assert.Equal(0.3, scaled.InertiaAtComLocal.M23, 9);
        }

        [Fact]
        public void Scale_LengthMmToMeters_ScalesComAndInertia()
        {
            var mp = new MassProps(2.0,
                new Vector3(1000f, 500f, -250f),  // mm
                new Matrix3(1000, 0, 0, 0, 1000, 0, 0, 0, 1000));  // mass·mm²
            var ctx = new StubUnits { LengthScale = 0.001, MassScale = 1.0 };

            var scaled = UnitsScaler.Scale(mp, ctx);

            Assert.Equal(2.0, scaled.Mass, 9);
            Assert.Equal(1.0, scaled.ComLocal.X, 5);
            Assert.Equal(0.5, scaled.ComLocal.Y, 5);
            Assert.Equal(-0.25, scaled.ComLocal.Z, 5);
            // inertia scaled by 1e-6
            Assert.Equal(1e-3, scaled.InertiaAtComLocal.M11, 9);
            Assert.Equal(1e-3, scaled.InertiaAtComLocal.M22, 9);
            Assert.Equal(1e-3, scaled.InertiaAtComLocal.M33, 9);
        }

        [Fact]
        public void Scale_MassGramsToKg_ScalesMassAndInertia()
        {
            var mp = new MassProps(500.0,
                new Vector3(1f, 2f, 3f),
                new Matrix3(4, 0, 0, 0, 5, 0, 0, 0, 6));
            var ctx = new StubUnits { LengthScale = 1.0, MassScale = 0.001 };

            var scaled = UnitsScaler.Scale(mp, ctx);

            Assert.Equal(0.5, scaled.Mass, 9);
            Assert.Equal(1f, scaled.ComLocal.X);
            Assert.Equal(2f, scaled.ComLocal.Y);
            Assert.Equal(3f, scaled.ComLocal.Z);
            Assert.Equal(0.004, scaled.InertiaAtComLocal.M11, 9);
            Assert.Equal(0.005, scaled.InertiaAtComLocal.M22, 9);
            Assert.Equal(0.006, scaled.InertiaAtComLocal.M33, 9);
        }

        [Fact]
        public void Scale_NegativeScale_Throws()
        {
            var mp = new MassProps(1.0, Vector3.Zero, Matrix3.Identity);
            var ctx = new StubUnits { LengthScale = -1.0, MassScale = 1.0 };
            Assert.Throws<ArgumentException>(() => UnitsScaler.Scale(mp, ctx));

            var ctx2 = new StubUnits { LengthScale = 1.0, MassScale = -0.5 };
            Assert.Throws<ArgumentException>(() => UnitsScaler.Scale(mp, ctx2));
        }

        [Fact]
        public void ScaleLength_Pose_ScalesPositionRotationUnchanged()
        {
            var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f);
            var p = new Pose(new Vector3(100f, -50f, 25f), rot);

            var scaled = UnitsScaler.ScaleLength(p, 0.01);

            Assert.Equal(1.0, scaled.Position.X, 5);
            Assert.Equal(-0.5, scaled.Position.Y, 5);
            Assert.Equal(0.25, scaled.Position.Z, 5);
            Assert.Equal(rot.X, scaled.Rotation.X);
            Assert.Equal(rot.Y, scaled.Rotation.Y);
            Assert.Equal(rot.Z, scaled.Rotation.Z);
            Assert.Equal(rot.W, scaled.Rotation.W);
        }

        [Fact]
        public void ScaleLength_NegativeScale_Throws()
        {
            var p = new Pose(Vector3.Zero, Quaternion.Identity);
            Assert.Throws<ArgumentException>(() => UnitsScaler.ScaleLength(p, -1.0));
            Assert.Throws<ArgumentException>(() => UnitsScaler.ScaleLength(p, 0.0));
        }
    }
}
