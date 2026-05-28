using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SW2GZ.Math;

namespace SW2GZ.Build
{
    public static class InertialAggregator
    {
        // Combine N parts at given poses into a single rigid-body MassProps at the assembly origin.
        // Steps: 1) total mass; 2) mass-weighted COM; 3) translate each inertia tensor to the new COM
        // via parallel-axis theorem, then sum. Rotation between part frame and assembly frame ignored
        // here — caller must pre-rotate the inertia tensors if needed (good enough for v2.0 since SW
        // returns inertia in assembly frame already).
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
                com += (float)(p.Mass / totalMass) * (f.Position + p.ComLocal);

            // Parallel-axis: I_parent = sum_i ( I_i + m_i * (||d_i||^2 * I - d_i * d_i^T) )
            var I = new double[3, 3];
            foreach (var (p, f) in parts)
            {
                Vector3 d = (f.Position + p.ComLocal) - com;
                double d2 = d.X * d.X + d.Y * d.Y + d.Z * d.Z;

                I[0, 0] += p.InertiaAtComLocal.M11 + p.Mass * (d2 - d.X * d.X);
                I[0, 1] += p.InertiaAtComLocal.M12 + p.Mass * (    - d.X * d.Y);
                I[0, 2] += p.InertiaAtComLocal.M13 + p.Mass * (    - d.X * d.Z);
                I[1, 0] += p.InertiaAtComLocal.M21 + p.Mass * (    - d.Y * d.X);
                I[1, 1] += p.InertiaAtComLocal.M22 + p.Mass * (d2 - d.Y * d.Y);
                I[1, 2] += p.InertiaAtComLocal.M23 + p.Mass * (    - d.Y * d.Z);
                I[2, 0] += p.InertiaAtComLocal.M31 + p.Mass * (    - d.Z * d.X);
                I[2, 1] += p.InertiaAtComLocal.M32 + p.Mass * (    - d.Z * d.Y);
                I[2, 2] += p.InertiaAtComLocal.M33 + p.Mass * (d2 - d.Z * d.Z);
            }

            return new MassProps(totalMass, com,
                new Matrix3(I[0,0], I[0,1], I[0,2],
                            I[1,0], I[1,1], I[1,2],
                            I[2,0], I[2,1], I[2,2]));
        }
    }
}
