/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Swing-twist decomposition: given a unit-length axis `a` and a rotation
quaternion `q`, returns the signed angle of the twist component (rotation
about `a`). Used by the live preview joint sampler to read a revolute
joint's current angle from the relative pose of two SW components.

Algorithm (Dobrowolski 2015 / standard ref):
  - Project the quaternion's vector part onto `a` (dot product).
  - twist = normalize((projection · a, q.w)).
  - angle = 2 · atan2(|twist.xyz|, twist.w), signed by the projection.

Returns 0 for a zero-norm projection (no rotation about `a`).
Pure / COM-free.
*/
using System.Numerics;

namespace SW2GZ.Build.Model
{
    public static class SwingTwist
    {
        /// Signed angle (radians) of the rotation component of `q` around the
        /// unit axis `a`. Range (-π, π]. `a` should be unit-length; near-zero
        /// `a` yields 0.
        public static double TwistAngleAround(Quaternion q, Vector3 a)
        {
            float aLen = a.Length();
            if (aLen < 1e-9f) return 0.0;
            a = a / aLen;

            // Vector part of q, projected onto a.
            float dot = q.X * a.X + q.Y * a.Y + q.Z * a.Z;
            float tx = dot * a.X, ty = dot * a.Y, tz = dot * a.Z;
            float tw = q.W;

            float norm = (float)System.Math.Sqrt(tx * tx + ty * ty + tz * tz + tw * tw);
            if (norm < 1e-9f) return 0.0;
            tx /= norm; ty /= norm; tz /= norm; tw /= norm;

            // |twist.xyz| with sign = sign of (twist·a).
            float twistVecLen = (float)System.Math.Sqrt(tx * tx + ty * ty + tz * tz);
            float sign = (tx * a.X + ty * a.Y + tz * a.Z) >= 0f ? 1f : -1f;
            double angle = 2.0 * System.Math.Atan2(sign * twistVecLen, tw);
            // Wrap into (-π, π].
            if (angle > System.Math.PI)  angle -= 2.0 * System.Math.PI;
            if (angle <= -System.Math.PI) angle += 2.0 * System.Math.PI;
            return angle;
        }

        /// Signed distance along axis `a` for a translation `t`. `a` unit-length.
        public static double DisplacementAlong(Vector3 t, Vector3 a)
        {
            float aLen = a.Length();
            if (aLen < 1e-9f) return 0.0;
            a = a / aLen;
            return t.X * a.X + t.Y * a.Y + t.Z * a.Z;
        }
    }
}
