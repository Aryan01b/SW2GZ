using System.Text.RegularExpressions;

namespace SW2GZ.Build
{
    // Sanitizes URDF/SDF link, joint, and frame names to a safe identifier.
    //
    // Unlike PackageNameSanitizer (ament package names: lowercase ^[a-z][a-z0-9_]+$),
    // link/joint/tf-frame names are CASE-SENSITIVE in ROS — "base_link" and "Base_Link"
    // are distinct frames — so case is preserved here. Output matches ^[A-Za-z_][A-Za-z0-9_]*$.
    //
    // This is the single chokepoint that guarantees names entering the writers are valid
    // identifiers, which also makes the per-writer XML escaping defense-in-depth rather than
    // the only line of defense.
    public static class RosNameSanitizer
    {
        public static SanitizedName Sanitize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new SanitizedName("unnamed", true, raw ?? "");

            // Replace anything outside [A-Za-z0-9_] with '_', collapse runs, trim trailing '_'.
            string s = Regex.Replace(raw, "[^A-Za-z0-9_]", "_");
            s = Regex.Replace(s, "_+", "_").TrimEnd('_');
            if (s.Length == 0) s = "unnamed";
            // Identifiers cannot start with a digit — prefix with '_'.
            if (!Regex.IsMatch(s, "^[A-Za-z_]"))
                s = "_" + s;

            return new SanitizedName(s, s != raw, raw);
        }
    }
}
