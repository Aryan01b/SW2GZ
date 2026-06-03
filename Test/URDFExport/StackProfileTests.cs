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
        public void NewInstance_DefaultsMatchFactoryDefault()
        {
            var bare = new StackProfile();
            Assert.True(bare.GzSim);
            Assert.Equal(ActuationBackend.Ros2Control, bare.Actuation);
        }
    }
}
