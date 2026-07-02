# Robot-mode Joints Step (Phase 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Joints step to the robot-mode "Create Robot" wizard so the user can set each joint's Type (Fixed/Revolute/Continuous/Prismatic), axis, and motion limits — and wire those values into the URDF exporter, which currently ignores `JointDef` entirely and hardcodes every joint to `type="fixed"`.

**Architecture:** New pure helper `JointDefReconciler` (merge-preserve list sync, unit-testable) replaces the clear-and-rebuild `RebuildJoints()`. `Sw2gzRobotExporter` starts reading `config.RobotJoints` (already round-tripped, currently dead) to emit real `type`/`<axis>`/`<limit>` elements, rotating the user's assembly-frame axis into the child link's local frame with the same `Matrix3` transpose-multiply pattern already proven for joint origins. `Sw2gzCreateRobotPmp` gains a third wizard step: a native listbox of joints plus a shared detail form (rename box, type combobox, axis/limit numberboxes) that loads/commits per-selection, mirroring the already-shipped `Sw2gzCreateAssetPmp` combobox+numberbox pattern.

**Tech Stack:** C# / .NET Framework (SW COM interop, `#if SW_INTEROP`), xUnit (net8.0 test project), SolidWorks PropertyManagerPage API.

**Spec:** [`docs/superpowers/specs/2026-07-03-robot-joint-type-panel-design.md`](../specs/2026-07-03-robot-joint-type-panel-design.md)

---

## File Structure

