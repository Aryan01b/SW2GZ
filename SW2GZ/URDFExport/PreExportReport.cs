/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Pre-export validation summary (F15). Pure POCO that takes pre-computed
inputs so it stays SW-free and unit-testable. ExportHelper walks the
URDFRobot tree and feeds the counts in.
*/
using System.Collections.Generic;
using System.Text;

namespace SW2GZ.URDFExport
{
    public class PreExportReport
    {
        public string RobotName { get; set; }
        public int LinkCount { get; set; }
        public int JointCount { get; set; }
        public double TotalMassKg { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
        public string Summary { get; set; }

        public static PreExportReport Generate(string robotName, int linkCount, int jointCount, double totalMassKg)
        {
            var report = new PreExportReport
            {
                RobotName = robotName,
                LinkCount = linkCount,
                JointCount = jointCount,
                TotalMassKg = totalMassKg,
            };

            if (linkCount == 0) report.Warnings.Add("Robot has no links.");
            if (totalMassKg <= 0) report.Warnings.Add("Total mass is zero or negative.");

            var sb = new StringBuilder();
            sb.AppendLine("SW2GZ pre-export summary for '" + robotName + "':");
            sb.AppendLine("  " + linkCount + " link(s), " + jointCount + " joint(s)");
            sb.AppendLine("  Total mass: " + totalMassKg.ToString("F3") + " kg");
            if (report.Warnings.Count > 0)
            {
                sb.AppendLine("Warnings:");
                foreach (var w in report.Warnings) sb.AppendLine("  - " + w);
            }
            report.Summary = sb.ToString();
            return report;
        }
    }
}
