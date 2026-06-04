/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Builds the SolidWorks → ROS (REP-103) rotation matrix from the user's
choice of "which SW axis is up" + "which SW axis the robot faces".

The resulting matrix R is applied as v_ros = R * v_sw, and satisfies:
  R * up_sw      = (0, 0, 1)   (ROS Z is up)
  R * forward_sw = (1, 0, 0)   (ROS X is forward)
  R * left_sw    = (0, 1, 0)   (ROS Y is left, = Z × X)

The pipeline applies R exactly once — on the `world_to_<root>` fixed
joint that anchors the robot in the URDF / SDF world frame. Everything
inside the robot stays in its native SW frame; only the base hits the
world properly oriented.

Pure / COM-free so the test project source-links it.
*/
using System;
using SW2GZ.Math;

namespace SW2GZ.Build.Model
{
    public static class SwToRosRotation
    {
        /// Builds R such that v_ros = R * v_sw, with the user's SW `up`
        /// axis mapped to ROS +Z and SW `forward` mapped to ROS +X.
        /// Throws ArgumentException if up and forward are parallel (they
        /// must span a plane for the third basis vector to exist).
        public static Matrix3 Build(AxisDirection up, AxisDirection forward)
        {
            if (up.IsParallelTo(forward))
                throw new ArgumentException(
                    "SW up and forward axes must not be parallel — " +
                    "got up=" + up.ToShortString() +
                    ", forward=" + forward.ToShortString() + ".");

            (double ux, double uy, double uz) = up.ToVector();
            (double fx, double fy, double fz) = forward.ToVector();

            // ROS Y = ROS Z × ROS X = up × forward (in SW frame, then mapped).
            double lx = uy * fz - uz * fy;
            double ly = uz * fx - ux * fz;
            double lz = ux * fy - uy * fx;

            // Columns of R are the images of (1,0,0)_sw, (0,1,0)_sw, (0,0,1)_sw.
            // We have the rows directly: row 1 = forward_sw (so that R * forward = +X),
            // row 2 = left_sw (so that R * left = +Y), row 3 = up_sw (so R * up = +Z).
            return new Matrix3(
                fx, fy, fz,
                lx, ly, lz,
                ux, uy, uz);
        }

        /// Convenience: the (roll, pitch, yaw) extracted from Build(up, forward).
        /// Emitted directly into the URDF `world_to_<root>` joint origin.
        public static (double Roll, double Pitch, double Yaw) BuildRpy(
            AxisDirection up, AxisDirection forward) => Build(up, forward).ToRpy();
    }
}
