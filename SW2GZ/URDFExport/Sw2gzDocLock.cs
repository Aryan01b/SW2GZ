/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Decides whether a Sw2gzDoc is "locked into" its current mode — i.e. the user
has authored content under the active mode and switching mode would discard
that work. The ribbon's mode pills (Robot/World/Asset) disable while locked,
so the user can see the active mode but can't switch to another.

A doc is considered locked when ANY of the active mode's content lists is
non-empty (Robot.Links/Joints/Sensors, World.Ground/Assets, Asset.BodyPart).
We don't check the "config" fields (UseRos2Control, PhysicsEngine etc.) —
those are defaults that don't represent user content.

Pure / COM-free — source-linked into the test project.
*/
namespace SW2GZ.URDFExport
{
    public static class Sw2gzDocLock
    {
        public static bool IsLocked(Sw2gzDoc doc)
        {
            if (doc == null) return false;
            switch (doc.Mode)
            {
                case Sw2gzMode.Robot:
                    return (doc.Robot?.Links?.Count   ?? 0) > 0
                        || (doc.Robot?.Joints?.Count  ?? 0) > 0
                        || (doc.Robot?.Sensors?.Count ?? 0) > 0;
                case Sw2gzMode.World:
                    return !string.IsNullOrEmpty(doc.World?.Ground)
                        || (doc.World?.Assets?.Count ?? 0) > 0;
                case Sw2gzMode.Asset:
                    return !string.IsNullOrEmpty(doc.Asset?.BodyPart);
                default:
                    return false;
            }
        }
    }
}
