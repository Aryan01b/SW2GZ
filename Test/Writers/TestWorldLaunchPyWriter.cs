/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using SW2GZ.Ros2;
using Xunit;

namespace SW2GZ.Writers.Tests
{
    public class TestWorldLaunchPyWriter
    {
        [Fact]
        public void Write_StartsWithHeaderComment()
        {
            Assert.StartsWith("# ", WorldLaunchPyWriter.Write("my_env.sdf", false));
        }

        [Fact]
        public void Write_ResolvesPathsRelativeToLaunchFile_NotAment()
        {
            string py = WorldLaunchPyWriter.Write("my_env.sdf", false);
            // Standalone: our files resolve from __file__, not the ament share dir.
            Assert.Contains("os.path.realpath(__file__)", py);
            Assert.DoesNotContain("get_package_share_directory('my_env')", py);
        }

        [Fact]
        public void Write_LoadsTheGivenWorldFile()
        {
            string py = WorldLaunchPyWriter.Write("factory_cell.sdf", false);
            Assert.Contains("factory_cell.sdf", py);
            Assert.Contains("'-r ' + world_path", py);
        }

        [Fact]
        public void Write_IncludesGzSimAndParameterBridge()
        {
            string py = WorldLaunchPyWriter.Write("my_env.sdf", false);
            Assert.Contains("get_package_share_directory('ros_gz_sim')", py);
            Assert.Contains("gz_sim.launch.py", py);
            Assert.Contains("parameter_bridge", py);
            Assert.Contains("ros_gz_bridge.yaml", py);
            Assert.Contains("GZ_SIM_RESOURCE_PATH", py);
        }

        [Fact]
        public void Write_NoRobotSpawnOrControllers()
        {
            string py = WorldLaunchPyWriter.Write("my_env.sdf", true);
            Assert.DoesNotContain("'create'", py);
            Assert.DoesNotContain("robot_state_publisher", py);
            Assert.DoesNotContain("controller_manager", py);
        }

        [Fact]
        public void Write_NullWorldFile_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => WorldLaunchPyWriter.Write("  ", false));
        }
    }
}
