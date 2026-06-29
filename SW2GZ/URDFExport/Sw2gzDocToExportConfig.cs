/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Pure (COM-free) shim that adapts a Sw2gzDoc (v2.1.0 schema) + wizard meta
form values into a Sw2gzExportConfig (the legacy schema Sw2gzPipeline.Run
still consumes). Built so the new Export wizard can drive the existing
pipeline without a wholesale pipeline-side rewrite — that broader refactor
is part of the backend-wiring plan, not this UI work.

Mode mapping:
    Sw2gzMode.Robot → ExportMode.RobotPackage
    Sw2gzMode.World → ExportMode.SdfWorld
    Sw2gzMode.Asset → ExportMode.SdfModel
*/
using System.Collections.Generic;
using SW2GZ.Build.Model;
using SW2GZ.Ros2;

namespace SW2GZ.URDFExport
{
    public sealed class ExportMetaInput
    {
        public string OutputFolder { get; set; } = string.Empty;
        public string PackageName  { get; set; } = string.Empty;
        public string Author       { get; set; } = string.Empty;
        public string Email        { get; set; } = string.Empty;
        public string License      { get; set; } = string.Empty;
    }

    public static class Sw2gzDocToExportConfig
    {
        public static Sw2gzExportConfig Bridge(Sw2gzDoc doc, ExportMetaInput meta)
        {
            var cfg = new Sw2gzExportConfig
            {
                Mode = MapMode(doc?.Mode ?? Sw2gzMode.Robot),
                OutputFolder = meta?.OutputFolder ?? string.Empty,
                PackageName  = meta?.PackageName  ?? string.Empty,
                Author       = meta?.Author       ?? string.Empty,
                Email        = meta?.Email        ?? string.Empty,
                License      = meta?.License      ?? string.Empty,
                Links  = CloneLinks(doc?.Robot?.Links),
                Joints = CloneJoints(doc?.Robot?.Joints),
                Stacks = StackProfile.Default(),
            };

            // World mode — carry the Create-World picks through to the exporter.
            // (memory world-mode-dev: the first attempt failed here — Bridge
            // dropped World config so the exporter saw empty picks.)
            var world = doc?.World;
            if (world != null)
            {
                cfg.WorldGround        = world.Ground ?? string.Empty;
                cfg.WorldAssets        = new List<string>(world.Assets ?? new List<string>());
                cfg.WorldPhysicsEngine = world.PhysicsEngine ?? "ode";
                cfg.WorldMaxStepSize   = world.MaxStepSize;
                cfg.WorldRealTimeFactor = world.RealTimeFactor;
                cfg.WorldScene         = (world.Scene ?? new Sw2gzWorldSceneConfig()).Clone();
                cfg.WorldInitialView   = cfg.WorldScene.InitialView ?? "iso";
                cfg.WorldSensorPlugins = (world.SensorPlugins ?? new Sw2gzWorldSensorsConfig()).Clone();
            }

            // Asset mode — carry the Create-Asset picks through to the exporter.
            var asset = doc?.Asset;
            if (asset != null)
            {
                cfg.AssetBodyPart   = asset.BodyPart ?? string.Empty;
                cfg.AssetFrictionMu = asset.FrictionMu;
                cfg.AssetIsStatic   = asset.IsStatic;
            }
            return cfg;
        }

        public static ExportMode MapMode(Sw2gzMode mode)
        {
            switch (mode)
            {
                case Sw2gzMode.World: return ExportMode.SdfWorld;
                case Sw2gzMode.Asset: return ExportMode.SdfModel;
                default:              return ExportMode.RobotPackage;
            }
        }

        private static List<LinkDef> CloneLinks(List<LinkDef> src)
        {
            var list = new List<LinkDef>();
            if (src == null) return list;
            foreach (var l in src)
            {
                list.Add(new LinkDef
                {
                    Name = l.Name,
                    ParentName = l.ParentName,
                    ComponentIds = new List<string>(l.ComponentIds ?? new List<string>()),
                });
            }
            return list;
        }

        private static List<JointDef> CloneJoints(List<JointDef> src)
        {
            var list = new List<JointDef>();
            if (src == null) return list;
            foreach (var j in src)
            {
                list.Add(new JointDef
                {
                    Name = j.Name,
                    ParentLink = j.ParentLink,
                    ChildLink = j.ChildLink,
                    Type = j.Type,
                    MateName = j.MateName,
                    AxisX = j.AxisX,
                    AxisY = j.AxisY,
                    AxisZ = j.AxisZ,
                    LimitLower = j.LimitLower,
                    LimitUpper = j.LimitUpper,
                    MatePointX = j.MatePointX,
                    MatePointY = j.MatePointY,
                    MatePointZ = j.MatePointZ,
                    HasMatePoint = j.HasMatePoint,
                });
            }
            return list;
        }
    }
}
