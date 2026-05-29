/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using SW2GZ.Gz;
using Xunit;

namespace SW2GZ.Writers.Tests
{
    public class TestSdfWorldWriter
    {
        [Theory]
        [InlineData("gz-sim-physics-system")]
        [InlineData("gz-sim-sensors-system")]
        [InlineData("gz-sim-imu-system")]
        [InlineData("gz-sim-user-commands-system")]
        [InlineData("gz-sim-scene-broadcaster-system")]
        public void Write_UsesUnversionedHarmonicPluginFilenames_Bug4(string expectedFilename)
        {
            var sdf = SdfWorldWriter.Write(new SdfWorldInput("empty"));
            Assert.Contains(expectedFilename, sdf);
        }

        [Fact]
        public void Write_DoesNotUseGardenVersionedPlugins_Bug4()
        {
            var sdf = SdfWorldWriter.Write(new SdfWorldInput("empty"));
            Assert.DoesNotContain("gz-sim8-", sdf);
            Assert.DoesNotContain("gz-sim7-", sdf);
        }

        [Fact]
        public void Write_UsesHarmonicNamespace()
        {
            var sdf = SdfWorldWriter.Write(new SdfWorldInput("empty"));
            // Harmonic uses gz::sim, NOT ignition::gazebo
            Assert.Contains("gz::sim::systems::Physics", sdf);
            Assert.DoesNotContain("ignition::gazebo", sdf);
        }

        [Fact]
        public void Write_EmitsSdfVersion110()
        {
            var sdf = SdfWorldWriter.Write(new SdfWorldInput("empty"));
            Assert.Contains("<sdf version=\"1.10\">", sdf);
        }

        [Fact]
        public void Write_EmitsWorldNameFromInput()
        {
            var sdf = SdfWorldWriter.Write(new SdfWorldInput("my_test_world"));
            Assert.Contains("<world name=\"my_test_world\">", sdf);
        }

        [Fact]
        public void Write_EmitsPhysicsBlock()
        {
            var sdf = SdfWorldWriter.Write(new SdfWorldInput("empty"));
            Assert.Contains("<physics", sdf);
        }

        [Fact]
        public void Write_EmitsSunAndGroundPlane()
        {
            var sdf = SdfWorldWriter.Write(new SdfWorldInput("empty"));
            Assert.Contains("<light", sdf);          // sun
            Assert.Contains("ground_plane", sdf);    // ground
        }

        [Fact]
        public void Write_StartsWithXmlProlog()
        {
            var sdf = SdfWorldWriter.Write(new SdfWorldInput("empty"));
            Assert.StartsWith("<?xml version=\"1.0\"?>", sdf.TrimStart());
        }

        [Fact]
        public void Write_NullInput_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => SdfWorldWriter.Write(null));
        }

        [Fact]
        public void Write_NullWorldName_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => SdfWorldWriter.Write(new SdfWorldInput(null)));
        }

        [Fact]
        public void Write_WhitespaceWorldName_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => SdfWorldWriter.Write(new SdfWorldInput("  ")));
        }
    }
}
