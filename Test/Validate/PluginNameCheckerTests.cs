/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using SW2GZ.Validate;
using Xunit;

namespace SW2GZ.Validate.Tests
{
    public class PluginNameCheckerTests
    {
        // ─── PLG001 — Garden versioned plugins rejected ────────────────────

        [Fact]
        public void Check_GardenVersionedPhysicsPlugin_EmitsPlg001()
        {
            var sdf = "<plugin filename=\"gz-sim8-physics-system\" name=\"x\"/>";
            Assert.Contains(PluginNameChecker.Check(sdf), i => i.Code == "PLG001" && i.Severity == IssueSeverity.Error);
        }

        [Theory]
        [InlineData("gz-sim7-")]
        [InlineData("gz-sim8-")]
        [InlineData("gz-sim9-")]
        public void Check_AnyVersionedGzSimPlugin_EmitsPlg001(string prefix)
        {
            var sdf = $"<plugin filename=\"{prefix}sensors-system\" name=\"x\"/>";
            Assert.Contains(PluginNameChecker.Check(sdf), i => i.Code == "PLG001");
        }

        [Fact]
        public void Check_HarmonicUnversionedPlugin_NoPlg001()
        {
            var sdf = "<plugin filename=\"gz-sim-physics-system\" name=\"gz::sim::systems::Physics\"/>";
            Assert.DoesNotContain(PluginNameChecker.Check(sdf), i => i.Code == "PLG001");
        }

        // ─── PLG002 — gz_ros2_control plugin must use correct class name ───

        [Fact]
        public void Check_WrongRos2ControlClassName_EmitsPlg002_Bug8()
        {
            var xml = "<plugin filename=\"gz_ros2_control-system\" name=\"gz_ros2_control::system\"/>";
            Assert.Contains(PluginNameChecker.Check(xml), i => i.Code == "PLG002");
        }

        [Fact]
        public void Check_CorrectRos2ControlClassName_NoPlg002()
        {
            var xml = "<plugin filename=\"gz_ros2_control-system\" name=\"gz_ros2_control::GazeboSimROS2ControlPlugin\"/>";
            Assert.DoesNotContain(PluginNameChecker.Check(xml), i => i.Code == "PLG002");
        }

        [Fact]
        public void Check_NoRos2ControlPluginAtAll_NoPlg002()
        {
            var xml = "<robot><gazebo><plugin filename=\"something_else\" name=\"x\"/></gazebo></robot>";
            Assert.DoesNotContain(PluginNameChecker.Check(xml), i => i.Code == "PLG002");
        }

        // ─── Combined / null ───────────────────────────────────────────────

        [Fact]
        public void Check_BothBugsPresent_EmitsBothCodes()
        {
            var xml = "<plugin filename=\"gz-sim8-physics-system\"/><plugin filename=\"gz_ros2_control-system\" name=\"gz_ros2_control::system\"/>";
            var issues = PluginNameChecker.Check(xml);
            Assert.Contains(issues, i => i.Code == "PLG001");
            Assert.Contains(issues, i => i.Code == "PLG002");
        }

        [Fact]
        public void Check_NullInput_NoIssues()
        {
            Assert.Empty(PluginNameChecker.Check(null));
        }

        [Fact]
        public void Check_EmptyString_NoIssues()
        {
            Assert.Empty(PluginNameChecker.Check(""));
        }
    }
}
