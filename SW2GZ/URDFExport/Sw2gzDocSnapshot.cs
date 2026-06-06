/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Deep-clone + restore helper for Sw2gzDoc. Used by every stub PMP to honour the
cancel-rolls-back contract: on PMP open the live doc is snapshotted; on Cancel
the snapshot is copied back over the live doc; on OK the snapshot is discarded.

Hand-rolled deep clone (vs. serializer round-trip) — the doc tree is small,
this is on the UI thread, and reflection-free is simpler.

Pure / COM-free — source-linked into the test project.
*/
using System.Collections.Generic;

namespace SW2GZ.URDFExport
{
    public static class Sw2gzDocSnapshot
    {
        public static Sw2gzDoc Clone(Sw2gzDoc src)
        {
            if (src == null) return new Sw2gzDoc();
            return new Sw2gzDoc
            {
                Mode = src.Mode,
                Robot = new Sw2gzRobotConfig
                {
                    Links = new List<string>(src.Robot.Links),
                    Joints = new List<string>(src.Robot.Joints),
                    Sensors = new List<string>(src.Robot.Sensors),
                    UseRos2Control = src.Robot.UseRos2Control,
                },
                World = new Sw2gzWorldConfig
                {
                    Ground = src.World.Ground,
                    Assets = new List<string>(src.World.Assets),
                    PhysicsEngine = src.World.PhysicsEngine,
                    MaxStepSize = src.World.MaxStepSize,
                    RealTimeFactor = src.World.RealTimeFactor,
                },
                Asset = new Sw2gzAssetConfig
                {
                    BodyPart = src.Asset.BodyPart,
                    FrictionMu = src.Asset.FrictionMu,
                    IsStatic = src.Asset.IsStatic,
                },
            };
        }

        public static void Restore(Sw2gzDoc snapshot, Sw2gzDoc target)
        {
            if (snapshot == null || target == null) return;
            target.Mode = snapshot.Mode;
            target.Robot = new Sw2gzRobotConfig
            {
                Links = new List<string>(snapshot.Robot.Links),
                Joints = new List<string>(snapshot.Robot.Joints),
                Sensors = new List<string>(snapshot.Robot.Sensors),
                UseRos2Control = snapshot.Robot.UseRos2Control,
            };
            target.World = new Sw2gzWorldConfig
            {
                Ground = snapshot.World.Ground,
                Assets = new List<string>(snapshot.World.Assets),
                PhysicsEngine = snapshot.World.PhysicsEngine,
                MaxStepSize = snapshot.World.MaxStepSize,
                RealTimeFactor = snapshot.World.RealTimeFactor,
            };
            target.Asset = new Sw2gzAssetConfig
            {
                BodyPart = snapshot.Asset.BodyPart,
                FrictionMu = snapshot.Asset.FrictionMu,
                IsStatic = snapshot.Asset.IsStatic,
            };
        }
    }
}
