using SW2GZ.Ros2;
using Xunit;

namespace Test.UI
{
    public class StackConfigMapTests
    {
        [Theory]
        [InlineData(0, ActuationBackend.None)]
        [InlineData(1, ActuationBackend.GzPlugin)]
        [InlineData(2, ActuationBackend.Ros2Control)]
        public void RadioIndex_RoundTrips(int idx, ActuationBackend backend)
        {
            Assert.Equal(backend, StackConfigMap.BackendForRadioIndex(idx));
            Assert.Equal(idx, StackConfigMap.RadioIndexForBackend(backend));
        }
    }
}
