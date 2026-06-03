using SW2GZ.Ros2;
using Xunit;

namespace Test.URDFExport
{
    public class StackFlyoutModelTests
    {
        [Fact]
        public void IsChecked_ReflectsProfile()
        {
            var p = new StackProfile { GzSim = true, Actuation = ActuationBackend.Ros2Control, SensorsEnabled = false };
            Assert.True(StackFlyoutModel.IsChecked(p, StackFlyoutItem.GazeboSim));
            Assert.True(StackFlyoutModel.IsChecked(p, StackFlyoutItem.ActuationRos2Control));
            Assert.False(StackFlyoutModel.IsChecked(p, StackFlyoutItem.ActuationGzPlugin));
            Assert.False(StackFlyoutModel.IsChecked(p, StackFlyoutItem.ActuationNone));
            Assert.False(StackFlyoutModel.IsChecked(p, StackFlyoutItem.Sensors));
        }

        [Fact]
        public void Apply_ActuationRadio_SetsBackend_DeselectsOthers()
        {
            var p = StackProfile.Default();
            StackProfile r = StackFlyoutModel.Apply(p, StackFlyoutItem.ActuationGzPlugin);
            Assert.Equal(ActuationBackend.GzPlugin, r.Actuation);
            Assert.True(StackFlyoutModel.IsChecked(r, StackFlyoutItem.ActuationGzPlugin));
            Assert.False(StackFlyoutModel.IsChecked(r, StackFlyoutItem.ActuationRos2Control));
        }

        [Fact]
        public void Apply_Toggle_FlipsBool_DoesNotMutateInput()
        {
            var p = StackProfile.Default();
            StackProfile r = StackFlyoutModel.Apply(p, StackFlyoutItem.Sensors);
            Assert.True(r.SensorsEnabled);
            Assert.False(p.SensorsEnabled);

            StackProfile r2 = StackFlyoutModel.Apply(p, StackFlyoutItem.GazeboSim);
            Assert.False(r2.GzSim);
            Assert.True(p.GzSim);
        }

        [Fact]
        public void Label_NonEmptyForEveryItem()
        {
            foreach (StackFlyoutItem it in System.Enum.GetValues(typeof(StackFlyoutItem)))
                Assert.False(string.IsNullOrWhiteSpace(StackFlyoutModel.Label(it)));
        }
    }
}
