/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.IO;
using System.Text;
using SW2GZ.Ros2;

namespace SW2GZ.Gz
{
    public class SdfWorldWriter
    {
        private readonly TargetProfile _profile;
        private readonly string _worldName;

        public SdfWorldWriter(TargetProfile profile, string worldName)
        {
            _profile = profile;
            _worldName = worldName;
        }

        public void WriteEmptyWorld(string outputDir, string fileName)
        {
            Directory.CreateDirectory(outputDir);
            string sdfVer = TargetProfile.SdfVersion[_profile.Gz];

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\"?>");
            sb.AppendLine($"<sdf version=\"{sdfVer}\">");
            sb.AppendLine($"  <world name=\"{_worldName}\">");
            sb.Append(GzPluginTags.WorldSystemBlock(_profile));
            sb.Append(SdfPhysicsBlock.Default());
            sb.Append(SdfPhysicsBlock.Sun());
            sb.Append(SdfPhysicsBlock.GroundPlane());
            sb.AppendLine("  </world>");
            sb.AppendLine("</sdf>");
            File.WriteAllText(Path.Combine(outputDir, fileName), sb.ToString());
        }
    }
}
