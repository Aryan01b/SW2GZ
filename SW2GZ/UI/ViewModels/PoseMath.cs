/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8+ — pure-C# pose helpers for the wizard. The domain Pose stores a
Quaternion, but the UI edits poses as xyz + roll/pitch/yaw (radians). This
util converts rpy → Quaternion (ZYX Tait-Bryan, the inverse of
Matrix3.ToRpy()) so the sensors page can build a SensorDef.Pose from the
edited fields, and exposes a small forward/up axis readout so the user can
sanity-check sensor orientation without a 3D viewport (the visual preview is
deferred — see the task report).

Kept net48-safe: System.Numerics only, no Core-only BCL.
*/
using System.Numerics;
using SW2GZ.Math;

namespace SW2GZ.UI.ViewModels
{
    public static class PoseMath
    {
        /// Build a Pose from translation (x,y,z) + intrinsic ZYX Euler angles
        /// (roll about X, pitch about Y, yaw about Z) in radians. This is the
        /// inverse of Matrix3.FromQuaternion(q).ToRpy(): FromXyzRpy(x,y,z,r,p,yaw)
        /// then ToRpy() round-trips back to (r,p,yaw) (away from gimbal lock).
        public static Pose FromXyzRpy(double x, double y, double z,
                                      double roll, double pitch, double yaw)
        {
            return new Pose(new Vector3((float)x, (float)y, (float)z),
                            QuaternionFromRpy(roll, pitch, yaw));
        }

        /// Quaternion from roll/pitch/yaw (radians), ZYX intrinsic convention.
        /// q = qz(yaw) * qy(pitch) * qx(roll).
        public static Quaternion QuaternionFromRpy(double roll, double pitch, double yaw)
        {
            double cr = System.Math.Cos(roll * 0.5);
            double sr = System.Math.Sin(roll * 0.5);
            double cp = System.Math.Cos(pitch * 0.5);
            double sp = System.Math.Sin(pitch * 0.5);
            double cy = System.Math.Cos(yaw * 0.5);
            double sy = System.Math.Sin(yaw * 0.5);

            double w = cr * cp * cy + sr * sp * sy;
            double qx = sr * cp * cy - cr * sp * sy;
            double qy = cr * sp * cy + sr * cp * sy;
            double qz = cr * cp * sy - sr * sp * cy;

            return new Quaternion((float)qx, (float)qy, (float)qz, (float)w);
        }

        /// Human-readable forward (+X) / up (+Z) axis readout for the given rpy,
        /// e.g. "fwd (1.00, 0.00, 0.00) · up (0.00, 0.00, 1.00)". Lets the user
        /// confirm a sensor's orientation in lieu of a 3D preview.
        public static string DescribeAxes(double roll, double pitch, double yaw)
        {
            Matrix3 r = Matrix3.FromQuaternion(QuaternionFromRpy(roll, pitch, yaw));
            // Columns of R are the rotated basis vectors: fwd = R·(1,0,0), up = R·(0,0,1).
            string fwd = Fmt(r.M11, r.M21, r.M31);
            string up = Fmt(r.M13, r.M23, r.M33);
            return "fwd " + fwd + " · up " + up;
        }

        private static string Fmt(double x, double y, double z)
        {
            return "(" +
                (x + 0.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ", " +
                (y + 0.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ", " +
                (z + 0.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ")";
        }
    }
}
