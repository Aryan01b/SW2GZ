using SW2GZ.Build;
using Xunit;

namespace SW2GZ.Build.Tests
{
    public class RosNameSanitizerTests
    {
        [Theory]
        // Clean names pass through unchanged (case preserved) — critical: keeps golden output identical.
        [InlineData("base_link", "base_link", false)]
        [InlineData("arm1",      "arm1",      false)]
        [InlineData("link",      "link",      false)]
        [InlineData("MixedCase", "MixedCase", false)]   // case preserved, unlike package sanitizer
        [InlineData("_private",  "_private",  false)]
        // Dirty names get repaired.
        [InlineData("Base Link", "Base_Link", true)]    // space → underscore, case kept
        [InlineData("joint-1",   "joint_1",   true)]    // hyphen → underscore
        [InlineData("arm@2",     "arm_2",     true)]    // special char
        [InlineData("3dof",      "_3dof",     true)]    // leading digit → prefixed
        [InlineData("link__name","link_name", true)]    // collapse repeated separators
        [InlineData("tip_",      "tip",       true)]    // trailing underscore trimmed
        [InlineData("",          "unnamed",   true)]    // blank → fallback
        [InlineData("###",       "unnamed",   true)]    // all special → empty → fallback
        public void Sanitize_ProducesValidIdentifier(string raw, string expected, bool changed)
        {
            var s = RosNameSanitizer.Sanitize(raw);
            Assert.Equal(expected, s.Value);
            Assert.Equal(changed, s.Changed);
            Assert.Equal(raw, s.Original);
        }
    }
}
