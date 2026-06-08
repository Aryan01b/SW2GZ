/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Pure (COM-free) extraction of a Pose from the 16-double array a SolidWorks
MathTransform exposes via MathTransform.ArrayData. Lifted out of
WizardAssemblyWalker.GetComponentPose so the same row-major + Shepperd's
quaternion code is reused by SwJointPoseReader for Reference Coordinate
System transforms (Component2.Extension.GetCoordinateSystemTransformByName).

SW Transform2.ArrayData layout (matches every other consumer in this
codebase: SolidWorksAssemblyWalker.RotateByComponent, SolidWorksMeshTessellator's
vertex bake-in, WizardAssemblyWalker.GetComponentPose):
  [0..8]  rotation 3x3 row-major
  [9..11] translation
  [12]    scale (ignored)
  [13..15] padding
*/
using System.Numerics;

namespace SW2GZ.Math
{
    public static class MathTransformPose
    {
        /// Extract a Pose from a 16-double SW MathTransform array. Returns
        /// Pose.Identity for null / short arrays so callers can treat
        /// "unknown" inputs gracefully.
        public static Pose FromArrayData(double[] d)
        {
            if (d == null || d.Length < 12) return Pose.Identity;

            // Translation is direct.
            var translation = new Vector3((float)d[9], (float)d[10], (float)d[11]);

            // Quaternion from the 3x3 rotation block, row-major. Shepperd's
            // method handles the sign-ambiguity case without trig.
            float m00 = (float)d[0], m01 = (float)d[1], m02 = (float)d[2];
            float m10 = (float)d[3], m11 = (float)d[4], m12 = (float)d[5];
            float m20 = (float)d[6], m21 = (float)d[7], m22 = (float)d[8];
            float trace = m00 + m11 + m22;
            float qx, qy, qz, qw;
            if (trace > 0f)
            {
                float s = (float)System.Math.Sqrt(trace + 1f) * 2f;   // 4 * qw
                qw = 0.25f * s;
                qx = (m21 - m12) / s;
                qy = (m02 - m20) / s;
                qz = (m10 - m01) / s;
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = (float)System.Math.Sqrt(1f + m00 - m11 - m22) * 2f;
                qw = (m21 - m12) / s;
                qx = 0.25f * s;
                qy = (m01 + m10) / s;
                qz = (m02 + m20) / s;
            }
            else if (m11 > m22)
            {
                float s = (float)System.Math.Sqrt(1f + m11 - m00 - m22) * 2f;
                qw = (m02 - m20) / s;
                qx = (m01 + m10) / s;
                qy = 0.25f * s;
                qz = (m12 + m21) / s;
            }
            else
            {
                float s = (float)System.Math.Sqrt(1f + m22 - m00 - m11) * 2f;
                qw = (m10 - m01) / s;
                qx = (m02 + m20) / s;
                qy = (m12 + m21) / s;
                qz = 0.25f * s;
            }
            return new Pose(translation, Quaternion.Normalize(new Quaternion(qx, qy, qz, qw)));
        }
    }
}
