using System;
using System.Numerics;
using SW2GZ.Math;
using SW2GZ.SwSurface.Abstractions;

namespace SW2GZ.Build
{
    /// <summary>
    /// Pure helpers that scale MassProps / Pose values from source units to SI.
    /// Not yet wired into Sw2gzPipeline — P3-units integration (which requires a
    /// SolidWorks workstation to exercise) is deferred. Schema + helper only.
    /// </summary>
    public static class UnitsScaler
    {
        /// <summary>
        /// Scale mass (×MassScale), COM length (×LengthScale), and inertia
        /// (×MassScale·LengthScale² — inertia has units mass·length²) from
        /// the source unit system to SI.
        /// </summary>
        public static MassProps Scale(MassProps mp, IUnitsContext units)
        {
            if (mp == null) throw new ArgumentNullException(nameof(mp));
            if (units == null) throw new ArgumentNullException(nameof(units));
            double ls = units.LengthScale;
            double ms = units.MassScale;
            if (ls <= 0) throw new ArgumentException("LengthScale must be > 0.", nameof(units));
            if (ms <= 0) throw new ArgumentException("MassScale must be > 0.", nameof(units));

            double inertiaFactor = ms * ls * ls;
            var com = new Vector3((float)(mp.ComLocal.X * ls),
                                  (float)(mp.ComLocal.Y * ls),
                                  (float)(mp.ComLocal.Z * ls));
            var I = mp.InertiaAtComLocal;
            var scaledInertia = new Matrix3(
                I.M11 * inertiaFactor, I.M12 * inertiaFactor, I.M13 * inertiaFactor,
                I.M21 * inertiaFactor, I.M22 * inertiaFactor, I.M23 * inertiaFactor,
                I.M31 * inertiaFactor, I.M32 * inertiaFactor, I.M33 * inertiaFactor);

            return new MassProps(mp.Mass * ms, com, scaledInertia);
        }

        /// <summary>
        /// Scale a Pose's translation by lengthScale. Rotation is preserved
        /// (orientation is unit-independent).
        /// </summary>
        public static Pose ScaleLength(Pose p, double lengthScale)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (lengthScale <= 0) throw new ArgumentException("lengthScale must be > 0.", nameof(lengthScale));

            var pos = new Vector3((float)(p.Position.X * lengthScale),
                                  (float)(p.Position.Y * lengthScale),
                                  (float)(p.Position.Z * lengthScale));
            return new Pose(pos, p.Rotation);
        }
    }
}
