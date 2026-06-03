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

            return new Sw2gzPipeline(mass, walker, tess, appearances).Run(
                config.OutputFolder, config.PackageName, config.Author, config.Email, config.License,
                System.Array.Empty<SensorDef>(), modelOnly: true);
        }

        public static string WorkspacePath(string outputFolder, string packageName) =>
            System.IO.Path.Combine(outputFolder, PackageNameSanitizer.Sanitize(packageName).Value + "_ws");
    }
}
#endif
