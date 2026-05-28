using System.Text.RegularExpressions;

namespace SW2GZ.Build
{
    public sealed record SanitizedName(string Value, bool Changed, string Original);

    public static class PackageNameSanitizer
    {
        // ament/ROS 2: ^[a-z][a-z0-9_]+$
        public static SanitizedName Sanitize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new SanitizedName("unnamed_package", true, raw ?? "");

            string s = raw.ToLowerInvariant();
            s = Regex.Replace(s, "[^a-z0-9_]", "_");
            // collapse repeats and strip trailing underscores
            s = Regex.Replace(s, "_+", "_").TrimEnd('_');
            if (s.Length == 0) s = "unnamed_package";
            // prefix with _ if starts with a digit
            if (!Regex.IsMatch(s, "^[a-z_]"))
                s = "_" + s;

            return new SanitizedName(s, s != raw, raw);
        }
    }
}
