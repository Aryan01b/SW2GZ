/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Pose composition + inversion + point/vector transform helpers used by the
URDF link-anchor / joint-origin pipeline. Pure / COM-free.

Conventions match System.Numerics:
  - Quaternion: (X, Y, Z, W); identity = (0, 0, 0, 1).
  - Rotating a vector v by q: q * v * q⁻¹  (left-multiplication is "apply q").
  - Composition Compose(a, b) is "first b, then a" — i.e., a ∘ b applied to
    a point gives a.Transform(b.Transform(p)). Matches transformation
    composition the rest of the codebase expects.
*/
using System.Numerics;

namespace SW2GZ.Math
{
    public static class PoseMath
    {
        /// p' = pose.Rotation · p + pose.Position. Standard point transform.
        public static Vector3 TransformPoint(Pose pose, Vector3 p) =>
            Vector3.Transform(p, pose.Rotation) + pose.Position;

        /// Rotates a direction vector; ignores translation.
        public static Vector3 TransformVector(Pose pose, Vector3 v) =>
            Vector3.Transform(v, pose.Rotation);

        /// Inverse: if Q = pose, applies Q⁻¹ to a point: Q⁻¹(p) = R⁻¹ · (p − t).
        /// Returned pose has rotation R⁻¹ and position −R⁻¹·t.
        public static Pose Inverse(Pose pose)
        {
            Quaternion invR = Quaternion.Inverse(pose.Rotation);
            Vector3 invT = -Vector3.Transform(pose.Position, invR);
            return new Pose(invT, invR);
        }

        /// Compose: result represents "first apply b, then apply a".
        /// Equivalent to TransformPoint(result, p) == TransformPoint(a, TransformPoint(b, p)).
        public static Pose Compose(Pose a, Pose b)
        {
            Quaternion r = a.Rotation * b.Rotation;
            Vector3 t = Vector3.Transform(b.Position, a.Rotation) + a.Position;
            return new Pose(t, r);
        }

        /// Returns the relative pose `b` expressed in `a`'s frame, i.e. a⁻¹ ∘ b.
        /// Equivalent to: TransformPoint(Relative(a, b), p) ==
        ///                TransformPoint(Inverse(a), TransformPoint(b, p)).
        public static Pose Relative(Pose a, Pose b) => Compose(Inverse(a), b);

        /// Signed twist angle (radians) of quaternion `q` around `axis`,
        /// via swing-twist decomposition. Returns the rotation component
        /// of `q` that acts purely around `axis`; the swing component
        /// (rotation perpendicular to `axis`) is discarded.
        ///
        /// `axis` does NOT need to be normalised — internally projected.
        /// Returns 0 if `axis` is the zero vector or if `q` has no
        /// rotation around `axis`.
        ///
        /// Used by the export pipeline to figure out how much of the URDF
        /// joint origin's rotation is "current angle around the joint
        /// axis", so the SW limit values can be shifted to be relative
        /// to the URDF joint=0 pose (which equals the SW current state).
        public static double TwistAngle(Quaternion q, Vector3 axis)
        {
            double ax = axis.X, ay = axis.Y, az = axis.Z;
            double axisLenSq = ax * ax + ay * ay + az * az;
            if (axisLenSq < 1e-18) return 0.0;
            double axisLen = System.Math.Sqrt(axisLenSq);
            ax /= axisLen; ay /= axisLen; az /= axisLen;

            // Project q's vector part onto axis. The twist quaternion is
            // (axis * dot, q.W) renormalised; its rotation angle is what
            // we want.
            double dot = q.X * ax + q.Y * ay + q.Z * az;
            double w   = q.W;
            double mag = System.Math.Sqrt(dot * dot + w * w);
            if (mag < 1e-12) return 0.0;
            double twistDot = dot / mag;   // signed magnitude along axis
            double twistW   = w   / mag;

            // 2*atan2 yields angle in [-2π, 2π]. Wrap to (-π, π].
            double angle = 2.0 * System.Math.Atan2(twistDot, twistW);
            const double TWO_PI = 2.0 * System.Math.PI;
            if (angle >  System.Math.PI) angle -= TWO_PI;
            if (angle <= -System.Math.PI) angle += TWO_PI;
            return angle;
        }

        /// Signed projection (metres) of `position` onto `axis`. Returns
        /// 0 if `axis` is zero. Used by the same limit-shift pass as
        /// `TwistAngle`, but for prismatic joints — the "current slide"
        /// distance baked into the URDF origin position.
        public static double SlideDistance(Vector3 position, Vector3 axis)
        {
            double axisLenSq = axis.X * axis.X + axis.Y * axis.Y + axis.Z * axis.Z;
            if (axisLenSq < 1e-18) return 0.0;
            double axisLen = System.Math.Sqrt(axisLenSq);
            return (position.X * axis.X + position.Y * axis.Y + position.Z * axis.Z) / axisLen;
        }
    }
}
