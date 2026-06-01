/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P1 — RobotModel keystone: assembles the immutable RobotModel aggregate
from raw UrdfLink/UrdfJoint POCOs plus metadata. Defaults Materials/Sensors
to empty lists and Control to a joint_state_broadcaster-only spec listing
every joint by name.

The builder sanitizes the package name (via PackageNameSanitizer) so
downstream serializers don't need to worry about non-ament-compliant
input. P2/P3 will extend the builder to apply CoordinateConvention to
every link/joint pose; for P1 it's a straight passthrough.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;

namespace SW2GZ.Build
{
    public sealed class RobotModelBuilder
    {
        public RobotModel Build(
            RobotMeta meta,
            IReadOnlyList<UrdfLink> links,
            IReadOnlyList<UrdfJoint> joints,
            IReadOnlyList<MaterialDef>? materials = null,
            IReadOnlyList<SensorDef>? sensors = null,
            ControlSpec? control = null)
        {
            if (meta == null) throw new ArgumentNullException(nameof(meta));
            if (links == null) throw new ArgumentNullException(nameof(links));
            if (joints == null) throw new ArgumentNullException(nameof(joints));
            if (links.Count == 0)
                throw new ArgumentException("RobotModel requires at least one link.", nameof(links));

            // Sanitize package name in-place — downstream writers assume it's ament-clean.
            string sanitizedPkg = PackageNameSanitizer.Sanitize(meta.PackageName).Value;
            RobotMeta safeMeta = meta with { PackageName = sanitizedPkg };

            var modelLinks = new List<ModelLink>(links.Count);
            foreach (UrdfLink link in links)
                modelLinks.Add(new ModelLink(link, null, null));

            IReadOnlyList<MaterialDef> mats = materials ?? Array.Empty<MaterialDef>();
            IReadOnlyList<SensorDef> sens = sensors ?? Array.Empty<SensorDef>();
            ControlSpec ctrl = control ?? new ControlSpec(
                joints.Select(j => j.Name).ToList(),
                "joint_state_broadcaster");

            return new RobotModel(safeMeta, modelLinks, joints, mats, sens, ctrl);
        }
    }
}
