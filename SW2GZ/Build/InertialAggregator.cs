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
        //
        // The returned MassProps reports COM and inertia in the ASSEMBLY frame
        // (rotation + translation). URDF's <inertial> block wants both in the
        // link-local frame — use the (parts, linkAnchor) overload below for that.
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

        // Combine + rebase into the link-local frame defined by `linkAnchor`
        // (assembly-frame pose of the link's anchor part). URDF's <inertial>
        // wants COM and inertia expressed in the link's own frame; the base
        // Combine() returns both in the assembly frame, which is wrong as soon
        // as the link anchor is not at the assembly origin.
        //
        // Rebase math (R = R_linkAnchor):
        //   COM_link  = R^-1 · (COM_assembly − linkAnchor.Position)
        //   I_link    = R^-1 · I_assembly · R
        // Mass is invariant.
        //
        // When linkAnchor == Pose.Identity, R = I and the result equals
        // Combine(parts) byte-for-byte → existing goldens stay green.
        public static MassProps Combine(
            IReadOnlyList<(MassProps Props, Pose Frame)> parts,
            Pose linkAnchor)
        {
            MassProps assemblyFrame = Combine(parts);
            if (linkAnchor == null || linkAnchor == Pose.Identity) return assemblyFrame;
            if (assemblyFrame.Mass <= 0) return assemblyFrame;

            // R = link anchor rotation (assembly → link is Rᵀ; world rotates by R
            // to land in link orientation only if we are going link → world).
            // We need a vector expressed in the LINK frame, so we apply Rᵀ.
            Matrix3 R = Matrix3.FromQuaternion(linkAnchor.Rotation);
            Matrix3 Rinv = R.Transpose();

            // COM offset in assembly coords, then rotate into link frame.
            double dx = assemblyFrame.ComLocal.X - linkAnchor.Position.X;
            double dy = assemblyFrame.ComLocal.Y - linkAnchor.Position.Y;
            double dz = assemblyFrame.ComLocal.Z - linkAnchor.Position.Z;
            var (lx, ly, lz) = Rinv.Mul(dx, dy, dz);
            var comLink = new Vector3((float)lx, (float)ly, (float)lz);

            // I_link = R^-1 · I_assembly · R   (same tensor at the same point,
            // re-expressed in the rotated basis).
            Matrix3 Ilink = Rinv * assemblyFrame.InertiaAtComLocal * R;

            return new MassProps(assemblyFrame.Mass, comLink, Ilink);
        }
    }
}
