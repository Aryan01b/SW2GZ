/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Reads a component's raw Component2.Transform2 pose (assembly-frame rotation +
translation), COLUMN-major (see memory sw-mathtransform-column-major).

SW_INTEROP is defined when building SW2GZ.csproj (COM references); NOT
defined when building the xunit test project, so the same source compiles
in both (mirrors SolidWorksMassProperties / SolidWorksMeshTessellator).
*/
using System;
using System.Numerics;
using SW2GZ.Math;
using SW2GZ.SwSurface.Abstractions;

#if SW_INTEROP
using SolidWorks.Interop.sldworks;
#endif

namespace SW2GZ.SwSurface
{
    public sealed class SolidWorksComponentPoses : IComponentPoses
    {
#if SW_INTEROP
        private readonly AssemblyDoc _doc;
#endif

        // Skeleton ctor — preserves the NotImplementedException-when-unwired
        // convention shared by SolidWorksMeshTessellator / SolidWorksMassProperties.
        public SolidWorksComponentPoses() { }

#if SW_INTEROP
        public SolidWorksComponentPoses(AssemblyDoc doc) { _doc = doc; }
#endif

        public (Matrix3 Rotation, Vector3 Translation) GetPose(string componentPathName)
        {
#if SW_INTEROP
            if (_doc == null)
#endif
                throw new NotImplementedException(
                    "SolidWorksComponentPoses.GetPose() requires an assembly doc — pass it via constructor.");

#if SW_INTEROP
            Component2 comp = SolidWorksMassProperties.FindComponent(
                (object[])_doc.GetComponents(false), componentPathName);
            if (comp == null)
                throw new SW2GZ.Exceptions.Sw2gzExportException(
                    "Component path not found in active assembly: " + componentPathName);

            MathTransform xform = comp.Transform2;
            double[] d = xform?.ArrayData as double[];
            if (d == null || d.Length < 12) return (Matrix3.Identity, Vector3.Zero);

            var r = new Matrix3(
                d[0], d[3], d[6],
                d[1], d[4], d[7],
                d[2], d[5], d[8]);
            var t = new Vector3((float)d[9], (float)d[10], (float)d[11]);
            return (r, t);
#endif
        }
    }
}
