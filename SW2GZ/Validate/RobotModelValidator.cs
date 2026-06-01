/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P9 — Structural pre-write validation of a RobotModel. Catches malformed
trees, non-PD inertia, broken refs, etc. BEFORE writers run. Complements
OutputValidator (which lints files-on-disk after writers complete).

These checks are FAIL-FAST: errors should be promoted to an exception by
the caller (Sw2gzPipeline) before any output directory is created. Warnings
flow back to the caller for surface in the PreExportReport.
*/
using System.Collections.Generic;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;

namespace SW2GZ.Validate
{
    public static class RobotModelValidator
    {
        // V7 — joint axis magnitude floor. Anything below this is effectively zero
        // and would crash downstream (URDF rejects axis="0 0 0" for revolute/prismatic).
        private const double AxisEpsilon = 1e-6;

        public static ValidationReport Validate(RobotModel model)
        {
            var issues = new List<ValidationIssue>();
            if (model == null)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error, "P9.E.MODEL_NULL",
                    "RobotModel is null.", "RobotModel"));
                return new ValidationReport(issues);
            }

            CheckLinksNonEmpty(model, issues);
            CheckLinkNameUniqueness(model, issues);
            CheckJointNameUniqueness(model, issues);
            CheckJointLinkRefs(model, issues);
            CheckTreeStructure(model, issues);
            CheckInertialAndMass(model, issues);
            CheckJointAxes(model, issues);
            CheckMaterialRefs(model, issues);
            CheckSensorRefs(model, issues);
            CheckControlRefs(model, issues);
            CheckFrame(model, issues);
            CheckPackageName(model, issues);

            return new ValidationReport(issues);
        }

        // V1 — Links must be non-empty. RobotModelBuilder already throws on empty,
        // but a direct primary-ctor construction can bypass it; this is the safety net.
        private static void CheckLinksNonEmpty(RobotModel model, List<ValidationIssue> issues)
        {
            if (model.Links == null || model.Links.Count == 0)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error, "P9.E.LINKS_EMPTY",
                    "RobotModel has zero links", "RobotModel.Links"));
            }
        }

        // V2 — Link.Name must be unique. Per-builder uniqueness is local; this catches
        // cross-builder collisions (e.g. two ModelLink lists merged manually).
        private static void CheckLinkNameUniqueness(RobotModel model, List<ValidationIssue> issues)
        {
            if (model.Links == null) return;
            var counts = new Dictionary<string, int>();
            foreach (ModelLink ml in model.Links)
            {
                string name = ml?.Link?.Name ?? string.Empty;
                counts.TryGetValue(name, out int n);
                counts[name] = n + 1;
            }
            foreach (KeyValuePair<string, int> kv in counts)
            {
                if (kv.Value > 1)
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Error, "P9.E.LINK_NAME_DUPE",
                        $"Link name '{kv.Key}' appears {kv.Value} times",
                        "RobotModel.Links"));
                }
            }
        }

        // V3 — Joint.Name must be unique.
        private static void CheckJointNameUniqueness(RobotModel model, List<ValidationIssue> issues)
        {
            if (model.Joints == null) return;
            var counts = new Dictionary<string, int>();
            foreach (UrdfJoint j in model.Joints)
            {
                string name = j?.Name ?? string.Empty;
                counts.TryGetValue(name, out int n);
                counts[name] = n + 1;
            }
            foreach (KeyValuePair<string, int> kv in counts)
            {
                if (kv.Value > 1)
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Error, "P9.E.JOINT_NAME_DUPE",
                        $"Joint name '{kv.Key}' appears {kv.Value} times",
                        "RobotModel.Joints"));
                }
            }
        }

        // V4 — every joint's ParentLink and ChildLink must resolve to a link by Name.
        private static void CheckJointLinkRefs(RobotModel model, List<ValidationIssue> issues)
        {
            if (model.Joints == null || model.Links == null) return;
            var linkNames = BuildLinkNameSet(model);
            foreach (UrdfJoint j in model.Joints)
            {
                if (j == null) continue;
                if (!linkNames.Contains(j.ParentLink))
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Error, "P9.E.JOINT_UNKNOWN_LINK",
                        $"Joint '{j.Name}' references unknown link '{j.ParentLink}'",
                        $"Joint '{j.Name}'"));
                }
                if (!linkNames.Contains(j.ChildLink))
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Error, "P9.E.JOINT_UNKNOWN_LINK",
                        $"Joint '{j.Name}' references unknown link '{j.ChildLink}'",
                        $"Joint '{j.Name}'"));
                }
            }
        }

        // V5 — kinematic tree: exactly one root when joints exist; every non-root has
        // exactly one parent; no cycles. Joint-less multi-link models emit a warning
        // per disconnected component (v2.1 ships disconnected-link export as a known
        // limitation — flag but don't block).
        private static void CheckTreeStructure(RobotModel model, List<ValidationIssue> issues)
        {
            if (model.Links == null || model.Links.Count == 0) return;
            var joints = model.Joints ?? (IReadOnlyList<UrdfJoint>)System.Array.Empty<UrdfJoint>();
            var linkNames = BuildLinkNameSet(model);

            // Joint-less case: each link is its own disconnected root. Warn per
            // extra link (v2.1 known limitation).
            if (joints.Count == 0)
            {
                if (model.Links.Count > 1)
                {
                    foreach (ModelLink ml in model.Links)
                    {
                        string name = ml?.Link?.Name ?? string.Empty;
                        issues.Add(new ValidationIssue(
                            IssueSeverity.Warning, "P9.W.DISCONNECTED_LINK",
                            $"Link '{name}' has no joints; v2.1 ships disconnected-link export as a known limitation",
                            $"Link '{name}'"));
                    }
                }
                return;
            }

            // Build child→parent count + parent→children adjacency. Skip joints whose
            // refs don't resolve — V4 already reported those.
            var parentCount = new Dictionary<string, int>();
            var adjacency = new Dictionary<string, List<string>>();
            foreach (ModelLink ml in model.Links)
            {
                string name = ml?.Link?.Name ?? string.Empty;
                parentCount[name] = 0;
                adjacency[name] = new List<string>();
            }
            foreach (UrdfJoint j in joints)
            {
                if (j == null) continue;
                if (!linkNames.Contains(j.ParentLink) || !linkNames.Contains(j.ChildLink))
                    continue;
                parentCount[j.ChildLink] = parentCount[j.ChildLink] + 1;
                adjacency[j.ParentLink].Add(j.ChildLink);
            }

            // V5a — multi-parent: any child with >1 incoming joint.
            foreach (KeyValuePair<string, int> kv in parentCount)
            {
                if (kv.Value > 1)
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Error, "P9.E.MULTI_PARENT",
                        $"Link '{kv.Key}' has {kv.Value} parent joints; URDF tree requires exactly one",
                        $"Link '{kv.Key}'"));
                }
            }

            // V5b — single root: collect links with zero incoming edges.
            var roots = new List<string>();
            foreach (KeyValuePair<string, int> kv in parentCount)
            {
                if (kv.Value == 0) roots.Add(kv.Key);
            }
            if (roots.Count == 0)
            {
                // All links have a parent — must be a cycle. Cycle detection below
                // will name the offending link; emit a top-level error here for context.
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error, "P9.E.MULTIPLE_ROOTS",
                    "No root link found (every link has a parent joint) — kinematic tree is cyclic",
                    "RobotModel"));
            }
            else if (roots.Count > 1)
            {
                roots.Sort(System.StringComparer.Ordinal);
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error, "P9.E.MULTIPLE_ROOTS",
                    $"Multiple root links found: {string.Join(", ", roots)}",
                    "RobotModel"));
            }

            // V5c — cycle detection via DFS coloring (0=unseen, 1=on-stack, 2=done).
            var color = new Dictionary<string, int>();
            foreach (string n in adjacency.Keys) color[n] = 0;
            var reportedCycleAt = new HashSet<string>(System.StringComparer.Ordinal);

            // Start from each root; if no roots (pure cycle), also start from every link.
            var seeds = roots.Count > 0 ? roots : new List<string>(adjacency.Keys);
            foreach (string seed in seeds)
            {
                DfsForCycles(seed, adjacency, color, reportedCycleAt, issues);
            }
            // Catch cycles in disconnected components that no root reaches.
            foreach (string n in adjacency.Keys)
            {
                if (color[n] == 0)
                    DfsForCycles(n, adjacency, color, reportedCycleAt, issues);
            }
        }

        // Iterative DFS with on-stack marking. Reports each cycle once per touched
        // link name. Iterative to avoid blowing the stack on deep trees.
        private static void DfsForCycles(
            string start,
            Dictionary<string, List<string>> adjacency,
            Dictionary<string, int> color,
            HashSet<string> reportedAt,
            List<ValidationIssue> issues)
        {
            if (!color.ContainsKey(start) || color[start] == 2) return;
            var stack = new Stack<(string Node, int ChildIdx)>();
            stack.Push((start, 0));
            color[start] = 1;
            while (stack.Count > 0)
            {
                (string node, int idx) = stack.Pop();
                List<string> children = adjacency[node];
                if (idx >= children.Count)
                {
                    color[node] = 2;
                    continue;
                }
                stack.Push((node, idx + 1));
                string child = children[idx];
                if (!color.ContainsKey(child)) continue;
                int c = color[child];
                if (c == 1)
                {
                    if (reportedAt.Add(child))
                    {
                        issues.Add(new ValidationIssue(
                            IssueSeverity.Error, "P9.E.CYCLE",
                            $"Cycle detected through link '{child}'",
                            $"Link '{child}'"));
                    }
                }
                else if (c == 0)
                {
                    color[child] = 1;
                    stack.Push((child, 0));
                }
            }
        }

        // V6 — Mass > 0 and inertia tensor sanity.
        //   • Mass <= 0   → P9.E.MASS_NONPOSITIVE.
        //   • Triangle inequality on principal moments → P9.W.INERTIA_TRIANGLE (warning).
        //     Necessary condition for a physically realizable rigid body: each principal
        //     moment ≤ sum of the other two. Skipped when Mass <= 0 (already errored).
        //   • Leading 2x2 minor det positive → P9.E.INERTIA_NOT_PD. If Ixx*Iyy - Ixy² ≤ 0
        //     the tensor cannot be positive-definite.
        private static void CheckInertialAndMass(RobotModel model, List<ValidationIssue> issues)
        {
            if (model.Links == null) return;
            foreach (ModelLink ml in model.Links)
            {
                if (ml?.Link == null) continue;
                UrdfLink link = ml.Link;
                if (link.Mass <= 0)
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Error, "P9.E.MASS_NONPOSITIVE",
                        $"Link '{link.Name}' has non-positive mass {link.Mass}",
                        $"Link '{link.Name}'"));
                    continue;
                }

                Matrix3 I = link.InertiaAtComLocal;
                double ixx = I.M11, iyy = I.M22, izz = I.M33;
                double ixy = I.M12;

                // Triangle inequality (warning only — thin shells legitimately approach
                // the boundary; don't block export, just flag).
                if (ixx + iyy < izz || iyy + izz < ixx || ixx + izz < iyy)
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Warning, "P9.W.INERTIA_TRIANGLE",
                        $"Link '{link.Name}' inertia violates triangle inequality (Ixx={ixx}, Iyy={iyy}, Izz={izz})",
                        $"Link '{link.Name}'"));
                }

                // Leading 2x2 minor must be positive-definite for the full tensor to be PD.
                double minor2 = ixx * iyy - ixy * ixy;
                if (ixx <= 0 || minor2 <= 0)
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Error, "P9.E.INERTIA_NOT_PD",
                        $"Link '{link.Name}' inertia tensor is not positive-definite (Ixx={ixx}, leading 2x2 det={minor2})",
                        $"Link '{link.Name}'"));
                }
            }
        }

        // V7 — joint axis must be non-zero for revolute/continuous/prismatic.
        private static void CheckJointAxes(RobotModel model, List<ValidationIssue> issues)
        {
            if (model.Joints == null) return;
            foreach (UrdfJoint j in model.Joints)
            {
                if (j == null) continue;
                if (j.Type == UrdfJointType.Fixed) continue;
                double ax = j.Axis.X, ay = j.Axis.Y, az = j.Axis.Z;
                double mag = System.Math.Sqrt(ax * ax + ay * ay + az * az);
                if (mag < AxisEpsilon)
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Error, "P9.E.JOINT_AXIS_ZERO",
                        $"Joint '{j.Name}' ({j.Type}) has zero axis vector",
                        $"Joint '{j.Name}'"));
                }
            }
        }

        // V8 — every ModelLink.MaterialName must resolve to a MaterialDef.
        private static void CheckMaterialRefs(RobotModel model, List<ValidationIssue> issues)
        {
            if (model.Links == null) return;
            var matNames = new HashSet<string>(System.StringComparer.Ordinal);
            if (model.Materials != null)
            {
                foreach (MaterialDef m in model.Materials)
                {
                    if (m != null) matNames.Add(m.Name);
                }
            }
            foreach (ModelLink ml in model.Links)
            {
                if (ml?.MaterialName == null) continue;
                if (!matNames.Contains(ml.MaterialName))
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Error, "P9.E.MATERIAL_REF_UNKNOWN",
                        $"Link '{ml.Link?.Name}' references unknown material '{ml.MaterialName}'",
                        $"Link '{ml.Link?.Name}'"));
                }
            }
        }

        // V9 — sensor AttachedLink (and ForceTorque ChildJointName) must resolve.
        private static void CheckSensorRefs(RobotModel model, List<ValidationIssue> issues)
        {
            if (model.Sensors == null || model.Sensors.Count == 0) return;
            var linkNames = BuildLinkNameSet(model);
            var jointNames = new HashSet<string>(System.StringComparer.Ordinal);
            if (model.Joints != null)
            {
                foreach (UrdfJoint j in model.Joints)
                {
                    if (j != null) jointNames.Add(j.Name);
                }
            }
            foreach (SensorDef s in model.Sensors)
            {
                if (s == null) continue;
                if (!linkNames.Contains(s.AttachedLink))
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Error, "P9.E.SENSOR_LINK_UNKNOWN",
                        $"Sensor '{s.Name}' references unknown attached link '{s.AttachedLink}'",
                        $"Sensor '{s.Name}'"));
                }
                if (s is ForceTorqueSensor ft && !jointNames.Contains(ft.ChildJointName))
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Error, "P9.E.SENSOR_JOINT_UNKNOWN",
                        $"ForceTorque sensor '{s.Name}' references unknown child joint '{ft.ChildJointName}'",
                        $"Sensor '{s.Name}'"));
                }
            }
        }

        // V10 — every Control.JointNames entry must map to an actual joint.
        private static void CheckControlRefs(RobotModel model, List<ValidationIssue> issues)
        {
            if (model.Control?.JointNames == null) return;
            var jointNames = new HashSet<string>(System.StringComparer.Ordinal);
            if (model.Joints != null)
            {
                foreach (UrdfJoint j in model.Joints)
                {
                    if (j != null) jointNames.Add(j.Name);
                }
            }
            foreach (string controlJoint in model.Control.JointNames)
            {
                if (controlJoint == null) continue;
                if (!jointNames.Contains(controlJoint))
                {
                    issues.Add(new ValidationIssue(
                        IssueSeverity.Error, "P9.E.CONTROL_JOINT_UNKNOWN",
                        $"ControlSpec references unknown joint '{controlJoint}'",
                        "RobotModel.Control"));
                }
            }
        }

        // V11 — CoordinateConvention.Validate (orthonormality + positive scale).
        private static void CheckFrame(RobotModel model, List<ValidationIssue> issues)
        {
            CoordinateConvention frame = model.Meta?.Frame;
            if (frame == null)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error, "P9.E.FRAME_INVALID",
                    "CoordinateConvention is null", "RobotModel.Meta.Frame"));
                return;
            }
            if (!frame.Validate())
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error, "P9.E.FRAME_INVALID",
                    "CoordinateConvention failed orthonormality / scale check",
                    "RobotModel.Meta.Frame"));
            }
        }

        // V12 — sanitized package name must survive (non-empty, non-whitespace).
        private static void CheckPackageName(RobotModel model, List<ValidationIssue> issues)
        {
            string pkg = model.Meta?.PackageName;
            if (string.IsNullOrWhiteSpace(pkg))
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error, "P9.E.PACKAGE_NAME_EMPTY",
                    "RobotMeta.PackageName is null or whitespace",
                    "RobotModel.Meta.PackageName"));
            }
        }

        private static HashSet<string> BuildLinkNameSet(RobotModel model)
        {
            var names = new HashSet<string>(System.StringComparer.Ordinal);
            if (model.Links == null) return names;
            foreach (ModelLink ml in model.Links)
            {
                if (ml?.Link?.Name != null) names.Add(ml.Link.Name);
            }
            return names;
        }
    }
}
