/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using SW2GZ.Gz;
using SW2GZ.Ros2;
using Xunit;

namespace SW2GZ.Test.Writers
{
    public class TestSdfWorldWriter : WriterTestBase
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void WritesEmptySdfWithSdfVersionMatchingProfile()
        {
            new SdfWorldWriter(new TargetProfile { Gz = GzVersion.Harmonic }, "empty")
                .WriteEmptyWorld(TempDir, "empty.sdf");
            Assert.True(Exists("empty.sdf"));
            var doc = LoadXml("empty.sdf");
            Assert.Equal("sdf", doc.Root.Name.LocalName);
            Assert.Equal("1.10", doc.Root.Attribute("version").Value);
            var world = doc.Root.Element("world");
            Assert.NotNull(world);
            Assert.Equal("empty", world.Attribute("name").Value);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void EmptyWorldContainsPluginsSunAndGround()
        {
            new SdfWorldWriter(new TargetProfile { Gz = GzVersion.Harmonic }, "empty")
                .WriteEmptyWorld(TempDir, "empty.sdf");
            var txt = ReadAllText("empty.sdf");
            Assert.Contains("gz-sim8-physics-system", txt);
            Assert.Contains("gz-sim8-scene-broadcaster-system", txt);
            Assert.Contains("<light", txt);
            Assert.Contains("ground_plane", txt);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void EmptyWorldForFortressUsesIgnition()
        {
            new SdfWorldWriter(new TargetProfile { Gz = GzVersion.Fortress }, "empty")
                .WriteEmptyWorld(TempDir, "empty.sdf");
            var txt = ReadAllText("empty.sdf");
            Assert.Contains("ignition-gazebo6-physics-system", txt);
            var doc = LoadXml("empty.sdf");
            Assert.Equal("1.9", doc.Root.Attribute("version").Value);
        }
    }
}
