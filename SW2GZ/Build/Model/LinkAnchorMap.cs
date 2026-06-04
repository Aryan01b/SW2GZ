/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Pure helper: builds a `linkName → assembly-frame Pose` dictionary by
asking the optional IComponentPoseSource for each link's first part.
Used by Sw2gzPipeline to rebase mesh vertices into link-local frames
and compute URDF joint origins as parent-frame transforms.

When the pose source is unavailable (e.g. test mocks of IAssemblyWalker
that don't also implement IComponentPoseSource), every link maps to
Pose.Identity — yielding the legacy "everything at world origin" output
byte-for-byte for back-compat with existing golden tests.
*/
using System;
using System.Collections.Generic;
using SW2GZ.Math;
using SW2GZ.SwSurface.Abstractions;

namespace SW2GZ.Build.Model
{
    public static class LinkAnchorMap
    {
        /// Returns a map linkName → anchor pose in the assembly frame. The
        /// anchor of a link is the world placement of its first part (chosen
        /// canonically as `spec.FlattenedPartPaths[0]`). When `source` is null
        /// or the source returns Identity for every part, the result is an
        /// all-identity map.
        public static IReadOnlyDictionary<string, Pose> Build(
            IEnumerable<LinkSpec> specs,
            IComponentPoseSource source)
        {
            if (specs == null) throw new ArgumentNullException(nameof(specs));
            var map = new Dictionary<string, Pose>(StringComparer.Ordinal);
            foreach (LinkSpec spec in specs)
            {
                if (spec == null || string.IsNullOrEmpty(spec.Name)) continue;
                if (spec.FlattenedPartPaths == null || spec.FlattenedPartPaths.Count == 0)
                {
                    map[spec.Name] = Pose.Identity;
                    continue;
                }
                Pose anchor = source == null
                    ? Pose.Identity
                    : (source.GetComponentPose(spec.FlattenedPartPaths[0]) ?? Pose.Identity);
                map[spec.Name] = anchor;
            }
            return map;
        }
    }
}
