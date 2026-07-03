/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Pure (COM-free) classifier: given a SolidWorks mate's type, its limit range,
and already-extracted LOCAL (part-frame) face geometry + the owning
component's pose, decides the resulting UrdfJointType and computes the
assembly-frame axis/pivot-point. No SolidWorks types appear here — the
COM-facing SwMateJointResolver extracts local geometry (ISurface.
CylinderParams/PlaneParams) and component poses (IComponentPoses, already
column-major-correct) and hands them to this pure layer, exactly the same
split JointDefReconciler established for link-tree reconciliation.

Classification:
  Lock                    → Fixed
  Concentric, no limit    → Continuous
  Concentric, limited     → Revolute
  Angle,  limited         → Revolute
  Distance, limited       → Prismatic
  anything else           → Fixed (no fabricated axis)
"Limited" means abs(lower) > 1e-9 || abs(upper) > 1e-9 — a plain Concentric
mate always reports 0/0 (it never carries its own limit; confirmed against
FULL_ARM.SLDASM's real mates during design — see
docs/superpowers/specs/2026-07-03-robot-mate-joint-suggestion-design.md).

Axis + pivot geometry, once local geometry is transformed into assembly
frame via Matrix3.Mul (rotation) / Matrix3.Mul + translation (points) —
NEVER via raw Transform2.ArrayData reads, see the plan's "Why not reuse
AutoJointResolver.cs verbatim" section:
  Concentric → cylinder axis direction + a point on that axis.
  Angle      → cross product of the two mated planes' normals; origin =
               parent plane's point (the actual hinge sits on the shared
               edge between the two faces — the parent point is a fair
               anchor, good enough for the URDF, not required to be exact
               to the millimeter for a first suggestion the user can edit).
  Distance   → parent plane's normal as slide direction; origin = that
               plane's point.
  Lock       → no geometry needed.

A movable classification (Continuous/Revolute/Prismatic) with no
extractable geometry (e.g. a Concentric mate whose face somehow wasn't a
real cylinder) demotes to Fixed rather than emit a zero-axis joint — same
defensive rule the pre-gut AutoJointResolver used.
*/
using System.Collections.Generic;
using System.Numerics;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;

namespace SW2GZ.Build
{
    public enum SwMateTypeCode { Lock, Concentric, Angle, Distance, Other }

    public static class MateJointClassification
    {
        public sealed class PlanePair
        {
            public Vector3 ParentNormalLocal, ParentPointLocal, ParentTranslation;
            public Matrix3 ParentRotation;
            public Vector3 ChildNormalLocal, ChildPointLocal, ChildTranslation;
            public Matrix3 ChildRotation;

            public PlanePair(
                Vector3 parentNormalLocal, Vector3 parentPointLocal, Matrix3 parentRotation, Vector3 parentTranslation,
                Vector3 childNormalLocal, Vector3 childPointLocal, Matrix3 childRotation, Vector3 childTranslation)
            {
                ParentNormalLocal = parentNormalLocal; ParentPointLocal = parentPointLocal;
                ParentRotation = parentRotation; ParentTranslation = parentTranslation;
                ChildNormalLocal = childNormalLocal; ChildPointLocal = childPointLocal;
                ChildRotation = childRotation; ChildTranslation = childTranslation;
            }
        }

        public sealed class Result
        {
            public bool Found;
            public UrdfJointType Type = UrdfJointType.Fixed;
            public Vector3 AxisAssembly = Vector3.Zero;
            public Vector3? OriginAssembly;
            public double? LimitLower;
            public double? LimitUpper;
        }

