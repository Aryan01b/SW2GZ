/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using SW2GZ.Ros2;
using Xunit;

namespace SW2GZ.Test.Writers
{
    public class TestTargetProfile
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void DefaultProfileIsRobotPackageJazzyHarmonic()
        {
            var p = new TargetProfile();
            Assert.Equal(ExportMode.RobotPackage, p.Mode);
            Assert.Equal(Ros2Distro.Jazzy, p.Ros2);
            Assert.Equal(GzVersion.Harmonic, p.Gz);
        }

        [Theory]
        [InlineData(GzVersion.Fortress)]
        [InlineData(GzVersion.Harmonic)]
        [InlineData(GzVersion.Ionic)]
        [Trait("Category", "Unit")]
        public void EveryGzVersionHasPrefixSimLibControlPluginSdfVersion(GzVersion gz)
        {
            Assert.True(TargetProfile.GzPackagePrefix.ContainsKey(gz));
            Assert.True(TargetProfile.SimPluginLib.ContainsKey(gz));
            Assert.True(TargetProfile.Ros2ControlPlugin.ContainsKey(gz));
            Assert.True(TargetProfile.SdfVersion.ContainsKey(gz));
            Assert.False(string.IsNullOrEmpty(TargetProfile.GzPackagePrefix[gz]));
        }

        [Theory]
        [InlineData(Ros2Distro.Humble,  GzVersion.Fortress)]
        [InlineData(Ros2Distro.Jazzy,   GzVersion.Harmonic)]
        [InlineData(Ros2Distro.Kilted,  GzVersion.Ionic)]
        [InlineData(Ros2Distro.Rolling, GzVersion.Harmonic)]
        [Trait("Category", "Unit")]
        public void PairingMatchesOSRFReleases(Ros2Distro distro, GzVersion expectedGz)
        {
            Assert.Equal(expectedGz, TargetProfile.Pairing[distro]);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void RosGzSimPackageNameFormatsCorrectly()
        {
            var p = new TargetProfile { Ros2 = Ros2Distro.Jazzy, Gz = GzVersion.Harmonic };
            Assert.Equal("ros-jazzy-ros-gz-sim", p.RosGzSimPackageName());

            var p2 = new TargetProfile { Ros2 = Ros2Distro.Humble, Gz = GzVersion.Fortress };
            Assert.Equal("ros-humble-ros-ign-sim", p2.RosGzSimPackageName());
        }
    }
}
