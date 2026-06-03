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
        [InlineData("3dof_arm", "pkg_3dof_arm", true)]     // leading digit → letter-prefixed
        [InlineData("",         "unnamed_package", true)]
        [InlineData("name-",     "name",          true)]   // trailing hyphen → trimmed
        [InlineData("a---b",     "a_b",           true)]   // collapse repeated separators
        [InlineData("123",       "pkg_123",       true)]   // all digits → letter-prefixed
        [InlineData("!!!",       "unnamed_package", true)] // all special → falls through to empty
        [InlineData("Good_Name", "good_name",     true)]   // case-only change still flagged Changed
        public void Sanitize_ProducesAmentSafeName(string raw, string expected, bool changed)
        {
            var s = PackageNameSanitizer.Sanitize(raw);
            Assert.Equal(expected, s.Value);
            Assert.Equal(changed, s.Changed);
            Assert.Equal(raw, s.Original);
        }
    }
}
