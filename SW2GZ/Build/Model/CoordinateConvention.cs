/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P1 — RobotModel keystone: carries the SolidWorks→ROS rotation matrix and
the unit scale that the builder will apply uniformly in P2/P3. For P1 we
default to Identity + meters and don't actually rotate anything yet — that
wiring lands in the math phase.
*/
namespace SW2GZ.Build.Model
{
    public sealed record CoordinateConvention(SW2GZ.Math.Matrix3 SwToRos, double LengthScale)
    {
        public static CoordinateConvention Identity =>
            new CoordinateConvention(SW2GZ.Math.Matrix3.Identity, 1.0);

        // Called by P3 InertialAggregator wiring; not exercised in P1 production code.
        // Rough sanity check. Strict orthonormality belongs in P3 once the
        // math helpers exist; here we just guard against the obvious bad
        // values (zero scale, zero matrix).
        public bool Validate()
        {
            if (LengthScale <= 0) return false;
            if (SwToRos.IsZero()) return false;
            return true;
        }
    }
}
