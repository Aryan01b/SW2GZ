/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Pure helper: given the parent + child link anchors (assembly-frame
poses) and the joint's axis direction in the assembly frame, returns
the URDF joint's parent-frame origin pose, the axis expressed in
the child link's (joint's) frame, and the offset from the new child
link frame to the child part's design origin.

URDF convention reminder:
  - joint <origin xyz rpy>  transforms PARENT link frame → CHILD link frame
  - joint <axis xyz>        is expressed in the JOINT frame, which after
                            <origin> is applied coincides with the CHILD link frame.

Two modes:

  1. LEGACY (matePoint == null) — origin = parentAnchor⁻¹ ∘ childAnchor,
     i.e. the child link frame coincides with the child part's design
     origin. Byte-identical to the pre-mate-point pipeline output;
     keeps the existing golden tests stable.

  2. MATE-POINT (matePoint != null) — the child link frame is anchored
     at the mate's geometric reference point (e.g. the axis origin of a
     concentric mate's cylindrical face), with the SAME orientation as
     the child part. This fixes hinges that previously pivoted around
     the child part's design origin instead of the mate axis.

     Position:  origin.pos   = R_parent⁻¹ · (matePoint_asm − parentAnchor.pos)
     Rotation:  origin.rot   = parentAnchor.Rot⁻¹ * childAnchor.Rot       (unchanged)
     Axis:      axis_child   = childAnchor.Rot⁻¹ · axis_assembly          (unchanged)

     The child link frame's design-origin offset (i.e. where the part's
     local (0,0,0) sits relative to the new link frame, expressed IN the
     child link frame) is:

         ChildAnchorOffset = childAnchor.Rot⁻¹ · (childAnchor.pos − matePoint_asm)

     Downstream URDF emitters use this to place <visual>, <collision>,
     and <inertial> origins so the mesh keeps its original world
     position even though the link frame moved to the mate point.

When matePoint is null, ChildAnchorOffset is Vector3.Zero.
*/
using System.Numerics;
using SW2GZ.Math;

namespace SW2GZ.Build.Model
{
    public static class JointOriginResolver
    {
        public sealed class Resolved
        {
            public Pose Origin { get; }
            public Vector3 AxisInJointFrame { get; }
            // Offset from the new child link frame to the child part's design
            // origin, expressed in the CHILD link frame. Zero in legacy mode
            // (matePoint == null) so existing emitters stay byte-stable.
            public Vector3 ChildAnchorOffset { get; }

            public Resolved(Pose origin, Vector3 axis, Vector3 childAnchorOffset)
            {
                Origin = origin;
                AxisInJointFrame = axis;
                ChildAnchorOffset = childAnchorOffset;
            }

            // Back-compat ctor for callers that don't carry a child-anchor
            // offset (legacy path / older test fixtures).
            public Resolved(Pose origin, Vector3 axis)
                : this(origin, axis, Vector3.Zero) { }
        }

        // Legacy 3-arg overload — preserved for back-compat with callers and
        // tests that haven't been updated. Equivalent to passing matePoint=null.
        public static Resolved Compute(Pose parentAnchor, Pose childAnchor, Vector3 axisAssembly) =>
            Compute(parentAnchor, childAnchor, axisAssembly, matePointAssembly: null);

        // Full overload — when matePointAssembly is non-null, anchors the joint
        // origin at the mate's geometric reference point instead of at the
        // child part's design origin.
        public static Resolved Compute(
            Pose parentAnchor,
            Pose childAnchor,
            Vector3 axisAssembly,
            Vector3? matePointAssembly)
        {
            parentAnchor = parentAnchor ?? Pose.Identity;
            childAnchor  = childAnchor  ?? Pose.Identity;

            Quaternion invChildRot = Quaternion.Inverse(childAnchor.Rotation);
            Vector3 axis = Vector3.Transform(axisAssembly, invChildRot);

            if (!matePointAssembly.HasValue)
            {
                // Legacy path: child link frame == child part's design origin.
                Pose origin = PoseMath.Relative(parentAnchor, childAnchor);
                return new Resolved(origin, axis, Vector3.Zero);
            }

            // Mate-point path: child link frame is at the mate point but keeps
            // the child part's orientation (so the visual/collision/inertial
            // mesh can be placed via a pure translation offset).
            Vector3 mp = matePointAssembly.Value;
            Quaternion invParentRot = Quaternion.Inverse(parentAnchor.Rotation);
            Vector3 posInParent = Vector3.Transform(mp - parentAnchor.Position, invParentRot);
            Quaternion rotInParent = invParentRot * childAnchor.Rotation;
            var origin2 = new Pose(posInParent, rotInParent);

            // The child part's design origin sits at childAnchor.Position in
            // the assembly. Express the vector from mate point → part origin
            // in the child link frame (which shares childAnchor's rotation).
            Vector3 childAnchorOffset =
                Vector3.Transform(childAnchor.Position - mp, invChildRot);

            return new Resolved(origin2, axis, childAnchorOffset);
        }
    }
}