        public static Result Classify(
            SwMateTypeCode mateType,
            double? limitLower, double? limitUpper,
            Vector3? cylinderLocalOrigin, Vector3? cylinderLocalAxis,
            Matrix3 cylinderComponentRotation, Vector3 cylinderComponentTranslation,
            PlanePair planeGeometry)
        {
            bool hasLimit = (limitLower.HasValue && System.Math.Abs(limitLower.Value) > 1e-9)
                         || (limitUpper.HasValue && System.Math.Abs(limitUpper.Value) > 1e-9);

            UrdfJointType type = mateType switch
            {
                SwMateTypeCode.Lock => UrdfJointType.Fixed,
                SwMateTypeCode.Concentric => hasLimit ? UrdfJointType.Revolute : UrdfJointType.Continuous,
                SwMateTypeCode.Angle => UrdfJointType.Revolute,
                SwMateTypeCode.Distance => UrdfJointType.Prismatic,
                _ => UrdfJointType.Fixed,
            };

            Vector3 axis = Vector3.Zero;
            Vector3? origin = null;
            bool geometryOk = false;

            if (mateType == SwMateTypeCode.Concentric && cylinderLocalOrigin.HasValue && cylinderLocalAxis.HasValue)
            {
                axis = NormalizeOrZero(cylinderComponentRotation.Mul(cylinderLocalAxis.Value));
                origin = cylinderComponentRotation.Mul(cylinderLocalOrigin.Value) + cylinderComponentTranslation;
                geometryOk = axis != Vector3.Zero;
            }
            else if (mateType == SwMateTypeCode.Angle && planeGeometry != null)
            {
                Vector3 parentNormalAsm = NormalizeOrZero(planeGeometry.ParentRotation.Mul(planeGeometry.ParentNormalLocal));
                Vector3 childNormalAsm = NormalizeOrZero(planeGeometry.ChildRotation.Mul(planeGeometry.ChildNormalLocal));
                Vector3 cross = Vector3.Cross(parentNormalAsm, childNormalAsm);
                if (cross.LengthSquared() > 1e-12f)
                {
                    axis = Vector3.Normalize(cross);
                    origin = planeGeometry.ParentRotation.Mul(planeGeometry.ParentPointLocal) + planeGeometry.ParentTranslation;
                    geometryOk = true;
                }
            }
            else if (mateType == SwMateTypeCode.Distance && planeGeometry != null)
            {
                axis = NormalizeOrZero(planeGeometry.ParentRotation.Mul(planeGeometry.ParentNormalLocal));
                origin = planeGeometry.ParentRotation.Mul(planeGeometry.ParentPointLocal) + planeGeometry.ParentTranslation;
                geometryOk = axis != Vector3.Zero;
            }

            if (!geometryOk && type != UrdfJointType.Fixed)
            {
                // Movable kind with no extractable geometry → would write a
                // zero-axis joint. Demote to Fixed so the user can add a
                // cleaner mate and re-trigger suggestion.
                type = UrdfJointType.Fixed;
                axis = Vector3.Zero;
                origin = null;
            }

            return new Result
            {
                Found = true,
                Type = type,
                AxisAssembly = axis,
                OriginAssembly = geometryOk ? origin : null,
                LimitLower = hasLimit ? limitLower : null,
                LimitUpper = hasLimit ? limitUpper : null,
            };
        }

        // Prefer a limit-bearing candidate (→ Revolute/Prismatic) over a
        // plain Continuous one when multiple mates span the same (parent,
        // child) pair; first-seen wins within a tie, same rank the pre-gut
        // AutoJointResolved.ChooseBest used.
        public static Result ChooseBest(IReadOnlyList<Result> candidates)
        {
            if (candidates == null || candidates.Count == 0) return new Result { Found = false };
            Result firstAny = null, firstLimit = null;
            foreach (Result c in candidates)
            {
                if (c == null || !c.Found) continue;
                if (firstAny == null) firstAny = c;
                if (firstLimit == null && (c.LimitLower.HasValue || c.LimitUpper.HasValue)) firstLimit = c;
            }
            return firstLimit ?? firstAny ?? new Result { Found = false };
        }

        private static Vector3 NormalizeOrZero(Vector3 v) =>
            v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : Vector3.Zero;
    }
}
