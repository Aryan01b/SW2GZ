/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Per-user default values for the Export dialog: Author, Email, License, and the
last-used Output folder. Stored in HKCU\Software\SW2GZ\UserDefaults so the
first Export of a brand-new assembly defaults to the user's identity instead
of empty strings. The per-doc Sw2gzExportConfig still wins when populated.

Best-effort: registry I/O is wrapped in try/catch so a corrupted/locked
registry key never blocks the Export flow. PackageName is intentionally NOT
persisted here — it should always come from the doc.
*/
#if SW_INTEROP
using System;
using Microsoft.Win32;

namespace SW2GZ.Utilities
{
    public static class Sw2gzUserDefaults
    {
        private const string RegistryPath = @"Software\SW2GZ\UserDefaults";

        public sealed class Values
        {
            public string Author { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string License { get; set; } = string.Empty;
            public string LastOutputFolder { get; set; } = string.Empty;
        }

        public static Values Load()
        {
            var v = new Values();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    if (key == null) return v;
                    v.Author = (string)key.GetValue("Author", string.Empty);
                    v.Email = (string)key.GetValue("Email", string.Empty);
                    v.License = (string)key.GetValue("License", string.Empty);
                    v.LastOutputFolder = (string)key.GetValue("LastOutputFolder", string.Empty);
                }
            }
            catch (Exception)
            {
                // Registry unavailable / corrupted — fall through with empty defaults.
            }
            return v;
        }

        public static void Save(Values v)
        {
            if (v == null) return;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    if (key == null) return;
                    key.SetValue("Author", v.Author ?? string.Empty);
                    key.SetValue("Email", v.Email ?? string.Empty);
                    key.SetValue("License", v.License ?? string.Empty);
                    key.SetValue("LastOutputFolder", v.LastOutputFolder ?? string.Empty);
                }
            }
            catch (Exception)
            {
                // Best-effort persist — silent on failure (e.g. HKCU write blocked).
            }
        }
    }
}
#endif
