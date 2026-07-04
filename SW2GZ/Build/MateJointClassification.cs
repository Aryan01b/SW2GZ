/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Pure (COM-free) classifier: given a SolidWorks mate's type and its limit
range, decides the resulting UrdfJointType and limit. No geometry, no
SolidWorks types — axis/pivot no longer come from mate geometry at all
(see docs/superpowers/specs/2026-07-03-manual-axis-pivot-pick-design.md):
three live-tested attempts at deriving an accurate axis from mate geometry
each fixed one bug and surfaced another against FULL_ARM.SLDASM, so this
phase replaces that mechanism with a direct user pick of a cylindrical
face or straight edge (SwMateJointResolver.TryExtractAxisFromSelection)
instead of patching the classifier's geometry guesses a fourth time.

Classification:
  Lock                    → Fixed
  Concentric, no limit    → Continuous
  Concentric, limited     → Revolute
  Angle                   → Revolute
  Distance                → Prismatic
  anything else           → Fixed
"Limited" means abs(lower) > 1e-9 || abs(upper) > 1e-9 — a plain Concentric
mate always reports 0/0 (it never carries its own limit; confirmed against
FULL_ARM.SLDASM's real mates during design — see
docs/superpowers/specs/2026-07-03-robot-mate-joint-suggestion-design.md).
*/
using System.Collections.Generic;
using SW2GZ.Build.Urdf;

namespace SW2GZ.Build
{
    public enum SwMateTypeCode { Lock, Concentric, Angle, Distance, Other }

    public static class MateJointClassification
    {
        public sealed class Result
        {
            public bool Found;
            public UrdfJointType Type = UrdfJointType.Fixed;
            public double? LimitLower;
            public double? LimitUpper;
            // Which SW mate produced this candidate — set by the impure
            // caller (SwMateJointResolver), not by Classify itself (pure,
            // has no mate identity to give).
            public string MateName;
        }

        public static Result Classify(SwMateTypeCode mateType, double? limitLower, double? limitUpper)
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

            return new Result
            {
                Found = true,
                Type = type,
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
    }
}
