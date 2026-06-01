using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SW2GZ.Math;

namespace SW2GZ.Build
{
    public static class InertialAggregator
    {
        // Combine N parts at given poses into a single rigid-body MassProps at the assembly origin.
        // Steps:
        //   1) total mass
        //   2) mass-weighted COM, with each part's local COM rotated into the assembly frame
        //      via R_f = Matrix3.FromQuaternion(frame.Rotation) before translation
        //   3) for each part, transform its inertia tensor from part frame to assembly frame
        //      as I_a = R_f · I_part · R_fᵀ, then translate to the combined COM via the
        //      parallel-axis theorem, and sum.
        // If frame.Rotation is Quaternion.Identity, R_f is Identity and the result is
        // byte-equivalent to the pre-P3 translation-only behavior.
        public static MassProps Combine(IReadOnlyList<(MassProps Props, Pose Frame)> parts)
        {
            if (parts == null) return new MassProps(0, Vector3.Zero, Matrix3.Identity);
            if (parts.Count == 0)
                return new MassProps(0, Vector3.Zero, Matrix3.Identity);

            double totalMass = parts.Sum(p => p.Props.Mass);
            if (totalMass <= 0)
                return new MassProps(0, Vector3.Zero, Matrix3.Identity);

            // Cache R_f per part: FromQuaternion + the rotated COM offset are
            // needed in both the COM pass and the parallel-axis pass.
            var rotations = new Matrix3[parts.Count];
            // Per-part rotated COM offset in assembly frame (double precision),
            // i.e. (f.Position + R_f * p.ComLocal). Reused for the parallel-axis d.
            var partComsX = new double[parts.Count];
            var partComsY = new double[parts.Count];
            var partComsZ = new double[parts.Count];

            double comX = 0.0, comY = 0.0, comZ = 0.0;
            for (int i = 0; i < parts.Count; i++)
            {
                var (p, f) = parts[i];
                var R_f = Matrix3.FromQuaternion(f.Rotation);
                rotations[i] = R_f;
                var (rx, ry, rz) = R_f.Mul((double)p.ComLocal.X, p.ComLocal.Y, p.ComLocal.Z);
                double pcx = f.Position.X + rx;
                double pcy = f.Position.Y + ry;
                double pcz = f.Position.Z + rz;
                partComsX[i] = pcx; partComsY[i] = pcy; partComsZ[i] = pcz;
                double w = p.Mass / totalMass;
                comX += w * pcx; comY += w * pcy; comZ += w * pcz;
            }
            var com = new Vector3((float)comX, (float)comY, (float)comZ);

            // Parallel-axis: I_parent = sum_i ( R_i I_i R_iᵀ + m_i * (||d_i||^2 * I - d_i * d_i^T) )
            var I = new double[3, 3];
            for (int i = 0; i < parts.Count; i++)
            {
                var (p, _) = parts[i];
                var R_f = rotations[i];
                var I_rot = R_f * p.InertiaAtComLocal * R_f.Transpose();

                // Offset from assembly COM to this part's COM, in double precision.
                double dx = partComsX[i] - comX;
                double dy = partComsY[i] - comY;
                double dz = partComsZ[i] - comZ;
                double d2 = dx * dx + dy * dy + dz * dz;

                I[0, 0] += I_rot.M11 + p.Mass * (d2 - dx * dx);
                I[0, 1] += I_rot.M12 + p.Mass * (    - dx * dy);
                I[0, 2] += I_rot.M13 + p.Mass * (    - dx * dz);
                I[1, 0] += I_rot.M21 + p.Mass * (    - dy * dx);
                I[1, 1] += I_rot.M22 + p.Mass * (d2 - dy * dy);
                I[1, 2] += I_rot.M23 + p.Mass * (    - dy * dz);
                I[2, 0] += I_rot.M31 + p.Mass * (    - dz * dx);
                I[2, 1] += I_rot.M32 + p.Mass * (    - dz * dy);
                I[2, 2] += I_rot.M33 + p.Mass * (d2 - dz * dz);
            }

            return new MassProps(totalMass, com,
                new Matrix3(I[0,0], I[0,1], I[0,2],
                            I[1,0], I[1,1], I[1,2],
                            I[2,0], I[2,1], I[2,2]));
        }
    }
}
