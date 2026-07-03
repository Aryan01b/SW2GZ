/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Runs the bare-model export from a saved Sw2gzExportConfig (links + joints + the
meta supplied at export time). Used by the ribbon Export command, separate from
the Create-Model wizard which only saves the structure.
*/
#if SW_INTEROP
using SolidWorks.Interop.sldworks;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.SwSurface;

namespace SW2GZ.URDFExport
{
    public static class Sw2gzModelExporter
    {
        public static SW2GZ.Validate.ValidationReport Run(SldWorks swApp, ModelDoc2 model, Sw2gzExportConfig config)
            => RunCore(swApp, model, config, config.OutputFolder);

        // outputDirOverride lets the Preview path direct the same pipeline to a
        // temp directory without touching the user's chosen output folder. The
        // rest of `config` (package name, author, mode, links, joints, coord
        // convention) is honoured unchanged.
        internal static SW2GZ.Validate.ValidationReport RunCore(
            SldWorks swApp, ModelDoc2 model, Sw2gzExportConfig config, string outputDirOverride)
        {
            // Part document → export the whole part as a Gz asset model (no
            // assembly / components). Forced to Asset mode regardless of the
            // saved doc mode.
            if (model.GetType() == (int)SolidWorks.Interop.swconst.swDocumentTypes_e.swDocPART)
            {
                var partTess = new SolidWorksMeshTessellator(swApp, (PartDoc)model);
                if (string.IsNullOrWhiteSpace(config.AssetBodyPart))
                    config.AssetBodyPart = "part";   // ignored by the part tessellator; keeps the exporter's guard happy
                var partRot = SwToRosRotation.Build(config.SwUpAxis, config.SwForwardAxis);
                return Sw2gzAssetExporter.Export(partTess, config, outputDirOverride, partRot);
            }

            var tess = new SolidWorksMeshTessellator(swApp, (AssemblyDoc)model);

            // World mode = environment of static CAD assets, NOT a kinematic
            // robot — route to the dedicated world exporter before the
            // robot-only walk/mass/joint pipeline runs. The whole-scene SW→ROS
            // rotation rides on each model's <pose>.
            if (config.Mode == SW2GZ.Ros2.ExportMode.SdfWorld)
            {
                var worldCoord = new CoordinateConvention(
                    SwToRosRotation.Build(config.SwUpAxis, config.SwForwardAxis), LengthScale: 1.0);
                (double wr, double wp, double wy) = worldCoord.SwToRos.ToRpy();
                return Sw2gzWorldExporter.Export(tess, config, outputDirOverride, wr, wp, wy);
            }

            // Asset mode = a single part exported as a reusable Gz model dir.
            if (config.Mode == SW2GZ.Ros2.ExportMode.SdfModel)
            {
                var rot = SwToRosRotation.Build(config.SwUpAxis, config.SwForwardAxis);
                return Sw2gzAssetExporter.Export(tess, config, outputDirOverride, rot);
            }

            // Robot mode v3 — minimal flat/fixed exporter (validating cut for the
            // rebuild): one link per top-level component, all Fixed to the first,
            // real relative joint pose (see Sw2gzRobotExporter for the math).
            var massProps = new SolidWorksMassProperties(swApp, (AssemblyDoc)model);
            var poses = new SolidWorksComponentPoses((AssemblyDoc)model);
            var robotRot = SwToRosRotation.Build(config.SwUpAxis, config.SwForwardAxis);
            return Sw2gzRobotExporter.Export(tess, massProps, poses, config, outputDirOverride, robotRot);
        }

        public static string WorkspacePath(string outputFolder, string packageName) =>
            System.IO.Path.Combine(outputFolder, PackageNameSanitizer.Sanitize(packageName).Value + "_ws");
    }
}
#endif
