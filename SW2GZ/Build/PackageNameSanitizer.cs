using System.Text.RegularExpressions;

namespace SW2GZ.Build
{
    public sealed record SanitizedName(string Value, bool Changed, string Original);

    public static class PackageNameSanitizer
    {
        // ament/ROS 2 package names must match ^[a-z][a-z0-9_]*[a-z0-9]$ — start
        // with a lowercase letter, end with a letter/digit, no leading/trailing
        // underscore, length >= 2.
        public static SanitizedName Sanitize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new SanitizedName("unnamed_package", true, raw ?? "");

            string s = raw.ToLowerInvariant();
            s = Regex.Replace(s, "[^a-z0-9_]", "_");
            // collapse repeats and strip leading/trailing underscores
            s = Regex.Replace(s, "_+", "_").Trim('_');
            if (s.Length == 0)
                s = "unnamed_package";
            else if (!Regex.IsMatch(s, "^[a-z]"))
                s = "pkg_" + s;            // a leading digit/underscore isn't allowed
            if (s.Length < 2)
                s = s + "_pkg";           // ament requires at least 2 chars

            return new SanitizedName(s, s != raw, raw);
        }
    }
}
