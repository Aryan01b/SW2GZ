/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using SW2GZ.Gz;
using SW2GZ.Ros2;
using Xunit;

namespace SW2GZ.Test.Writers
{
    public class TestGzPluginTags
    {
        [Theory]
        [InlineData(GzVersion.Fortress, "ignition-gazebo6")]
        [InlineData(GzVersion.Harmonic, "gz-sim8")]
        [InlineData(GzVersion.Ionic,    "gz-sim9")]
        [Trait("Category", "Unit")]
        public void WorldSystemBlockReferencesCorrectSimLib(GzVersion gz, string expectedLib)
        {
            var profile = new TargetProfile { Gz = gz };
            string xml = GzPluginTags.WorldSystemBlock(profile);
            Assert.Contains($"filename=\"{expectedLib}-physics-system\"", xml);
            Assert.Contains($"filename=\"{expectedLib}-sensors-system\"", xml);
            Assert.Contains($"filename=\"{expectedLib}-scene-broadcaster-system\"", xml);
            Assert.Contains($"filename=\"{expectedLib}-user-commands-system\"", xml);
        }

        [Theory]
        [InlineData(GzVersion.Fortress, "ign_ros2_control-system")]
        [InlineData(GzVersion.Harmonic, "gz_ros2_control-system")]
        [InlineData(GzVersion.Ionic,    "gz_ros2_control-system")]
        [Trait("Category", "Unit")]
        public void ControlPluginBlockUsesCorrectLib(GzVersion gz, string expectedLib)
        {
            var profile = new TargetProfile { Gz = gz };
            string xml = GzPluginTags.Ros2ControlPluginBlock(profile, "controllers.yaml");
            Assert.Contains($"filename=\"{expectedLib}\"", xml);
            Assert.Contains("controllers.yaml", xml);
        }
    }
}