- **Create** `SW2GZ/Build/JointDefReconciler.cs` — pure, COM-free. Merge-preserve sync of `Robot.Joints` against `Robot.Links`, mirrors the existing `SW2GZ/Build/LinkHierarchy.cs` pattern (static helper class, `List<LinkDef>` in, pure logic, unit-tested independently of the COM-gated PMP).
- **Create** `Test/Build/JointDefReconcilerTests.cs` — unit tests for the above.
- **Modify** `SW2GZ/URDFExport/Sw2gzRobotExporter.cs` — `Export()` reads `config.RobotJoints`, computes each joint's axis rotated into child-local frame; `WriteUrdf()` emits real `type`/`<axis>`/`<limit>` instead of hardcoded `"fixed"`.
- **Modify** `Test/URDFExport/Sw2gzRobotExporterTests.cs` — new test cases for the above (existing 12 tests must stay green unmodified — they don't set `RobotJoints`, so they exercise the backward-compat "no JointDef found → default Fixed" path).
- **Modify** `SW2GZ/UI/Pmp/Sw2gzCreateRobotPmp.cs` — new Joints step (listbox + detail form), `RebuildJoints()` now delegates to `JointDefReconciler`. Not unit-testable (`#if SW_INTEROP`-gated COM class) — verified by build + manual live SW check.
- **Modify** `SW2GZ/SW2GZ.csproj`, `Test/SW2GZ.Writers.Test.csproj` — add the new file (per this repo's convention: new `.cs` files must be added to both).

---

### Task 1: `JointDefReconciler` — merge-preserve joint list sync

**Files:**
- Create: `SW2GZ/Build/JointDefReconciler.cs`
- Create: `Test/Build/JointDefReconcilerTests.cs`
- Modify: `SW2GZ/SW2GZ.csproj`
- Modify: `Test/SW2GZ.Writers.Test.csproj`

- [ ] **Step 1: Write the failing tests**

```csharp
/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Collections.Generic;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using Xunit;

namespace SW2GZ.Writers.Tests
{
    public class JointDefReconcilerTests
    {
        [Fact]
        public void Reconcile_NewLink_CreatesDefaultFixedJoint()
        {
            var links = new List<LinkDef>
            {
                new LinkDef { Name = "base_link", ParentName = "" },
                new LinkDef { Name = "arm_link",  ParentName = "base_link" },
            };

            List<JointDef> result = JointDefReconciler.Reconcile(new List<JointDef>(), links);

            JointDef j = Assert.Single(result);
            Assert.Equal("base_link_to_arm_link", j.Name);
            Assert.Equal("base_link", j.ParentLink);
            Assert.Equal("arm_link", j.ChildLink);
            Assert.Equal(UrdfJointType.Fixed, j.Type);
        }

        [Fact]
        public void Reconcile_ExistingPairPreserved_KeepsUserEdits()
        {
            var links = new List<LinkDef>
            {
                new LinkDef { Name = "base_link", ParentName = "" },
                new LinkDef { Name = "arm_link",  ParentName = "base_link" },
            };
            var existing = new List<JointDef>
            {
                new JointDef
                {
                    Name = "shoulder", ParentLink = "base_link", ChildLink = "arm_link",
                    Type = UrdfJointType.Revolute, AxisZ = 1, LimitLower = -1.0, LimitUpper = 1.0,
                },
            };

            List<JointDef> result = JointDefReconciler.Reconcile(existing, links);

            JointDef j = Assert.Single(result);
            Assert.Same(existing[0], j);
            Assert.Equal("shoulder", j.Name);
            Assert.Equal(UrdfJointType.Revolute, j.Type);
            Assert.Equal(1.0, j.AxisZ);
        }

        [Fact]
        public void Reconcile_RemovedLink_DropsItsJoint()
        {
            var links = new List<LinkDef>
            {
                new LinkDef { Name = "base_link", ParentName = "" },
                new LinkDef { Name = "arm_link",  ParentName = "base_link" },
            };
            var existing = new List<JointDef>
            {
                new JointDef { Name = "base_link_to_arm_link", ParentLink = "base_link", ChildLink = "arm_link", Type = UrdfJointType.Revolute },
                new JointDef { Name = "arm_link_to_wrist_link", ParentLink = "arm_link", ChildLink = "wrist_link", Type = UrdfJointType.Continuous },
            };

            List<JointDef> result = JointDefReconciler.Reconcile(existing, links);

            JointDef j = Assert.Single(result);
            Assert.Equal("arm_link", j.ChildLink);
        }

        [Fact]
        public void Reconcile_RootOnlyLink_ReturnsEmptyJointList()
        {
            var links = new List<LinkDef> { new LinkDef { Name = "base_link", ParentName = "" } };

            List<JointDef> result = JointDefReconciler.Reconcile(new List<JointDef>(), links);

            Assert.Empty(result);
        }

        [Fact]
        public void Reconcile_NullExistingAndNullLinks_DoesNotThrow()
        {
            Assert.Empty(JointDefReconciler.Reconcile(null, null));
            Assert.Empty(JointDefReconciler.Reconcile(null, new List<LinkDef>()));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter JointDefReconcilerTests`
Expected: compile error — `JointDefReconciler` does not exist yet.

- [ ] **Step 3: Write the implementation**

```csharp
/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Syncs Robot.Joints against Robot.Links after any link-tree edit
(add/remove/reparent). Previously RebuildJoints() cleared and rebuilt every
JointDef from scratch on each edit — harmless while Type stayed hardcoded
Fixed, but would silently discard the user's Type/Axis/Limit edits (see
docs/superpowers/specs/2026-07-03-robot-joint-type-panel-design.md) once
those fields became real. Match by (ParentLink, ChildLink): a pair that
still exists keeps its JointDef untouched; new pairs get a fresh
default-Fixed JointDef; pairs whose link was removed/reparented away are
dropped.
*/
using System.Collections.Generic;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;

namespace SW2GZ.Build
{
    public static class JointDefReconciler
    {
        public static List<JointDef> Reconcile(IReadOnlyList<JointDef> existing, IReadOnlyList<LinkDef> links)
        {
            var existingByPair = new Dictionary<(string Parent, string Child), JointDef>();
            if (existing != null)
            {
                foreach (JointDef j in existing)
                    existingByPair[(j.ParentLink, j.ChildLink)] = j;
            }

            var result = new List<JointDef>();
            if (links == null) return result;

            foreach (LinkDef link in links)
            {
                if (string.IsNullOrEmpty(link.ParentName)) continue;

                var key = (link.ParentName, link.Name);
                if (existingByPair.TryGetValue(key, out JointDef kept))
                {
                    result.Add(kept);
                }
                else
                {
                    result.Add(new JointDef
                    {
                        Name = link.ParentName + "_to_" + link.Name,
                        ParentLink = link.ParentName,
                        ChildLink = link.Name,
                        Type = UrdfJointType.Fixed,
                    });
                }
            }
            return result;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter JointDefReconcilerTests`
Expected: 5 passed.

- [ ] **Step 5: Add the new file to both csproj files**

In `SW2GZ/SW2GZ.csproj`, next to the existing `Build\LinkHierarchy.cs` entry (around line 378):

```xml
    <Compile Include="Build\LinkHierarchy.cs" />
    <Compile Include="Build\JointDefReconciler.cs" />
```

In `Test/SW2GZ.Writers.Test.csproj`, next to the existing `Build\LinkHierarchy.cs` link entry (around line 50):

```xml
    <Compile Include="..\SW2GZ\Build\LinkHierarchy.cs"          Link="Sources\Build\LinkHierarchy.cs" />
    <Compile Include="..\SW2GZ\Build\JointDefReconciler.cs"     Link="Sources\Build\JointDefReconciler.cs" />
```

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj`
Expected: all tests pass, count increased by 5 from the baseline (481 → 486; confirm current baseline with `dotnet test` before this task if it has drifted).

- [ ] **Step 7: Commit**

```bash
git add SW2GZ/Build/JointDefReconciler.cs Test/Build/JointDefReconcilerTests.cs SW2GZ/SW2GZ.csproj Test/SW2GZ.Writers.Test.csproj
git commit -m "feat(robot): add JointDefReconciler, merge-preserve joint list sync"
```

---

### Task 2: Wire `JointDef` Type/Axis/Limit into `Sw2gzRobotExporter`

**Files:**
- Modify: `SW2GZ/URDFExport/Sw2gzRobotExporter.cs`
- Test: `Test/URDFExport/Sw2gzRobotExporterTests.cs`

- [ ] **Step 1: Write the failing tests**

Add these to `Test/URDFExport/Sw2gzRobotExporterTests.cs`, inside the `Sw2gzRobotExporterTests` class (after the existing `Export_MultiComponentLink_UsesEachComponentsOwnPose_NotSharedLinkFrame` test, before the closing brace):

```csharp
        [Fact]
        public void Export_UsesJointDefType_InsteadOfHardcodedFixed()
        {
            var cfg = Cfg();
            cfg.RobotJoints = new List<JointDef>
            {
                new JointDef { Name = "shoulder", ParentLink = "base_link", ChildLink = "arm_link", Type = UrdfJointType.Continuous },
            };
            Sw2gzRobotExporter.Export(new FakeTess(), new FakeMassProps(), new FakePoses(), cfg, _dir, Matrix3.Identity);

            XElement joint = UrdfRoot().Elements("joint").Single();
            Assert.Equal("shoulder", (string)joint.Attribute("name"));
            Assert.Equal("continuous", (string)joint.Attribute("type"));
        }

        [Fact]
        public void Export_WritesAxisRotatedIntoChildLocalFrame()
        {
            // Child rotated 90deg about Z in the assembly; axis set to
            // assembly +X. In the child's own (locally un-rotated) frame
            // that same physical direction reads as approximately +Y —
            // R_child^T undoes the child's own 90deg rotation.
            var poses = new Dictionary<string, (Matrix3, Vector3)>
            {
                ["base-1@asm"] = (Matrix3.Identity, Vector3.Zero),
                ["arm-1@asm"]  = (RotZ(System.Math.PI / 2), Vector3.Zero),
            };
            var cfg = Cfg();
            cfg.RobotJoints = new List<JointDef>
            {
                new JointDef { Name = "j1", ParentLink = "base_link", ChildLink = "arm_link", Type = UrdfJointType.Revolute, AxisX = 1, AxisY = 0, AxisZ = 0 },
            };
            Sw2gzRobotExporter.Export(new FakeTess(), new FakeMassProps(), new FakePoses(poses), cfg, _dir, Matrix3.Identity);

            XElement joint = UrdfRoot().Elements("joint").Single();
            string[] xyz = ((string)joint.Element("axis").Attribute("xyz")).Split(' ');
            Assert.Equal(0.0, double.Parse(xyz[0]), 3);
            Assert.Equal(1.0, double.Parse(xyz[1]), 3);
            Assert.Equal(0.0, double.Parse(xyz[2]), 3);
        }

        [Fact]
        public void Export_WritesLimitElement_ForRevoluteAndPrismatic()
        {
            var cfg = Cfg();
            cfg.RobotJoints = new List<JointDef>
            {
                new JointDef
                {
                    Name = "j1", ParentLink = "base_link", ChildLink = "arm_link",
                    Type = UrdfJointType.Prismatic, AxisX = 0, AxisY = 0, AxisZ = 1,
                    LimitLower = -0.5, LimitUpper = 0.5,
                },
            };
            Sw2gzRobotExporter.Export(new FakeTess(), new FakeMassProps(), new FakePoses(), cfg, _dir, Matrix3.Identity);

            XElement joint = UrdfRoot().Elements("joint").Single();
            Assert.Equal(-0.5, double.Parse((string)joint.Element("limit").Attribute("lower"), CultureInfo.InvariantCulture), 3);
            Assert.Equal(0.5, double.Parse((string)joint.Element("limit").Attribute("upper"), CultureInfo.InvariantCulture), 3);
        }

        [Fact]
        public void Export_FixedType_EmitsNoAxisOrLimitElements()
        {
            var cfg = Cfg();
            cfg.RobotJoints = new List<JointDef>
            {
                new JointDef { Name = "j1", ParentLink = "base_link", ChildLink = "arm_link", Type = UrdfJointType.Fixed },
            };
            Sw2gzRobotExporter.Export(new FakeTess(), new FakeMassProps(), new FakePoses(), cfg, _dir, Matrix3.Identity);

            XElement joint = UrdfRoot().Elements("joint").Single();
            Assert.Null(joint.Element("axis"));
            Assert.Null(joint.Element("limit"));
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter Sw2gzRobotExporterTests`
Expected: the 4 new tests FAIL (`Export_UsesJointDefType_InsteadOfHardcodedFixed` and `Export_FixedType_EmitsNoAxisOrLimitElements` fail because type is still always `"fixed"` — wait, `Export_FixedType_...` would actually pass by coincidence since Fixed emits nothing either way; the meaningful failures are the other 3: type stays `"fixed"` instead of `"continuous"`, and `joint.Element("axis")`/`joint.Element("limit")` are always null since nothing emits them yet). Existing 12 tests still pass unmodified.

- [ ] **Step 3: Implement the exporter changes**

In `SW2GZ/URDFExport/Sw2gzRobotExporter.cs`, inside `Export()`, after the existing dictionary declarations (around line 101-102):

```csharp
            var jointOrigins = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            var jointRpys = new Dictionary<string, (double, double, double)>(StringComparer.Ordinal);
            var jointAxesLocal = new Dictionary<string, Vector3>(StringComparer.Ordinal);

            List<JointDef> jointDefs = config.RobotJoints ?? new List<JointDef>();
            var jointByChild = new Dictionary<string, JointDef>(StringComparer.Ordinal);
            foreach (JointDef j in jointDefs) jointByChild[j.ChildLink] = j;
```

Then, inside the per-link loop, extend the existing joint-origin block (around line 145-158) — this is the block that currently reads:

```csharp
                if (!string.IsNullOrEmpty(link.ParentName) &&
                    linkPoses.TryGetValue(link.ParentName, out (Matrix3 R, Vector3 T) parentPose))
                {
                    Matrix3 rJoint = parentPose.R.Transpose() * linkR;
                    Vector3 tJoint = parentPose.R.Transpose().Mul(linkT - parentPose.T);
                    jointOrigins[link.Name] = tJoint;
                    jointRpys[link.Name] = rJoint.ToRpy();
                }
```

Add the axis computation right after `jointRpys[link.Name] = rJoint.ToRpy();`, still inside the same `if` block:

```csharp
                    jointOrigins[link.Name] = tJoint;
                    jointRpys[link.Name] = rJoint.ToRpy();

                    // Axis is stored in assembly frame (what the user sees/enters
                    // in the Joints panel — see the design spec's "Axis frame"
                    // decision); URDF's <axis> is expressed in the joint frame,
                    // which for a plain joint (no extra offset) is the child
                    // link's own local frame. Same transpose-multiply pattern
                    // already proven correct for the joint origin above.
                    if (jointByChild.TryGetValue(link.Name, out JointDef jd) &&
                        jd.Type != UrdfJointType.Fixed && jd.HasAxis)
                    {
                        var axisAssembly = new Vector3((float)jd.AxisX, (float)jd.AxisY, (float)jd.AxisZ);
                        jointAxesLocal[link.Name] = linkR.Transpose().Mul(axisAssembly);
                    }
```

Update the `WriteUrdf` call (around line 164) to pass the two new pieces of data:

```csharp
            WriteUrdf(urdfPath, pkg, baseLinkName, links, meshFiles, masses, jointOrigins, jointRpys,
                jointByChild, jointAxesLocal, config.EmitWorldLink, swToRos);
```

Update `WriteUrdf`'s signature (around line 272-276):

```csharp
        private static void WriteUrdf(
            string path, string pkg, string baseLinkName, List<LinkDef> links,
            Dictionary<string, string> meshFiles, Dictionary<string, MassProps> masses,
            Dictionary<string, Vector3> jointOrigins, Dictionary<string, (double, double, double)> jointRpys,
            Dictionary<string, JointDef> jointByChild, Dictionary<string, Vector3> jointAxesLocal,
            bool emitWorldLink, Matrix3 swToRos)
```

Replace the joint-writing loop (around line 347-363) — currently:

```csharp
            foreach (LinkDef link in links)
            {
                if (string.IsNullOrEmpty(link.ParentName)) continue;
                Vector3 origin = jointOrigins.TryGetValue(link.Name, out var o) ? o : Vector3.Zero;
                (double roll, double pitch, double yaw) = jointRpys.TryGetValue(link.Name, out var rpy)
                    ? rpy : (0.0, 0.0, 0.0);
                w.WriteStartElement("joint");
                w.WriteAttributeString("name", link.ParentName + "_to_" + link.Name);
                w.WriteAttributeString("type", "fixed");
                w.WriteStartElement("parent"); w.WriteAttributeString("link", link.ParentName); w.WriteEndElement();
                w.WriteStartElement("child"); w.WriteAttributeString("link", link.Name); w.WriteEndElement();
                w.WriteStartElement("origin");
                w.WriteAttributeString("xyz", Fmt(origin.X) + " " + Fmt(origin.Y) + " " + Fmt(origin.Z));
                w.WriteAttributeString("rpy", Fmt(roll) + " " + Fmt(pitch) + " " + Fmt(yaw));
                w.WriteEndElement();
                w.WriteEndElement();
            }
```

with:

```csharp
            foreach (LinkDef link in links)
            {
                if (string.IsNullOrEmpty(link.ParentName)) continue;
                Vector3 origin = jointOrigins.TryGetValue(link.Name, out var o) ? o : Vector3.Zero;
                (double roll, double pitch, double yaw) = jointRpys.TryGetValue(link.Name, out var rpy)
                    ? rpy : (0.0, 0.0, 0.0);

                jointByChild.TryGetValue(link.Name, out JointDef jd);
                UrdfJointType type = jd?.Type ?? UrdfJointType.Fixed;
                string jointName = !string.IsNullOrEmpty(jd?.Name) ? jd.Name : link.ParentName + "_to_" + link.Name;

                w.WriteStartElement("joint");
                w.WriteAttributeString("name", jointName);
                w.WriteAttributeString("type", JointTypeString(type));
                w.WriteStartElement("parent"); w.WriteAttributeString("link", link.ParentName); w.WriteEndElement();
                w.WriteStartElement("child"); w.WriteAttributeString("link", link.Name); w.WriteEndElement();
                w.WriteStartElement("origin");
                w.WriteAttributeString("xyz", Fmt(origin.X) + " " + Fmt(origin.Y) + " " + Fmt(origin.Z));
                w.WriteAttributeString("rpy", Fmt(roll) + " " + Fmt(pitch) + " " + Fmt(yaw));
                w.WriteEndElement();

                if (type != UrdfJointType.Fixed)
                {
                    Vector3 axis = jointAxesLocal.TryGetValue(link.Name, out var a) ? a : new Vector3(0, 0, 1);
                    w.WriteStartElement("axis");
                    w.WriteAttributeString("xyz", Fmt(axis.X) + " " + Fmt(axis.Y) + " " + Fmt(axis.Z));
                    w.WriteEndElement();

                    if (type == UrdfJointType.Revolute || type == UrdfJointType.Prismatic)
                    {
                        w.WriteStartElement("limit");
                        w.WriteAttributeString("lower", Fmt(jd?.LimitLower ?? 0.0));
                        w.WriteAttributeString("upper", Fmt(jd?.LimitUpper ?? 0.0));
                        w.WriteEndElement();
                    }
                }

                w.WriteEndElement();
            }
```

Add the type-mapping helper, next to `Fmt` at the bottom of the class:

```csharp
        // Planar/Floating aren't offered in the Joints panel (no use case yet
        // in this codebase) — fall back to fixed rather than throw, so a
        // hand-edited or legacy JointDef with one of those values doesn't
        // crash export.
        private static string JointTypeString(UrdfJointType t) => t switch
        {
            UrdfJointType.Fixed      => "fixed",
            UrdfJointType.Revolute   => "revolute",
            UrdfJointType.Continuous => "continuous",
            UrdfJointType.Prismatic  => "prismatic",
            _ => "fixed",
        };
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter Sw2gzRobotExporterTests`
Expected: all tests pass (12 existing + 4 new = 16).

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj`
Expected: all pass, count up by 4 from Task 1's total.

- [ ] **Step 6: Commit**

```bash
git add SW2GZ/URDFExport/Sw2gzRobotExporter.cs Test/URDFExport/Sw2gzRobotExporterTests.cs
git commit -m "feat(robot): exporter emits real joint type/axis/limit from JointDef"
```

---

### Task 3: Wire `RebuildJoints()` to the reconciler

**Files:**
- Modify: `SW2GZ/UI/Pmp/Sw2gzCreateRobotPmp.cs`

This class is `#if SW_INTEROP`-gated (COM-dependent) and not part of the net8 test project — this task is verified by a clean SolidWorks-addin build, not `dotnet test`.

- [ ] **Step 1: Add the `using` for the new namespace**

`SW2GZ.Build` is already imported for `LinkHierarchy` usage elsewhere in this file — no new `using` needed (confirm `using SW2GZ.Build;` is present near the top; it already is, line 32).

- [ ] **Step 2: Replace `RebuildJoints()`**

Replace (current, around line 485-504):

```csharp
        // Joints are pure derived state: one Fixed joint per non-root link,
        // straight off the ParentName the user picked at Add time. Re-run
        // after every list mutation so Links/Joints never drift out of sync.
        // Joint TYPE stays hardcoded Fixed in this cut (mate-driven detection
        // was attempted and reverted — see memory `robot-mode-dev`).
        private void RebuildJoints()
        {
            _liveDoc.Robot.Joints.Clear();
            foreach (LinkDef link in _liveDoc.Robot.Links)
            {
                if (string.IsNullOrEmpty(link.ParentName)) continue;
                _liveDoc.Robot.Joints.Add(new JointDef
                {
                    Name = link.ParentName + "_to_" + link.Name,
                    ParentLink = link.ParentName,
                    ChildLink = link.Name,
                    Type = UrdfJointType.Fixed,
                });
            }
        }
```

with:

```csharp
        // Joints stay 1:1 with the link tree (one per non-root link), but
        // MERGE-preserve instead of clear-and-rebuild — a link add/remove/
        // reparent elsewhere must not wipe out Type/Axis/Limit edits the
        // user already made on a joint whose (parent, child) pair is
        // unaffected. See JointDefReconciler and
        // docs/superpowers/specs/2026-07-03-robot-joint-type-panel-design.md.
        private void RebuildJoints()
        {
            _liveDoc.Robot.Joints = JointDefReconciler.Reconcile(_liveDoc.Robot.Joints, _liveDoc.Robot.Links);
        }
```

- [ ] **Step 3: Update the file's header doc comment**

The top-of-file comment (lines 19-22) currently says:

```
Steps map to Sw2gzDoc.Robot:
    0 — Links    (pick mesh -> name -> parent -> Add; first Add = base_link;
                  Joints rebuilt (Fixed) from each link's ParentName)
    1 — Review   (counts; Next caption flips to "Finish")
```

Leave this as-is for now — Task 4 changes the step count and will update this comment together with the step-list change, so the doc comment and the step list land in the same commit.

- [ ] **Step 4: Build the addin**

Run the project's normal SolidWorks-addin build (see memory `sw2gz-build-deploy` for the exact MSBuild command/`SolutionDir` param — SolidWorks must be closed). Confirm a clean build with no new warnings/errors in `Sw2gzCreateRobotPmp.cs`.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj`
Expected: unchanged pass count from Task 2 (this task touches no test-project code).

- [ ] **Step 6: Commit**

```bash
git add SW2GZ/UI/Pmp/Sw2gzCreateRobotPmp.cs
git commit -m "refactor(robot): RebuildJoints uses JointDefReconciler, preserves edits"
```

---

### Task 4: Joints step — scaffold + joint list (read-only checkpoint)

**Files:**
- Modify: `SW2GZ/UI/Pmp/Sw2gzCreateRobotPmp.cs`

This is deliberately the smallest possible wiring step before adding the editable detail form in Task 5 — per the project's own lesson (memory `robot-mode-dev`): this class of PMP/COM change has broken live in SolidWorks twice before despite every automated gate passing, so get a live checkpoint on step navigation + list population alone before adding editing on top. Not unit-testable; verified by build + the live check at the end of this task.

- [ ] **Step 1: Update step constants and names**

Replace (current, lines 55-58):

```csharp
        private const int StepLinks  = 0;
        private const int StepReview = 1;
        private static readonly string[] StepNames = { "Links", "Review" };
        private const int StepCount = 2;
```

with:

```csharp
        private const int StepLinks  = 0;
        private const int StepJoints = 1;
        private const int StepReview = 2;
        private static readonly string[] StepNames = { "Links", "Joints", "Review" };
        private const int StepCount = 3;
```

- [ ] **Step 2: Update the file header doc comment**

Replace (lines 19-22):

```
Steps map to Sw2gzDoc.Robot:
    0 — Links    (pick mesh -> name -> parent -> Add; first Add = base_link;
                  Joints rebuilt (Fixed) from each link's ParentName)
    1 — Review   (counts; Next caption flips to "Finish")
```

with:

```
Steps map to Sw2gzDoc.Robot:
    0 — Links    (pick mesh -> name -> parent -> Add; first Add = base_link;
                  Joints re-synced (merge-preserve) from each link's
                  ParentName via JointDefReconciler)
    1 — Joints   (one row per non-root link; edit type/axis/limit for the
                  selected row)
    2 — Review   (counts; Next caption flips to "Finish")
```

- [ ] **Step 3: Add new control IDs and fields**

Add after the existing `IdLinksTree = 18;` line (line 77), before `MeshSelectionMark`:

```csharp
        private const int IdJointsGroup        = 20;
        private const int IdJointsDescr        = 21;
        private const int IdJointsList         = 22;
        private const int IdJointNameLabel     = 23;
        private const int IdJointNameBox       = 24;
        private const int IdJointTypeLabel     = 25;
        private const int IdJointTypeCombo     = 26;
        private const int IdJointAxisLabel     = 27;
        private const int IdJointAxisXBox      = 28;
        private const int IdJointAxisYBox      = 29;
        private const int IdJointAxisZBox      = 30;
        private const int IdJointLimitLabel    = 31;
        private const int IdJointLimitLowerBox = 32;
        private const int IdJointLimitUpperBox = 33;
```

Renumber the Review-step IDs (current lines 82-86) from the `20`s to the `40`s so they no longer collide with the new Joints IDs:

```csharp
        private const int IdReviewGroup       = 40;
        private const int IdReviewDescr       = 41;
        private const int IdReviewLinksLabel  = 42;
        private const int IdReviewBaseLabel   = 43;
        private const int IdReviewJointsLabel = 44;
```

Add the joint type option arrays, next to `LinkNamePlaceholder` (line 80):

```csharp
        private static readonly string[] JointTypeLabels =
            { "Fixed", "Revolute", "Continuous", "Prismatic" };
        private static readonly UrdfJointType[] JointTypeOptions =
            { UrdfJointType.Fixed, UrdfJointType.Revolute, UrdfJointType.Continuous, UrdfJointType.Prismatic };
```

Add new PMP-native control fields, after the existing `_reviewJointsLabel` field declaration (line 110):

```csharp
        private PropertyManagerPageListbox _jointsList;
        private PropertyManagerPageTextbox _jointNameBox;
        private PropertyManagerPageCombobox _jointTypeCombo;
        private PropertyManagerPageLabel _jointAxisLabel;
        private PropertyManagerPageNumberbox _jointAxisXBox;
        private PropertyManagerPageNumberbox _jointAxisYBox;
        private PropertyManagerPageNumberbox _jointAxisZBox;
        private PropertyManagerPageLabel _jointLimitLabel;
        private PropertyManagerPageNumberbox _jointLimitLowerBox;
        private PropertyManagerPageNumberbox _jointLimitUpperBox;

        private int _selectedJointIndex = -1;
```

- [ ] **Step 4: Register the new step group in `BuildPage()`**

Replace (lines 198-200):

```csharp
            _stepGroups = new PropertyManagerPageGroup[StepCount];
            _stepGroups[StepLinks]  = BuildLinksGroup(grpOptions, leftEdge, visibleEnabled);
            _stepGroups[StepReview] = BuildReviewGroup(grpOptions, leftEdge, visibleEnabled);
```

with:

```csharp
            _stepGroups = new PropertyManagerPageGroup[StepCount];
            _stepGroups[StepLinks]  = BuildLinksGroup(grpOptions, leftEdge, visibleEnabled);
            _stepGroups[StepJoints] = BuildJointsGroup(grpOptions, leftEdge, visibleEnabled);
            _stepGroups[StepReview] = BuildReviewGroup(grpOptions, leftEdge, visibleEnabled);
```

- [ ] **Step 5: Add `BuildJointsGroup()`**

Add this new method right after `BuildLinksGroup()` (after line 296, before `AddFieldLabel`):

```csharp
        private PropertyManagerPageGroup BuildJointsGroup(int grpOptions, int leftEdge, int visibleEnabled)
        {
            var grp = (PropertyManagerPageGroup)_page.AddGroupBox(IdJointsGroup, "Joints", grpOptions);
            grp.AddControl2(IdJointsDescr,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "One joint per non-root link. Click a joint to edit its type, axis, and limits.",
                (short)leftEdge, visibleEnabled, "");

            _jointsList = (PropertyManagerPageListbox)grp.AddControl2(
                IdJointsList,
                (short)swPropertyManagerPageControlType_e.swControlType_Listbox,
                "Joints", (short)leftEdge, visibleEnabled, "Current robot joints");
            ((IPropertyManagerPageListbox)_jointsList).Height = 90;

            AddFieldLabel(grp, IdJointNameLabel, "Joint name", leftEdge, visibleEnabled);
            _jointNameBox = (PropertyManagerPageTextbox)grp.AddControl2(
                IdJointNameBox,
                (short)swPropertyManagerPageControlType_e.swControlType_Textbox,
                "", (short)leftEdge, visibleEnabled, "Renamable; defaults to parent_to_child");

            AddFieldLabel(grp, IdJointTypeLabel, "Type", leftEdge, visibleEnabled);
            _jointTypeCombo = (PropertyManagerPageCombobox)grp.AddControl2(
                IdJointTypeCombo,
                (short)swPropertyManagerPageControlType_e.swControlType_Combobox,
                "", (short)leftEdge, visibleEnabled, "Joint type");
            _jointTypeCombo.Height = 14;
            _jointTypeCombo.Style = (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;
            foreach (string label in JointTypeLabels) _jointTypeCombo.AddItems(label);

            _jointAxisLabel = (PropertyManagerPageLabel)grp.AddControl2(
                IdJointAxisLabel,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Axis (assembly frame X/Y/Z)", (short)leftEdge, visibleEnabled, "");
            _jointAxisXBox = NewAxisBox(grp, IdJointAxisXBox, leftEdge, visibleEnabled);
            _jointAxisYBox = NewAxisBox(grp, IdJointAxisYBox, leftEdge, visibleEnabled);
            _jointAxisZBox = NewAxisBox(grp, IdJointAxisZBox, leftEdge, visibleEnabled);

            _jointLimitLabel = (PropertyManagerPageLabel)grp.AddControl2(
                IdJointLimitLabel,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "", (short)leftEdge, visibleEnabled, "");
            _jointLimitLowerBox = (PropertyManagerPageNumberbox)grp.AddControl2(
                IdJointLimitLowerBox,
                (short)swPropertyManagerPageControlType_e.swControlType_Numberbox,
                "Lower", (short)leftEdge, visibleEnabled, "Lower motion limit");
            _jointLimitUpperBox = (PropertyManagerPageNumberbox)grp.AddControl2(
                IdJointLimitUpperBox,
                (short)swPropertyManagerPageControlType_e.swControlType_Numberbox,
                "Upper", (short)leftEdge, visibleEnabled, "Upper motion limit");

            RefreshJointsList();
            return grp;
        }

        private static PropertyManagerPageNumberbox NewAxisBox(
            PropertyManagerPageGroup grp, int id, int leftEdge, int visibleEnabled)
        {
            var box = (PropertyManagerPageNumberbox)grp.AddControl2(
                id, (short)swPropertyManagerPageControlType_e.swControlType_Numberbox,
                "", (short)leftEdge, visibleEnabled, "Axis component");
            box.SetRange2((int)swNumberboxUnitType_e.swNumberBox_UnitlessDouble,
                -1.0, 1.0, true, 0.0, 0.05, 0.05);
            return box;
        }
```

- [ ] **Step 6: Add the read-only list-population/selection plumbing**

Add these methods next to `RefreshReviewLabels()` (after line 515):

```csharp
        private void RefreshJointsList()
        {
            if (_jointsList == null) return;
            _jointsList.Clear();
            foreach (JointDef j in _liveDoc.Robot.Joints) _jointsList.AddItems(j.Name);
            if (_liveDoc.Robot.Joints.Count > 0)
            {
                _jointsList.CurrentSelection = 0;
                _selectedJointIndex = 0;
            }
            else
            {
                _selectedJointIndex = -1;
            }
        }
```

(Loading the selected joint's values into the detail form, and committing edits back, are added in Task 5 — for this checkpoint the list populates and tracks a selected index, but the detail fields stay static so there is nothing yet to load/commit.)

- [ ] **Step 7: Refresh the list on step entry**

In `ShowStep()`, replace (line 542):

```csharp
            if (_currentStep == StepReview) RefreshReviewLabels();
```

with:

```csharp
            if (_currentStep == StepJoints) RefreshJointsList();
            if (_currentStep == StepReview) RefreshReviewLabels();
```

- [ ] **Step 8: Update the Review step's joint-count label wording**

In `RefreshReviewLabels()` (line 513-514), replace:

```csharp
            if (_reviewJointsLabel != null)
                _reviewJointsLabel.Caption = "Joints (fixed): " + _liveDoc.Robot.Joints.Count;
```

with:

```csharp
            if (_reviewJointsLabel != null)
                _reviewJointsLabel.Caption = "Joints: " + _liveDoc.Robot.Joints.Count;
```

(No longer always "fixed" — Task 5 makes the type real.)

- [ ] **Step 9: Build the addin**

Build via the project's MSBuild path (memory `sw2gz-build-deploy`). Confirm no errors/warnings.

- [ ] **Step 10: Run the full test suite**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj`
Expected: unchanged pass count (this task touches no test-project code).

- [ ] **Step 11: Commit**

```bash
git add SW2GZ/UI/Pmp/Sw2gzCreateRobotPmp.cs
git commit -m "feat(robot): add Joints wizard step scaffold with joint list"
```

- [ ] **Step 12: Live checkpoint (manual, in SolidWorks)**

Deploy and open Create Robot on `FULL_ARM.SLDASM`. Build a link tree with at least 2 links (so at least one joint exists). Click ▶ to reach the new "Joints" step. Confirm: the step indicator reads "Step 2 of 3 — Joints", the joint list shows one row named `<parent>_to_<child>`, clicking Back returns to Links intact, clicking ▶ again reaches Review and its "Joints: N" count matches. **Do not proceed to Task 5 until this is confirmed working** — this is the smallest-possible-wiring-step checkpoint the project's history says to insist on before adding the editable detail form on top.

---

### Task 5: Joints step — editable detail form (type/axis/limit)

**Files:**
- Modify: `SW2GZ/UI/Pmp/Sw2gzCreateRobotPmp.cs`

- [ ] **Step 1: Add load/commit/visibility helpers**

Add these next to `RefreshJointsList()` (added in Task 4):

```csharp
        private void LoadJointIntoControls(JointDef j)
        {
            if (_jointNameBox == null) return;
            _jointNameBox.Text = j.Name;
            int typeIdx = Array.IndexOf(JointTypeOptions, j.Type);
            _jointTypeCombo.CurrentSelection = (short)(typeIdx >= 0 ? typeIdx : 0);
            _jointAxisXBox.Value = j.AxisX;
            _jointAxisYBox.Value = j.AxisY;
            _jointAxisZBox.Value = j.HasAxis ? j.AxisZ : 1.0;
            _jointLimitLowerBox.Value = j.Type == UrdfJointType.Revolute ? RadToDeg(j.LimitLower ?? 0.0) : (j.LimitLower ?? 0.0);
            _jointLimitUpperBox.Value = j.Type == UrdfJointType.Revolute ? RadToDeg(j.LimitUpper ?? 0.0) : (j.LimitUpper ?? 0.0);
            UpdateJointFieldVisibility(j.Type);
        }

        private void ClearJointControls()
        {
            if (_jointNameBox == null) return;
            _jointNameBox.Text = string.Empty;
            _jointTypeCombo.CurrentSelection = 0;
            _jointAxisXBox.Value = 0; _jointAxisYBox.Value = 0; _jointAxisZBox.Value = 1;
            _jointLimitLowerBox.Value = 0; _jointLimitUpperBox.Value = 0;
            UpdateJointFieldVisibility(UrdfJointType.Fixed);
        }

        // Axis is only meaningful for a moving joint; limits only for
        // Revolute/Prismatic (Continuous is unlimited by definition, Fixed
        // moves at all). IPropertyManagerPageControl.Visible is the same
        // generic control property already used to toggle whole step groups
        // (see docs/reference/solidworks-api.md) — this is its first use on
        // an individual control rather than a group.
        private void UpdateJointFieldVisibility(UrdfJointType type)
        {
            bool showAxis = type != UrdfJointType.Fixed;
            bool showLimit = type == UrdfJointType.Revolute || type == UrdfJointType.Prismatic;
            _jointAxisLabel.Visible = showAxis;
            _jointAxisXBox.Visible = showAxis;
            _jointAxisYBox.Visible = showAxis;
            _jointAxisZBox.Visible = showAxis;
            _jointLimitLabel.Visible = showLimit;
            _jointLimitLabel.Caption = type == UrdfJointType.Revolute ? "Limit (degrees)" : "Limit (meters)";
            _jointLimitLowerBox.Visible = showLimit;
            _jointLimitUpperBox.Visible = showLimit;
        }

        // Reads whatever is currently in the shared detail-form controls
        // back into the JointDef that was loaded into them. Must run BEFORE
        // switching the selected list row (single shared control set, one
        // JointDef "checked out" at a time) and before leaving the Joints
        // step entirely (ShowStep) or reviewing it (RefreshReviewLabels).
        private void CommitSelectedJointFromControls()
        {
            if (_selectedJointIndex < 0 || _selectedJointIndex >= _liveDoc.Robot.Joints.Count) return;
            JointDef j = _liveDoc.Robot.Joints[_selectedJointIndex];

            string newName = (_jointNameBox?.Text ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(newName)) j.Name = newName;

            short typeIdx = _jointTypeCombo?.CurrentSelection ?? 0;
            UrdfJointType type = JointTypeOptions[Math.Max(0, Math.Min(JointTypeOptions.Length - 1, typeIdx))];
            j.Type = type;

            if (type != UrdfJointType.Fixed)
            {
                j.SetAxis(new System.Numerics.Vector3(
                    (float)_jointAxisXBox.Value, (float)_jointAxisYBox.Value, (float)_jointAxisZBox.Value));
            }

            if (type == UrdfJointType.Revolute || type == UrdfJointType.Prismatic)
            {
                j.LimitLower = type == UrdfJointType.Revolute ? DegToRad(_jointLimitLowerBox.Value) : _jointLimitLowerBox.Value;
                j.LimitUpper = type == UrdfJointType.Revolute ? DegToRad(_jointLimitUpperBox.Value) : _jointLimitUpperBox.Value;
            }
        }

        private static double DegToRad(double deg) => deg * System.Math.PI / 180.0;
        private static double RadToDeg(double rad) => rad * 180.0 / System.Math.PI;
```

- [ ] **Step 2: Load the first joint on list refresh**

In `RefreshJointsList()` (added in Task 4), replace:

```csharp
        private void RefreshJointsList()
        {
            if (_jointsList == null) return;
            _jointsList.Clear();
            foreach (JointDef j in _liveDoc.Robot.Joints) _jointsList.AddItems(j.Name);
            if (_liveDoc.Robot.Joints.Count > 0)
            {
                _jointsList.CurrentSelection = 0;
                _selectedJointIndex = 0;
            }
            else
            {
                _selectedJointIndex = -1;
            }
        }
```

with:

```csharp
        private void RefreshJointsList()
        {
            if (_jointsList == null) return;
            _jointsList.Clear();
            foreach (JointDef j in _liveDoc.Robot.Joints) _jointsList.AddItems(j.Name);
            if (_liveDoc.Robot.Joints.Count > 0)
            {
                _jointsList.CurrentSelection = 0;
                _selectedJointIndex = 0;
                LoadJointIntoControls(_liveDoc.Robot.Joints[0]);
            }
            else
            {
                _selectedJointIndex = -1;
                ClearJointControls();
            }
        }
```

- [ ] **Step 3: Wire the listbox selection and type combobox handlers**

Replace the empty stub (line 620):

```csharp
        void IPropertyManagerPage2Handler9.OnListboxSelectionChanged(int Id, int Item) { }
```

with:

```csharp
        void IPropertyManagerPage2Handler9.OnListboxSelectionChanged(int Id, int Item)
        {
            if (Id != IdJointsList) return;
            CommitSelectedJointFromControls();
            _selectedJointIndex = Item;
            if (Item >= 0 && Item < _liveDoc.Robot.Joints.Count) LoadJointIntoControls(_liveDoc.Robot.Joints[Item]);
        }
```

Replace the empty stub (line 619):

```csharp
        void IPropertyManagerPage2Handler9.OnComboboxSelectionChanged(int Id, int Item) { }
```

with:

```csharp
        void IPropertyManagerPage2Handler9.OnComboboxSelectionChanged(int Id, int Item)
        {
            if (Id != IdJointTypeCombo) return;
            UrdfJointType type = JointTypeOptions[Math.Max(0, Math.Min(JointTypeOptions.Length - 1, Item))];
            // Suggest a sane default axis the first time a joint leaves
            // Fixed, instead of leaving it at (0,0,0) — a zero-vector axis
            // is meaningless in URDF.
            if (type != UrdfJointType.Fixed && _jointAxisXBox.Value == 0 && _jointAxisYBox.Value == 0 && _jointAxisZBox.Value == 0)
                _jointAxisZBox.Value = 1;
            UpdateJointFieldVisibility(type);
        }
```

- [ ] **Step 4: Commit the selected joint when leaving the Joints step**

In `ShowStep()`, at the very top (right after the `step` clamping, before `_currentStep = step;`), add:

```csharp
            if (_currentStep == StepJoints && step != StepJoints) CommitSelectedJointFromControls();
```

So the method now reads (showing the relevant top portion):

```csharp
        private void ShowStep(int step)
        {
            if (step < 0) step = 0;
            if (step > StepCount - 1) step = StepCount - 1;
            if (_currentStep == StepJoints && step != StepJoints) CommitSelectedJointFromControls();

            _currentStep = step;
            for (int i = 0; i < StepCount; i++)
            {
                try { _stepGroups[i].Visible = (i == _currentStep); }
                catch (Exception ex) { logger.Error("Robot ShowStep group[" + i + "].Visible failed", ex); }
            }
            // ... unchanged from here down ...
```

- [ ] **Step 5: Commit the selected joint before displaying Review**

In `RefreshReviewLabels()`, add a commit at the top:

```csharp
        private void RefreshReviewLabels()
        {
            CommitSelectedJointFromControls();
            if (_reviewLinksLabel != null)
                _reviewLinksLabel.Caption = "Links: " + _liveDoc.Robot.Links.Count;
            if (_reviewBaseLabel != null)
                _reviewBaseLabel.Caption = "Base link: " +
                    (_liveDoc.Robot.Links.Count > 0 ? _liveDoc.Robot.Links[0].Name : "(none)");
            if (_reviewJointsLabel != null)
                _reviewJointsLabel.Caption = "Joints: " + _liveDoc.Robot.Joints.Count;
        }
```

- [ ] **Step 6: Build the addin**

Build via the project's MSBuild path (memory `sw2gz-build-deploy`). Confirm no errors/warnings.

- [ ] **Step 7: Run the full test suite**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj`
Expected: unchanged pass count (this task touches no test-project code).

- [ ] **Step 8: Commit**

```bash
git add SW2GZ/UI/Pmp/Sw2gzCreateRobotPmp.cs
git commit -m "feat(robot): Joints step detail form edits type/axis/limit per joint"
```

---

### Task 6: Manual live verification in SolidWorks

**Files:** none (verification only).

This class of change has broken live in SolidWorks twice before while every automated gate passed (memory `robot-mode-dev`) — green tests and a clean build are necessary but not sufficient. This task is executed by the user, not the agent (no tool in this environment can drive SolidWorks' PMP UI).

- [ ] **Step 1: Deploy and open Create Robot on `FULL_ARM.SLDASM`.**

- [ ] **Step 2: Build a link tree** with at least 3 links so there's a mix of joints to exercise (e.g. `base_link` → `arm_link` → `wrist_link`).

- [ ] **Step 3: On the Joints step**, select the `base_link_to_arm_link` row, set Type = Revolute, set Axis to `0, 0, 1`, set Limit lower/upper to `-90` / `90` (degrees). Select the `arm_link_to_wrist_link` row, set Type = Prismatic, Axis `1, 0, 0`, Limit `-0.1` / `0.1` (meters). Confirm the Axis/Limit fields show/hide correctly as Type changes, and the Limit label switches between "degrees" and "meters".

- [ ] **Step 4: Go Back to Links**, add one more link, then forward to Joints again. Confirm the two joints you already edited **still show your Revolute/Prismatic settings** (this is the merge-preserve behavior from Task 1/3 — the regression this whole design guards against).

- [ ] **Step 5: Export.** Open the resulting `<pkg>.urdf.xacro` and confirm:
  - the Revolute joint has `type="revolute"`, a `<axis xyz="0 0 1"/>` (or its rotated equivalent if `arm_link` isn't identity-oriented in the assembly), and `<limit lower="-1.5708" upper="1.5708"/>` (radians, converted from the 90° you entered)
  - the Prismatic joint has `type="prismatic"`, `<axis xyz="1 0 0"/>` (rotated equivalent), and `<limit lower="-0.1" upper="0.1"/>` (meters, unconverted)
  - any joint you left untouched still has `type="fixed"` and no `<axis>`/`<limit>`

- [ ] **Step 6: Load the URDF in RViz/`jsp_gui`** (per the existing WSL ROS test loop — memory `wsl-ros-test-env`) and confirm the Revolute/Prismatic joints actually show up as movable sliders, not fixed.

- [ ] **Step 7: Report back.** If anything breaks, capture the specific symptom (which step, which control, what was expected vs. what happened) — per memory `robot-mode-dev`, a vague "broke" report without a captured symptom is what led to the last full revert of joint-related work; a specific symptom is what lets a fix actually land instead of another full rollback.

---

## Self-Review Notes

**Spec coverage:** every design decision in the spec maps to a task — auto-derived origin/pose (untouched, Task 2 doesn't touch `jointOrigins`/`jointRpys` computation), 1:1 joint list (Task 1's reconciler), assembly-frame axis input + child-local export conversion (Task 2 + Task 5), degrees-for-Revolute/meters-for-Prismatic limits (Task 5's `DegToRad`/`RadToDeg`), 4-type dropdown (Task 4's `JointTypeOptions`), merge-preserve `RebuildJoints()` (Task 1 + Task 3), new Joints step in the wizard (Task 4 + Task 5), live-test-before-more-changes sequencing (Task 4/Step 12 checkpoint before Task 5). Phase 2 (mate suggestion + yellow highlight) is explicitly out of scope, not represented here.

**Type consistency:** `JointDefReconciler.Reconcile` signature (`IReadOnlyList<JointDef>`, `IReadOnlyList<LinkDef>` → `List<JointDef>`) is used identically in its test file and in `RebuildJoints()`. `JointTypeOptions`/`JointTypeLabels` arrays declared once in Task 4, consumed by name in Task 5 — same names throughout. `Sw2gzRobotExporter.WriteUrdf`'s new parameters (`jointByChild`, `jointAxesLocal`) are declared in the signature and used consistently in the call site, both edited within Task 2.
