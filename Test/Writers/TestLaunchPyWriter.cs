/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using SW2GZ.Ros2;
using Xunit;

namespace SW2GZ.Writers.Tests
{
    public class TestLaunchPyWriter
    {
        // ─── GzSim ──────────────────────────────────────────────────────────

        [Fact]
        public void GzSim_SetsSystemPluginPath_Bug5()
        {
            var py = LaunchPyWriter.GzSim("my_pkg");
            Assert.Contains("SetEnvironmentVariable", py);
            Assert.Contains("GZ_SIM_SYSTEM_PLUGIN_PATH", py);
            Assert.Contains("get_package_prefix('gz_ros2_control')", py);
        }

        [Fact]
        public void GzSim_LaunchesParameterBridge_Bug7()
        {
            var py = LaunchPyWriter.GzSim("my_pkg");
            Assert.Contains("parameter_bridge", py);
            Assert.Contains("ros_gz_bridge", py);
            Assert.Contains("config/ros_gz_bridge.yaml", py);
        }

        [Fact]
        public void GzSim_SpawnNameMatchesPackage_Bug6()
        {
            var py = LaunchPyWriter.GzSim("my_pkg");
            Assert.Contains("'-name', 'my_pkg'", py);
        }

        [Fact]
        public void GzSim_DifferentPackageName_FlowsThroughEverywhere()
        {
            var py = LaunchPyWriter.GzSim("arm_2dof_description");
            Assert.Contains("'-name', 'arm_2dof_description'", py);
            Assert.Contains("get_package_share_directory('arm_2dof_description')", py);
            Assert.DoesNotContain("'-name', 'my_pkg'", py);
        }

        [Fact]
        public void GzSim_SetsResourcePath()
        {
            var py = LaunchPyWriter.GzSim("my_pkg");
            Assert.Contains("GZ_SIM_RESOURCE_PATH", py);
        }

        [Fact]
        public void GzSim_LoadsEmptyWorldFromPackage()
        {
            var py = LaunchPyWriter.GzSim("my_pkg");
            Assert.Contains("'worlds', 'empty.sdf'", py);
        }

        [Fact]
        public void GzSim_NullPackage_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => LaunchPyWriter.GzSim(null));
        }

        [Fact]
        public void GzSim_WhitespacePackage_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => LaunchPyWriter.GzSim("  "));
        }

        // ─── Display ────────────────────────────────────────────────────────

        [Fact]
        public void Display_RunsRobotStatePublisherAndRviz()
        {
            var py = LaunchPyWriter.Display("my_pkg");
            Assert.Contains("robot_state_publisher", py);
            Assert.Contains("rviz2", py);
            Assert.Contains("joint_state_publisher_gui", py);
        }

        [Fact]
        public void Display_PackageInterpolates()
        {
            var py = LaunchPyWriter.Display("my_pkg");
            Assert.Contains("get_package_share_directory('my_pkg')", py);
        }

        [Fact]
        public void Display_NullPackage_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => LaunchPyWriter.Display(null));
        }

        // ─── Ros2Control ────────────────────────────────────────────────────

        [Fact]
        public void Ros2Control_SpawnsJointStateBroadcasterAndTrajectoryController()
        {
            var py = LaunchPyWriter.Ros2Control("my_pkg");
            Assert.Contains("joint_state_broadcaster", py);
            Assert.Contains("joint_trajectory_controller", py);
            Assert.Contains("--controller-manager", py);
            Assert.Contains("/controller_manager", py);
        }

        [Fact]
        public void Ros2Control_SequencesViaOnProcessExit()
        {
            var py = LaunchPyWriter.Ros2Control("my_pkg");
            // joint_trajectory_controller waits for joint_state_broadcaster to be up
            Assert.Contains("RegisterEventHandler", py);
            Assert.Contains("OnProcessExit", py);
        }

        [Fact]
        public void Ros2Control_NullPackage_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => LaunchPyWriter.Ros2Control(null));
        }

        // ─── Generic ────────────────────────────────────────────────────────

        [Fact]
        public void AllOutputs_AreValidPython_StartWithHeaderComment()
        {
            Assert.StartsWith("# ", LaunchPyWriter.GzSim("p"));
            Assert.StartsWith("# ", LaunchPyWriter.Display("p"));
            Assert.StartsWith("# ", LaunchPyWriter.Ros2Control("p"));
        }

        // ─── GzAsset / GzWorld ──────────────────────────────────────────────

        [Fact]
        public void GzAsset_SetsResourcePathAndSpawnsModelFile()
        {
            string py = LaunchPyWriter.GzAsset("my_asset");
            Assert.Contains("GZ_SIM_RESOURCE_PATH", py);
            Assert.Contains("'models'", py);                 // resource path = <share>/models
            Assert.Contains("'empty.sdf'", py);              // empty world
            Assert.Contains("ros_gz_sim", py);
            Assert.Contains("'create'", py);                 // spawn the model
            Assert.Contains("'model.sdf'", py);              // spawn from the model.sdf file
            Assert.DoesNotContain("gz_ros2_control", py);
            Assert.DoesNotContain("controller_manager", py);
        }

        [Fact]
        public void GzWorld_SetsResourcePathAndLoadsWorld()
        {
            string py = LaunchPyWriter.GzWorld("my_world_pkg", "my_world_pkg");
            Assert.Contains("GZ_SIM_RESOURCE_PATH", py);
            Assert.Contains("'models'", py);
            Assert.Contains("my_world_pkg.sdf", py);         // loads the composed world
            Assert.Contains("ros_gz_sim", py);
            Assert.DoesNotContain("'create'", py);           // no spawn — model is in the world
            Assert.DoesNotContain("gz_ros2_control", py);
        }

        [Fact]
        public void GzAsset_NullPkg_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => LaunchPyWriter.GzAsset("  "));
        }
    }
}
