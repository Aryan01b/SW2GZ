/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Task 28: wires the actual SldWorks IMassProperty call.
The parameterless ctor preserves the original skeleton behaviour so that
the Moq test (SolidWorksImpl_NotYetWired_ThrowsNotImplemented) continues
to pass — Get() throws NotImplementedException when no SW handles are
present, unless the cache has been seeded.

SW_INTEROP is defined when building SW2GZ.csproj (which has the COM
references). It is NOT defined when building the xunit test project
(net8.0, no COM refs), allowing the same source file to compile in both.
*/
using System;
using System.Collections.Generic;
using SW2GZ.Build;
using SW2GZ.Exceptions;
using SW2GZ.SwSurface.Abstractions;

#if SW_INTEROP
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
#endif

namespace SW2GZ.SwSurface
{
    public sealed class SolidWorksMassProperties : IMassProperties
    {
        private readonly Dictionary<string, MassProps> _cache = new Dictionary<string, MassProps>();

#if SW_INTEROP
        private readonly SldWorks _swApp;
        private readonly AssemblyDoc _doc;
#endif

        // Skeleton ctor — preserves Moq test: Get() throws NotImplementedException.
        public SolidWorksMassProperties() { }

#if SW_INTEROP
        // Real ctor for production use.
        public SolidWorksMassProperties(SldWorks swApp, AssemblyDoc doc)
        {
            _swApp = swApp;
            _doc = doc;
        }
#endif

        public MassProps Get(string componentPathName)
        {
            if (_cache.TryGetValue(componentPathName, out var cached)) return cached;

#if SW_INTEROP
            if (_swApp == null || _doc == null)
#endif
                throw new NotImplementedException(
                    "SolidWorksMassProperties.Get() requires SW handles — pass them via constructor.");

#if SW_INTEROP
            // Walk the assembly tree to find the matching component.
            Component2 comp = FindComponent((object[])_doc.GetComponents(false), componentPathName);
            if (comp == null)
                throw new Sw2gzExportException(
                    "Component path not found in active assembly: " + componentPathName);

            IModelDoc2 model = null;
            ModelDocExtension ext = null;
            IMassProperty swMass = null;
            try
            {
                model = (IModelDoc2)comp.GetModelDoc2();
                ext = (ModelDocExtension)model.Extension;
                swMass = ext.CreateMassProperty();

                if (swMass.Mass <= 0)
                    throw new MaterialMissingException(componentPathName);

                double mass = swMass.Mass;
                double[] com = (double[])swMass.CenterOfMass;
                double[] moment = (double[])swMass.GetMomentOfInertia(
                    (int)swMassPropertyMoment_e.swMassPropertyMomentAboutCenterOfMass);

                var props = new MassProps(
                    mass,
                    new System.Numerics.Vector3((float)com[0], (float)com[1], (float)com[2]),
                    new SW2GZ.Math.Matrix3(
                        moment[0], moment[1], moment[2],
                        moment[3], moment[4], moment[5],
                        moment[6], moment[7], moment[8]));

                _cache[componentPathName] = props;
                return props;
            }
            finally
            {
                if (swMass != null) Marshal.ReleaseComObject(swMass);
                if (ext != null) Marshal.ReleaseComObject(ext);
                if (model != null) Marshal.ReleaseComObject(model);
            }
#endif
        }

        // Cache-seeder for tests (and eventually for the T28 SW-invocation hot path).
        internal void Seed(string path, MassProps props) => _cache[path] = props;

#if SW_INTEROP
        // Depth-first search through Component2 tree by GetPathName().
        // Internal so SolidWorksMeshTessellator can reuse it without duplication.
        internal static Component2 FindComponent(object[] topLevel, string targetPath)
        {
            if (topLevel == null) return null;
            foreach (object obj in topLevel)
            {
                Component2 comp = (Component2)obj;
                if (string.Equals(comp.GetPathName(), targetPath,
                        StringComparison.OrdinalIgnoreCase))
                    return comp;

                object[] children = (object[])comp.GetChildren();
                Component2 found = FindComponent(children, targetPath);
                if (found != null) return found;
            }
            return null;
        }
#endif
    }
}
