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
        {
            var mass = new SolidWorksMassProperties(swApp, (AssemblyDoc)model);
            var tess = new SolidWorksMeshTessellator(swApp, (AssemblyDoc)model);
            var walker = new WizardAssemblyWalker((AssemblyDoc)model, config.Links, config.Joints);
            var appearances = new DefaultAppearanceSource();

            // Drive the export from the assembly's persisted stack selection
            // (Stacks). Defensive default: an older config that somehow carries a
            // null profile falls back to the full stack rather than throwing.
            SW2GZ.Ros2.StackProfile profile = config.Stacks ?? SW2GZ.Ros2.StackProfile.Default();

            // ExportMode (config.Mode) selects the artifact — Robot Package vs
            // gz asset/world; the StackProfile selects which stacks emit. gz
            // modes ignore the actuation backend (no ros2_control).
            return new Sw2gzPipeline(mass, walker, tess, appearances).Run(
                config.OutputFolder, config.PackageName, config.Author, config.Email, config.License,
                System.Array.Empty<SensorDef>(), profile, config.Mode);
        }

        public static string WorkspacePath(string outputFolder, string packageName) =>
            System.IO.Path.Combine(outputFolder, PackageNameSanitizer.Sanitize(packageName).Value + "_ws");
    }
}
#endif
