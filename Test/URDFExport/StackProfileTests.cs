using SW2GZ.Ros2;
using Xunit;

namespace Test.URDFExport
{
    public class StackProfileTests
    {
        [Fact]
        public void Default_IsFullRos2ControlStack()
        {
            var p = StackProfile.Default();
            Assert.True(p.GzSim);
            Assert.Equal(ActuationBackend.Ros2Control, p.Actuation);
            Assert.False(p.SensorsEnabled);
        }

        [Fact]
        public void ModelOnly_DisablesActuation()
        {
            var p = StackProfile.ModelOnly();
            Assert.Equal(ActuationBackend.None, p.Actuation);
            Assert.False(p.SensorsEnabled);
        }

        [Fact]
        public void CopyCtor_CopiesAllFields()
        {
            var src = new StackProfile {
                GzSim = false, Actuation = ActuationBackend.GzPlugin, SensorsEnabled = true,
                Bridge = new BridgePlan { Clock = false, Tf = false, JointStates = false, CmdVel = true, Odom = true }
            };
            var copy = new StackProfile(src);
            Assert.False(copy.GzSim);
            Assert.Equal(ActuationBackend.GzPlugin, copy.Actuation);
            Assert.True(copy.SensorsEnabled);
            Assert.True(copy.Bridge.CmdVel);
            Assert.True(copy.Bridge.Odom);
            Assert.False(copy.Bridge.Clock);
            copy.Bridge.CmdVel = false;
            Assert.True(src.Bridge.CmdVel); // deep copy — source unaffected
        }

        [Fact]
        public void Default_BridgeHasSaneDefaults()
        {
            var p = StackProfile.Default();
            Assert.True(p.Bridge.Clock);
            Assert.True(p.Bridge.Tf);
            Assert.True(p.Bridge.JointStates);
            Assert.False(p.Bridge.CmdVel);
            Assert.False(p.Bridge.Odom);
        }

        [Fact]
        public void NewInstance_DefaultsMatchFactoryDefault()
        {
            var bare = new StackProfile();
            Assert.True(bare.GzSim);
            Assert.Equal(ActuationBackend.Ros2Control, bare.Actuation);
        }
    }
}
