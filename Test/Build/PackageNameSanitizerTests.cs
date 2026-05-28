using SW2GZ.Build;
using Xunit;

namespace SW2GZ.Build.Tests
{
    public class PackageNameSanitizerTests
    {
        [Theory]
        [InlineData("arm-2dof_description", "arm_2dof_description", true)]
        [InlineData("Two Bot", "two_bot", true)]
        [InlineData("MyRobot",  "myrobot", true)]
        [InlineData("good_name", "good_name", false)]
        [InlineData("3dof_arm", "_3dof_arm", true)]
        [InlineData("",         "unnamed_package", true)]
        public void Sanitize_ProducesAmentSafeName(string raw, string expected, bool changed)
        {
            var s = PackageNameSanitizer.Sanitize(raw);
            Assert.Equal(expected, s.Value);
            Assert.Equal(changed, s.Changed);
            Assert.Equal(raw, s.Original);
        }
    }
}
