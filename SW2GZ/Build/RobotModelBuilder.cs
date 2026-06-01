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

P5 — adds AssembleLinksWithMaterials: takes (UrdfLink, partPath) pairs
plus an IAppearanceSource and returns the (ModelLink list, deduped
MaterialDef list) tuple. RGBA range validation lives here (deferred
from P1's MaterialDef POCO). Material names are sanitized via
RosNameSanitizer so they're valid URDF identifiers; dedup is keyed on
the sanitized name with a conflict check (same name + different RGBA
=> InvalidOperationException). Also adds a Build overload that accepts
a pre-built ModelLink list (so the pipeline can route the material
results straight in).
*/
using System;
using System.Collections.Generic;
using System.Linq;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.SwSurface.Abstractions;

namespace SW2GZ.Build
{
    public static class RobotModelBuilder
    {
        private const double RgbaTolerance = 1e-6;

        public static RobotModel Build(
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
                ControlSpec.DefaultJointStateBroadcaster);

            return new RobotModel(safeMeta, modelLinks, joints, mats, sens, ctrl);
        }

        /// P5 — Build overload that accepts a pre-built ModelLink list directly.
        /// Used by the pipeline after AssembleLinksWithMaterials so the material
        /// references attached to each ModelLink survive into the RobotModel.
        public static RobotModel Build(
            RobotMeta meta,
            IReadOnlyList<ModelLink> modelLinks,
            IReadOnlyList<UrdfJoint> joints,
            IReadOnlyList<MaterialDef> materials,
            IReadOnlyList<SensorDef>? sensors = null,
            ControlSpec? control = null)
        {
            if (meta == null) throw new ArgumentNullException(nameof(meta));
            if (modelLinks == null) throw new ArgumentNullException(nameof(modelLinks));
            if (joints == null) throw new ArgumentNullException(nameof(joints));
            if (materials == null) throw new ArgumentNullException(nameof(materials));
            if (modelLinks.Count == 0)
                throw new ArgumentException("RobotModel requires at least one link.", nameof(modelLinks));

            string sanitizedPkg = PackageNameSanitizer.Sanitize(meta.PackageName).Value;
            RobotMeta safeMeta = meta with { PackageName = sanitizedPkg };

            IReadOnlyList<SensorDef> sens = sensors ?? Array.Empty<SensorDef>();
            ControlSpec ctrl = control ?? new ControlSpec(
                joints.Select(j => j.Name).ToList(),
                ControlSpec.DefaultJointStateBroadcaster);

            return new RobotModel(safeMeta, modelLinks, joints, materials, sens, ctrl);
        }

        /// P5 — for each (UrdfLink, partPath) pair, ask the appearance source
        /// for an optional MaterialDef. Returns the ModelLink list (each link
        /// tagged with its sanitized material name or null) and the deduped
        /// materials list (first-seen order).
        ///
        /// Validation:
        ///   - RGBA components must be in [0, 1] (else ArgumentException).
        ///   - Sanitized material name must be non-empty.
        ///   - Same sanitized name with different RGBA (beyond 1e-6) =>
        ///     InvalidOperationException ("conflicting definitions").
        ///   - Same sanitized name + same RGBA => deduped into a single entry.
        ///
        /// For v2.1 the source is queried only for the primary part of each link
        /// (first-part-wins for multi-body links); a richer multi-material schema
        /// is deferred.
        public static (IReadOnlyList<ModelLink> Links, IReadOnlyList<MaterialDef> Materials)
            AssembleLinksWithMaterials(
                IReadOnlyList<(UrdfLink Link, string PartPath)> linksWithPaths,
                IAppearanceSource appearanceSource)
        {
            if (linksWithPaths == null) throw new ArgumentNullException(nameof(linksWithPaths));
            if (appearanceSource == null) throw new ArgumentNullException(nameof(appearanceSource));

            var modelLinks = new List<ModelLink>(linksWithPaths.Count);
            var orderedMaterials = new List<MaterialDef>();
            var byName = new Dictionary<string, MaterialDef>(StringComparer.Ordinal);

            foreach ((UrdfLink link, string partPath) in linksWithPaths)
            {
                MaterialDef? raw = appearanceSource.GetMaterial(partPath);
                if (raw == null)
                {
                    modelLinks.Add(new ModelLink(link, null, null));
                    continue;
                }

                ValidateRgba(raw);

                string sanitized = RosNameSanitizer.Sanitize(raw.Name).Value;
                if (string.IsNullOrWhiteSpace(sanitized))
                    throw new ArgumentException(
                        $"Material name '{raw.Name}' sanitizes to empty.", nameof(linksWithPaths));

                MaterialDef canonical = raw with { Name = sanitized };

                if (byName.TryGetValue(sanitized, out MaterialDef? existing))
                {
                    if (!RgbaEquals(existing!, canonical))
                        throw new InvalidOperationException(
                            $"Material name '{sanitized}' has conflicting definitions.");
                    // Same RGBA — reuse existing entry; nothing new to add.
                }
                else
                {
                    byName[sanitized] = canonical;
                    orderedMaterials.Add(canonical);
                }

                modelLinks.Add(new ModelLink(link, sanitized, null));
            }

            return (modelLinks, orderedMaterials);
        }

        private static void ValidateRgba(MaterialDef m)
        {
            if (m.R < 0.0 || m.R > 1.0 ||
                m.G < 0.0 || m.G > 1.0 ||
                m.B < 0.0 || m.B > 1.0 ||
                m.A < 0.0 || m.A > 1.0)
            {
                throw new ArgumentException(
                    $"Material '{m.Name}' has RGBA out of [0,1]: ({m.R},{m.G},{m.B},{m.A}).");
            }
        }

        private static bool RgbaEquals(MaterialDef a, MaterialDef b) =>
            System.Math.Abs(a.R - b.R) <= RgbaTolerance &&
            System.Math.Abs(a.G - b.G) <= RgbaTolerance &&
            System.Math.Abs(a.B - b.B) <= RgbaTolerance &&
            System.Math.Abs(a.A - b.A) <= RgbaTolerance;
    }
}
