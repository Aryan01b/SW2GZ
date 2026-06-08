/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Pure-domain tests for AutoJointResolved — the DTO returned by AutoJointResolver
in the SW build. The COM-bound resolver itself (Mate2 / IFace2 walking)
cannot run in the off-COM test project, but the Resolved aggregation +
defaults are pure C# and live in SW2GZ\SwSurface\AutoJointResolved.cs.
*/
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.SwSurface;
using Xunit;

namespace SW2GZ.Test.SwSurface
{
    public class AutoJointResolverPureTests
    {
        [Fact]
        public void Defaults_AreNotFoundAndFixed()
        {
            var r = new AutoJointResolved();
            Assert.False(r.Found);
            Assert.Equal(string.Empty, r.MateName);
            Assert.Equal(MateKind.Fixed, r.Kind);
            Assert.Equal(Vector3.Zero, r.AxisAssembly);
            Assert.False(r.OriginAssembly.HasValue);
            Assert.Null(r.LimitLower);
            Assert.Null(r.LimitUpper);
        }

        [Fact]
        public void Aggregates_FoundConcentricWithCylinderOrigin()
        {
            // Mimic what AutoJointResolver fills in when a concentric mate
            // with a cylindrical face is detected: kind = Continuous, axis +
            // origin from the cylinder, MateName populated, no limits.
            var r = new AutoJointResolved
            {
                Found          = true,
                MateName       = "Concentric1",
                Kind           = MateKind.Continuous,
                AxisAssembly   = new Vector3(0, 0, 1),
                OriginAssembly = new Vector3(0.1f, 0.2f, 0.3f),
            };
            Assert.True(r.Found);
            Assert.Equal("Concentric1", r.MateName);
            Assert.Equal(MateKind.Continuous, r.Kind);
            Assert.Equal(new Vector3(0, 0, 1), r.AxisAssembly);
            Assert.True(r.OriginAssembly.HasValue);
            Assert.Equal(new Vector3(0.1f, 0.2f, 0.3f), r.OriginAssembly.Value);
        }

        [Fact]
        public void Aggregates_LimitMateCarriesRange()
        {
            // Limit-distance mate → Prismatic with a finite range. AxisAssembly
            // can stay Zero when no cylinder face is on the mate (legal
            // degenerate case — JointDef stays at the cached axis, if any).
            var r = new AutoJointResolved
            {
                Found      = true,
                MateName   = "LimitDistance1",
                Kind       = MateKind.Prismatic,
                LimitLower = -0.05,
                LimitUpper =  0.15,
            };
            Assert.Equal(MateKind.Prismatic, r.Kind);
            Assert.Equal(-0.05, r.LimitLower);
            Assert.Equal( 0.15, r.LimitUpper);
            Assert.Equal(Vector3.Zero, r.AxisAssembly);
            Assert.False(r.OriginAssembly.HasValue);
        }
    }
}
