/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

D3 — Mirrors upstream solidworks_urdf_exporter's reference-geometry-driven
joint export. The user picks a Reference Coordinate System feature (drives
joint origin xyz+rpy) and a Reference Axis feature (drives joint axis
direction) on the CHILD component of each joint. This file reads those two
inputs and the LocalizeJoint() math that turns child-global pose into
parent-frame URDF <origin>.

Upstream references:
  - SW2URDF/URDFExport/ExportHelperExtension.cs  (GetCoordinateSystemTransform,
                                                  GetRefAxis, LocalizeJoint)
  - SW2URDF/Utilities/MathOps.cs                 (GetXYZ, GetRPY, GetTransformation)

Why this matters: the v2.x anchor-derived joint-origin path lands joints at
the wrong fulcrum (FULL_ARM joint 2 origin was 3 cm off — should have been
~30 cm). Anchoring on a user-chosen Reference Coordinate System is upstream's
proven workaround, and SolidWorks already exposes the feature.

The pure-C# SwJointPoseMath.Localize static helper is always compiled (outside
the #if SW_INTEROP block) so the test project can exercise the LocalizeJoint
math without SolidWorks interop assemblies.
*/
using System.Numerics;
#if SW_INTEROP
using SolidWorks.Interop.sldworks;
#endif
using SW2GZ.Math;

namespace SW2GZ.SwSurface
{
    /// Pure-C# half of SwJointPoseReader — the LocalizeJoint math used to
    /// express a child component's global pose in its parent's frame. Lives
    /// outside #if SW_INTEROP so source-linked tests can exercise it.
    public static class SwJointPoseMath
    {
        /// childInParent = parentGlobal⁻¹ ∘ childGlobal.
        /// This is the URDF joint <origin> mapping parent link frame to child
        /// link frame, mirroring upstream LocalizeJoint.
        public static Pose Localize(Pose parentGlobal, Pose childGlobal)
        {
            return PoseMath.Relative(parentGlobal ?? Pose.Identity,
                                     childGlobal  ?? Pose.Identity);
        }
    }

#if SW_INTEROP
    public sealed class SwJointPoseReader
    {
        private readonly AssemblyDoc _doc;

        public SwJointPoseReader(AssemblyDoc doc)
        {
            _doc = doc;
        }

        /// World-frame (assembly-frame) transform of the named Reference
        /// Coordinate System feature on a specific component. Returns null
        /// when the component or the named feature is missing.
        ///
        /// Mirrors upstream ExportHelperExtension.GetCoordinateSystemTransform:
        /// the CS feature is local to the component's part model, so we look
        /// it up via component.GetModelDoc2().Extension and then post-multiply
        /// by component.Transform2 to map it into the assembly frame.
        public MathTransform GetCsTransform(Component2 component, string csName)
        {
            if (component == null || string.IsNullOrEmpty(csName)) return null;
            try
            {
                var compModel = component.GetModelDoc2() as ModelDoc2;
                if (compModel == null) return null;
                MathTransform local = compModel.Extension
                    .GetCoordinateSystemTransformByName(csName);
                if (local == null) return null;

                MathTransform compXform = component.Transform2;
                return compXform == null ? local : local.Multiply(compXform);
            }
            catch
            {
                return null;
            }
        }

        /// Unit world-frame direction of the named Reference Axis feature on a
        /// specific component. Returns null when the axis can't be resolved.
        ///
        /// Upstream pattern: SelectByID2 with type "AXIS" → SelectionManager
        /// .GetSelectedObject6(1,0) → cast to RefAxis → GetRefAxisParams gives
        /// the two endpoint coordinates [x1,y1,z1,x2,y2,z2]; direction =
        /// normalize(p2 - p1).
        public Vector3? GetAxisDirection(Component2 component, string axisName)
        {
            if (component == null || string.IsNullOrEmpty(axisName)) return null;
            try
            {
                var compModel = component.GetModelDoc2() as ModelDoc2;
                if (compModel == null) return null;

                // Clear any prior selection so GetSelectedObject6(1, ...) returns
                // the axis we just picked, not something stale.
                compModel.ClearSelection2(true);
                bool ok = compModel.Extension.SelectByID2(
                    axisName, "AXIS", 0, 0, 0, false, 0, null, 0);
                if (!ok) return null;

                var selMgr = compModel.SelectionManager as SelectionMgr;
                if (selMgr == null) return null;
                var feat = selMgr.GetSelectedObject6(1, 0) as Feature;
                if (feat == null) return null;
                var axis = feat.GetSpecificFeature2() as RefAxis;
                if (axis == null) return null;

                if (!(axis.GetRefAxisParams() is double[] p) || p.Length < 6) return null;
                var d = new Vector3(
                    (float)(p[3] - p[0]),
                    (float)(p[4] - p[1]),
                    (float)(p[5] - p[2]));
                float len = d.Length();
                if (len < 1e-9f) return null;
                return d / len;
            }
            catch
            {
                return null;
            }
        }

        /// Convenience overload that delegates to SwJointPoseMath.Localize
        /// after extracting Poses from the two MathTransforms.
        public static Pose Localize(MathTransform parentGlobal, MathTransform childGlobal)
        {
            Pose parent = MathTransformPose.FromArrayData(parentGlobal?.ArrayData as double[]);
            Pose child  = MathTransformPose.FromArrayData(childGlobal?.ArrayData as double[]);
            return SwJointPoseMath.Localize(parent, child);
        }
    }
#endif
}
