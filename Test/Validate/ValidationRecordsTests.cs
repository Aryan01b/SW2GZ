/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Collections.Generic;
using System.Linq;
using SW2GZ.Validate;
using Xunit;

namespace SW2GZ.Validate.Tests
{
    public class ValidationRecordsTests
    {
        private static ValidationIssue Err(string code = "X1") =>
            new ValidationIssue(IssueSeverity.Error, code, "boom", "loc");
        private static ValidationIssue Warn(string code = "W1") =>
            new ValidationIssue(IssueSeverity.Warning, code, "hmm", "loc");

        [Fact]
        public void Report_HasErrors_TrueWhenAnyError()
        {
            var report = new ValidationReport(new[] { Warn(), Err() });
            Assert.True(report.HasErrors);
        }

        [Fact]
        public void Report_HasErrors_FalseWhenOnlyWarnings()
        {
            var report = new ValidationReport(new[] { Warn(), Warn("W2") });
            Assert.False(report.HasErrors);
        }

        [Fact]
        public void Report_HasErrors_FalseWhenEmpty()
        {
            var report = new ValidationReport(System.Array.Empty<ValidationIssue>());
            Assert.False(report.HasErrors);
        }

        [Fact]
        public void Report_Errors_FiltersBySeverity()
        {
            var report = new ValidationReport(new[] { Warn(), Err("E1"), Err("E2"), Warn("W2") });
            var errs = report.Errors.ToList();
            Assert.Equal(2, errs.Count);
            Assert.All(errs, e => Assert.Equal(IssueSeverity.Error, e.Severity));
            Assert.Contains(errs, e => e.Code == "E1");
            Assert.Contains(errs, e => e.Code == "E2");
        }

        [Fact]
        public void Report_Warnings_FiltersBySeverity()
        {
            var report = new ValidationReport(new[] { Warn("W1"), Err(), Warn("W2") });
            var warns = report.Warnings.ToList();
            Assert.Equal(2, warns.Count);
            Assert.All(warns, w => Assert.Equal(IssueSeverity.Warning, w.Severity));
        }

        [Fact]
        public void Issue_CarriesAllFields()
        {
            var iss = new ValidationIssue(IssueSeverity.Error, "PKG001", "Bad name", "package.xml");
            Assert.Equal(IssueSeverity.Error, iss.Severity);
            Assert.Equal("PKG001", iss.Code);
            Assert.Equal("Bad name", iss.Message);
            Assert.Equal("package.xml", iss.Location);
        }
    }
}
