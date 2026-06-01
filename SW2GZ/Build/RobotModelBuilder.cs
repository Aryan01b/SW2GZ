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
        // 1e-6 is ~4000× tighter than 8-bit color resolution (1/255), tolerates float<->double round-trip,
        private const double RgbaTolerance = 1e-6;
        // but still triggers on intentional RGBA edits. Tune if SW appearance round-trip introduces wider drift.

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
            // Track the raw (pre-sanitization) name of the first material seen for
            // each sanitized key so a conflict can report both colliding inputs.
            var byName = new Dictionary<string, (MaterialDef Canonical, string FirstSeenRawName)>(StringComparer.Ordinal);

            foreach ((UrdfLink link, string partPath) in linksWithPaths)
            {
                MaterialDef? raw = appearanceSource.GetMaterial(partPath);
                if (raw == null)
                {
                    modelLinks.Add(new ModelLink(link, null, null));
                    continue;
                }

                ValidateRgba(raw);

                // RosNameSanitizer guarantees a non-empty result (returns "unnamed"
                // for inputs that would otherwise sanitize to empty), so no empty-
                // string guard is needed here.
                string sanitized = RosNameSanitizer.Sanitize(raw.Name).Value;

                MaterialDef canonical = raw with { Name = sanitized };

                if (byName.TryGetValue(sanitized, out (MaterialDef Canonical, string FirstSeenRawName) existing))
                {
                    if (!RgbaEquals(existing.Canonical, canonical))
                        throw new InvalidOperationException(
                            $"Material name '{sanitized}' has conflicting definitions: " +
                            $"first seen as '{existing.FirstSeenRawName}', then as '{raw.Name}' with different RGBA.");
                    // Same RGBA — reuse existing entry; nothing new to add.
                }
                else
                {
                    byName[sanitized] = (canonical, raw.Name);
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

        /// P6-data — validates a sensor list and returns a parallel list with
        /// sanitized names + topics. Mutations preserve order and concrete type
        /// (each SensorDef subclass round-trips via record `with`).
        ///
        /// Validation rules:
        ///   - Name must be non-empty; sanitized via RosNameSanitizer.
        ///   - Names must be unique post-sanitization (collision => InvalidOperationException).
        ///   - AttachedLink must reference a link in modelLinks (by Link.Name).
        ///   - Topic must start with '/'; non-slash prefix is added, then the
        ///     rest sanitized as a ROS-name segment.
        ///   - UpdateRate must be > 0.
        ///   - ForceTorqueSensor.ChildJointName must resolve to a joint in `joints`.
        public static IReadOnlyList<SensorDef> AssembleSensors(
            IReadOnlyList<SensorDef> rawSensors,
            IReadOnlyList<ModelLink> modelLinks,
            IReadOnlyList<UrdfJoint> joints)
        {
            if (rawSensors == null) throw new ArgumentNullException(nameof(rawSensors));
            if (modelLinks == null) throw new ArgumentNullException(nameof(modelLinks));
            if (joints == null) throw new ArgumentNullException(nameof(joints));

            if (rawSensors.Count == 0)
                return Array.Empty<SensorDef>();

            var linkNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModelLink ml in modelLinks)
                linkNames.Add(ml.Link.Name);

            var jointNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (UrdfJoint j in joints)
                jointNames.Add(j.Name);

            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            var seenTopics = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<SensorDef>(rawSensors.Count);

            foreach (SensorDef s in rawSensors)
            {
                if (s == null)
                    throw new ArgumentException("Sensor list contains a null entry.", nameof(rawSensors));

                if (string.IsNullOrWhiteSpace(s.Name))
                    throw new ArgumentException("Sensor Name must be non-empty.", nameof(rawSensors));

                string sanitizedName = RosNameSanitizer.Sanitize(s.Name).Value;
                if (!seenNames.Add(sanitizedName))
                    throw new InvalidOperationException(
                        $"Duplicate sensor name '{sanitizedName}' after sanitization.");

                if (!linkNames.Contains(s.AttachedLink))
                    throw new ArgumentException(
                        $"Sensor '{sanitizedName}' references unknown link '{s.AttachedLink}'.",
                        nameof(rawSensors));

                if (s.UpdateRate <= 0)
                    throw new ArgumentException(
                        $"Sensor '{sanitizedName}' has non-positive UpdateRate {s.UpdateRate}.",
                        nameof(rawSensors));

                string sanitizedTopic = SanitizeTopic(s.Topic);
                if (!seenTopics.Add(sanitizedTopic))
                    throw new InvalidOperationException(
                        $"Topic '{sanitizedTopic}' used by more than one sensor; ros_gz_bridge would reject duplicates.");

                SensorDef updated = s switch
                {
                    ImuSensor imu => imu with { Name = sanitizedName, Topic = sanitizedTopic },
                    GpuLidarSensor lidar => lidar with { Name = sanitizedName, Topic = sanitizedTopic },
                    CameraSensor cam => cam with { Name = sanitizedName, Topic = sanitizedTopic },
                    DepthCameraSensor dcam => dcam with { Name = sanitizedName, Topic = sanitizedTopic },
                    ContactSensor c => c with { Name = sanitizedName, Topic = sanitizedTopic },
                    NavsatSensor n => n with { Name = sanitizedName, Topic = sanitizedTopic },
                    ForceTorqueSensor ft => ValidateAndUpdateForceTorque(ft, sanitizedName, sanitizedTopic, joints, jointNames),
                    _ => throw new InvalidOperationException($"Unhandled SensorDef subtype: {s.GetType().Name}"),
                };

                result.Add(updated);
            }

            return result;
        }

        private static ForceTorqueSensor ValidateAndUpdateForceTorque(
            ForceTorqueSensor ft, string sanitizedName, string sanitizedTopic,
            IReadOnlyList<UrdfJoint> joints, HashSet<string> jointNames)
        {
            if (joints.Count == 0)
                throw new ArgumentException(
                    "ForceTorque sensor cannot reference a joint when no joints are defined.");

            if (string.IsNullOrEmpty(ft.ChildJointName) || !jointNames.Contains(ft.ChildJointName))
                throw new ArgumentException(
                    $"ForceTorque sensor '{sanitizedName}' references unknown joint '{ft.ChildJointName}'.");

            return ft with { Name = sanitizedName, Topic = sanitizedTopic };
        }

        // Topics: must start with '/'. The leading '/' is preserved verbatim; the
        // remainder is split on '/' so each segment can be sanitized independently
        // (RosNameSanitizer treats '/' as invalid and would collapse multi-segment
        // topics into a single underscored blob otherwise).
        private static string SanitizeTopic(string raw)
        {
            string s = raw ?? string.Empty;
            if (!s.StartsWith("/", StringComparison.Ordinal))
                s = "/" + s;
            string remainder = s.Substring(1);
            if (remainder.Length == 0)
                return "/unnamed";
            string[] segments = remainder.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (string.IsNullOrEmpty(segments[i])) continue;
                segments[i] = RosNameSanitizer.Sanitize(segments[i]).Value;
            }
            return "/" + string.Join("/", segments);
        }
    }
}
