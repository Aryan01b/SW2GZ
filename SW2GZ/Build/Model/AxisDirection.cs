/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Coordinate-convention primitive: one of the six signed cardinal axes
({+X, -X, +Y, -Y, +Z, -Z}). Used to describe which SolidWorks axis the
user's assembly treats as "up" (gravity-opposed) and which axis the
robot "faces". The (up, forward) pair drives the SW→ROS rotation
matrix built by SwToRosRotation.

Pure / COM-free so the test project source-links it.
*/
namespace SW2GZ.Build.Model
{
    public enum AxisDirection
    {
        PlusX = 0,
        MinusX = 1,
        PlusY = 2,
        MinusY = 3,
        PlusZ = 4,
        MinusZ = 5,
    }

    public static class AxisDirectionExtensions
    {
        /// Returns the unit vector (X, Y, Z) the axis points along.
        public static (double X, double Y, double Z) ToVector(this AxisDirection a)
        {
            switch (a)
            {
                case AxisDirection.PlusX:  return ( 1,  0,  0);
                case AxisDirection.MinusX: return (-1,  0,  0);
                case AxisDirection.PlusY:  return ( 0,  1,  0);
                case AxisDirection.MinusY: return ( 0, -1,  0);
                case AxisDirection.PlusZ:  return ( 0,  0,  1);
                case AxisDirection.MinusZ: return ( 0,  0, -1);
                default: return (0, 0, 0);
            }
        }

        /// True iff the two axes share a line — the same direction or its
        /// negation. SwToRosRotation rejects parallel (up, forward) pairs
        /// because they don't span a 3D rotation.
        public static bool IsParallelTo(this AxisDirection a, AxisDirection b)
        {
            (double ax, double ay, double az) = a.ToVector();
            (double bx, double by, double bz) = b.ToVector();
            double dot = ax * bx + ay * by + az * bz;
            return System.Math.Abs(dot) > 0.9999;   // exact ±1 for our six cardinals
        }

        /// Short label suitable for logs / wizard UI: "+Y", "-Z", etc.
        public static string ToShortString(this AxisDirection a)
        {
            switch (a)
            {
                case AxisDirection.PlusX:  return "+X";
                case AxisDirection.MinusX: return "-X";
                case AxisDirection.PlusY:  return "+Y";
                case AxisDirection.MinusY: return "-Y";
                case AxisDirection.PlusZ:  return "+Z";
                case AxisDirection.MinusZ: return "-Z";
                default: return "?";
            }
        }
    }
}
