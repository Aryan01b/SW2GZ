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

            Vector3 com = Vector3.Zero;
            foreach (var (p, f) in parts)
            {
                var R_f = Matrix3.FromQuaternion(f.Rotation);
                Vector3 comInAssembly = f.Position + R_f.Mul(p.ComLocal);
                com += (float)(p.Mass / totalMass) * comInAssembly;
            }

            // Parallel-axis: I_parent = sum_i ( R_i I_i R_iᵀ + m_i * (||d_i||^2 * I - d_i * d_i^T) )
            var I = new double[3, 3];
            foreach (var (p, f) in parts)
            {
                var R_f = Matrix3.FromQuaternion(f.Rotation);
                var I_rot = R_f * p.InertiaAtComLocal * R_f.Transpose();

                Vector3 d = (f.Position + R_f.Mul(p.ComLocal)) - com;
                double d2 = d.X * d.X + d.Y * d.Y + d.Z * d.Z;

                I[0, 0] += I_rot.M11 + p.Mass * (d2 - d.X * d.X);
                I[0, 1] += I_rot.M12 + p.Mass * (    - d.X * d.Y);
                I[0, 2] += I_rot.M13 + p.Mass * (    - d.X * d.Z);
                I[1, 0] += I_rot.M21 + p.Mass * (    - d.Y * d.X);
                I[1, 1] += I_rot.M22 + p.Mass * (d2 - d.Y * d.Y);
                I[1, 2] += I_rot.M23 + p.Mass * (    - d.Y * d.Z);
                I[2, 0] += I_rot.M31 + p.Mass * (    - d.Z * d.X);
                I[2, 1] += I_rot.M32 + p.Mass * (    - d.Z * d.Y);
                I[2, 2] += I_rot.M33 + p.Mass * (d2 - d.Z * d.Z);
            }

            return new MassProps(totalMass, com,
                new Matrix3(I[0,0], I[0,1], I[0,2],
                            I[1,0], I[1,1], I[1,2],
                            I[2,0], I[2,1], I[2,2]));
        }
    }
}
