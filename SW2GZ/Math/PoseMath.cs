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
    }
}
