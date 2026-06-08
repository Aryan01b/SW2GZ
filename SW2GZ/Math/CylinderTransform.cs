/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Pure (COM-free) helper that transforms a SolidWorks cylindrical-face
CylinderParams block (PART-local frame) into the ASSEMBLY frame using
a Component2.Transform2.ArrayData layout.

CylinderParams (from ISurface.CylinderParams when ISurface.IsCylinder()):
  [0..2] origin    — a point on the cylinder axis, PART-local.
  [3..5] direction — axis unit vector,              PART-local.
  [6]    radius (ignored here).

Component transform (16 doubles, row-major rotation + translation, matches
MathTransformPose / SolidWorksAssemblyWalker.TransformByComponent):
  [0..8]  rotation 3x3 row-major
  [9..11] translation
  [12]    scale (ignored)
  [13..15] padding

Origin is rotated AND translated; direction is rotated only (no translation
applied to a direction vector). Lifted into its own file so AutoJointResolver
can stay #if SW_INTEROP while the math is unit-testable off-COM.
*/
using System.Numerics;

namespace SW2GZ.Math
{
    public static class CylinderTransform
    {
        /// <summary>
        /// Transforms (cp[0..2] origin, cp[3..5] direction) from a part-local
        /// frame into the assembly frame using a 16-double Component2.Transform2
        /// ArrayData. Returns (Vector3.Zero, Vector3.Zero) when either input is
        /// malformed so callers can detect a degenerate transform without
        /// branching on nulls everywhere.
        /// </summary>
        public static (Vector3 originAssembly, Vector3 directionAssembly)
            TransformCylinderToAssembly(double[] arrayData, double[] cp)
        {
            if (arrayData == null || arrayData.Length < 12) return (Vector3.Zero, Vector3.Zero);
            if (cp == null || cp.Length < 6) return (Vector3.Zero, Vector3.Zero);

            double[] d = arrayData;
            // Origin: full transform (rotate + translate).
            float ox = (float)(d[0] * cp[0] + d[1] * cp[1] + d[2] * cp[2] + d[9]);
            float oy = (float)(d[3] * cp[0] + d[4] * cp[1] + d[5] * cp[2] + d[10]);
            float oz = (float)(d[6] * cp[0] + d[7] * cp[1] + d[8] * cp[2] + d[11]);

            // Direction: rotation only.
            float dx = (float)(d[0] * cp[3] + d[1] * cp[4] + d[2] * cp[5]);
            float dy = (float)(d[3] * cp[3] + d[4] * cp[4] + d[5] * cp[5]);
            float dz = (float)(d[6] * cp[3] + d[7] * cp[4] + d[8] * cp[5]);

            var dir = new Vector3(dx, dy, dz);
            if (dir.LengthSquared() > 1e-12f) dir = Vector3.Normalize(dir);

            return (new Vector3(ox, oy, oz), dir);
        }
    }
}
