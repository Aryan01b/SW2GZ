/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Linq;
using SW2GZ.Validate;
using Xunit;

namespace SW2GZ.Validate.Tests
{
    public class PackageNameCheckerTests
    {
        [Theory]
        [InlineData("good_name")]
        [InlineData("arm_2dof_description")]
        [InlineData("p1")]
        public void Check_ValidName_NoIssues(string name)
        {
            Assert.Empty(PackageNameChecker.Check(name));
        }

        [Theory]
        [InlineData("Bad-Name")]            // hyphens forbidden
        [InlineData("9starts_with_digit")]  // must start with letter
        [InlineData("has space")]
        [InlineData("UpperCase")]
        [InlineData("trailing_")]           // ament regex requires at least 2 chars after first
        public void Check_InvalidName_EmitsPkg001Error(string name)
        {
            var issues = PackageNameChecker.Check(name);
            Assert.Single(issues);
            var iss = issues.Single();
            Assert.Equal(IssueSeverity.Error, iss.Severity);
            Assert.Equal("PKG001", iss.Code);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Check_NullOrEmpty_EmitsPkg001Error(string name)
        {
            var issues = PackageNameChecker.Check(name);
            Assert.Single(issues);
            Assert.Equal("PKG001", issues.Single().Code);
        }

        [Fact]
        public void Check_InvalidName_LocationIsPackageXml()
        {
            var iss = PackageNameChecker.Check("Bad-Name").Single();
            Assert.Equal("package.xml", iss.Location);
        }

        [Fact]
        public void Check_InvalidName_MessageIncludesTheName()
        {
            var iss = PackageNameChecker.Check("Bad-Name").Single();
            Assert.Contains("Bad-Name", iss.Message);
        }
    }
}
