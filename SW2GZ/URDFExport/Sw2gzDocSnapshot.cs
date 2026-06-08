/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Deep-clone + restore helper for Sw2gzDoc. Used by every Create wizard to
honour the cancel-rolls-back contract: on PMP open the live doc is
snapshotted; on Cancel the snapshot is copied back over the live doc; on OK
the snapshot is discarded.

Hand-rolled deep clone — reflection-free, predictable, no serializer churn.

Pure / COM-free — source-linked into the test project.
*/
using System.Collections.Generic;
using SW2GZ.Build.Model;

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
                Robot = CloneRobot(src.Robot),
                World = CloneWorld(src.World),
                Asset = CloneAsset(src.Asset),
            };
        }

        public static void Restore(Sw2gzDoc snapshot, Sw2gzDoc target)
        {
            if (snapshot == null || target == null) return;
            target.Mode = snapshot.Mode;
            target.Robot = CloneRobot(snapshot.Robot);
            target.World = CloneWorld(snapshot.World);
            target.Asset = CloneAsset(snapshot.Asset);
        }

        private static Sw2gzRobotConfig CloneRobot(Sw2gzRobotConfig src)
        {
            var dst = new Sw2gzRobotConfig
            {
                Sensors = new List<string>(src.Sensors ?? new List<string>()),
                UseRos2Control = src.UseRos2Control,
            };
            if (src.Links != null)
                foreach (var l in src.Links) dst.Links.Add(CloneLink(l));
            if (src.Joints != null)
                foreach (var j in src.Joints) dst.Joints.Add(CloneJoint(j));
            return dst;
        }

        private static LinkDef CloneLink(LinkDef src) => new LinkDef
        {
            Name = src.Name,
            ParentName = src.ParentName,
            ComponentIds = new List<string>(src.ComponentIds ?? new List<string>()),
        };

        private static JointDef CloneJoint(JointDef src) => new JointDef
        {
            Name = src.Name,
            ParentLink = src.ParentLink,
            ChildLink = src.ChildLink,
            Type = src.Type,
            MateName = src.MateName,
            AxisX = src.AxisX,
            AxisY = src.AxisY,
            AxisZ = src.AxisZ,
            LimitLower = src.LimitLower,
            LimitUpper = src.LimitUpper,
            MatePointX = src.MatePointX,
            MatePointY = src.MatePointY,
            MatePointZ = src.MatePointZ,
            HasMatePoint = src.HasMatePoint,
        };

        private static Sw2gzWorldConfig CloneWorld(Sw2gzWorldConfig src) => new Sw2gzWorldConfig
        {
            Ground = src.Ground,
            Assets = new List<string>(src.Assets ?? new List<string>()),
            PhysicsEngine = src.PhysicsEngine,
            MaxStepSize = src.MaxStepSize,
            RealTimeFactor = src.RealTimeFactor,
        };

        private static Sw2gzAssetConfig CloneAsset(Sw2gzAssetConfig src) => new Sw2gzAssetConfig
        {
            BodyPart = src.BodyPart,
            FrictionMu = src.FrictionMu,
            IsStatic = src.IsStatic,
        };
    }
}
