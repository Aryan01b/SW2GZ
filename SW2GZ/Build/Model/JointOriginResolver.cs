/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Pure helper: given the parent + child link anchors (assembly-frame
poses) and the joint's axis direction in the assembly frame, returns
the URDF joint's parent-frame origin pose AND the axis expressed in
the child link's (joint's) frame.

URDF convention reminder:
  - joint <origin xyz rpy>  transforms PARENT link frame → CHILD link frame
  - joint <axis xyz>        is expressed in the JOINT frame, which after
                            <origin> is applied coincides with the CHILD link frame.

So origin = parentAnchor⁻¹ ∘ childAnchor, and the URDF axis vector is
the assembly-frame axis re-expressed in child rotation: axis_child =
childAnchor.Rotation⁻¹ · axis_assembly.

When both anchors are identity (test fakes without an IComponentPoseSource),
the result is (Pose.Identity, axis_assembly unchanged) — byte-identical
to the pre-anchor pipeline output.
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
            public Resolved(Pose origin, Vector3 axis) { Origin = origin; AxisInJointFrame = axis; }
        }

        public static Resolved Compute(Pose parentAnchor, Pose childAnchor, Vector3 axisAssembly)
        {
            parentAnchor = parentAnchor ?? Pose.Identity;
            childAnchor  = childAnchor  ?? Pose.Identity;

            Pose origin = PoseMath.Relative(parentAnchor, childAnchor);
            Quaternion invChildRot = Quaternion.Inverse(childAnchor.Rotation);
            Vector3 axis = Vector3.Transform(axisAssembly, invChildRot);
            return new Resolved(origin, axis);
        }
    }
}
