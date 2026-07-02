# Robot joint/link relative pose + multi-mesh links Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the robot exporter's joint origin math parent-relative (it's
currently always root-relative), detect the tree's root by structure instead
of list position, and honor every mesh component a link has been assigned
(currently only the first is used) — for both the mesh geometry and the mass
properties.

**Architecture:** `Sw2gzRobotExporter.Export` moves from a single pass that
assumes list position `[0]` is the root and every link's parent is that
root, to a two-pass approach: pass 1 reads every link's own reference pose
once (order-independent); pass 2 uses that lookup to compute each link's
joint relative to its *own* `ParentName`, and unions/combines every
assigned mesh component and mass property into that link's reference
frame. `InertialAggregator` gains a `Matrix3`-parameterized twin of its
existing `Quaternion`-based `Combine` overloads (shared core, no new
coordinate-conversion code) so the exporter never needs to leave
`Matrix3`/`Vector3` space. Small UI addition in the wizard surfaces which
assigned component is "primary" (defines the link's frame), reusing
`LinkTreeView`'s already-wired-but-unused tooltip support.

**Tech Stack:** C# / .NET Framework (add-in, `SW2GZ.csproj`) + .NET 8 (test
project, `SW2GZ.Writers.Test.csproj`, xUnit). No new dependencies, no new
files added to any `.csproj` (Test project is SDK-style — new test files
under the project directory are auto-included).

**Reference:** [`docs/superpowers/specs/2026-07-02-robot-joint-relative-pose-design.md`](../specs/2026-07-02-robot-joint-relative-pose-design.md)

---

### Task 1: `InertialAggregator` — Matrix3-parameterized `Combine` overloads

**Files:**
- Modify: `SW2GZ/Build/InertialAggregator.cs`
- Test (new file, auto-included): `Test/Build/InertialAggregatorMatrixTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Test/Build/InertialAggregatorMatrixTests.cs`:

```csharp
using System.Collections.Generic;
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Build.Tests
{
    public class InertialAggregatorMatrixTests
    {
        private static Matrix3 RotZ(double radians)
        {
            double c = System.Math.Cos(radians), s = System.Math.Sin(radians);
            return new Matrix3(c, -s, 0, s, c, 0, 0, 0, 1);
        }

        [Fact]
        public void Combine_Matrix3Overload_MatchesQuaternionOverload_IdentityRotation()
        {
            var p = new MassProps(1.0, Vector3.Zero, Matrix3.Identity);
            var posA = new Vector3(-1, 0, 0);
            var posB = new Vector3(1, 0, 0);

            var quaternionParts = new List<(MassProps, Pose)>
            {
                (p, new Pose(posA, Quaternion.Identity)),
                (p, new Pose(posB, Quaternion.Identity)),
            };
            var matrixParts = new List<(MassProps, Matrix3, Vector3)>
            {
                (p, Matrix3.Identity, posA),
                (p, Matrix3.Identity, posB),
            };

            MassProps viaQuaternion = InertialAggregator.Combine(quaternionParts);
            MassProps viaMatrix3 = InertialAggregator.Combine(matrixParts);

            Assert.Equal(2.0, viaMatrix3.Mass);
            Assert.Equal(viaQuaternion.Mass, viaMatrix3.Mass);
            Assert.Equal(viaQuaternion.ComLocal.X, viaMatrix3.ComLocal.X, 9);
            Assert.Equal(viaQuaternion.ComLocal.Y, viaMatrix3.ComLocal.Y, 9);
            Assert.Equal(viaQuaternion.ComLocal.Z, viaMatrix3.ComLocal.Z, 9);
            Assert.Equal(viaQuaternion.InertiaAtComLocal.M11, viaMatrix3.InertiaAtComLocal.M11, 9);
            Assert.Equal(viaQuaternion.InertiaAtComLocal.M22, viaMatrix3.InertiaAtComLocal.M22, 9);
            Assert.Equal(viaQuaternion.InertiaAtComLocal.M33, viaMatrix3.InertiaAtComLocal.M33, 9);
        }

        [Fact]
        public void Combine_Matrix3Overload_MatchesQuaternionOverload_NonIdentityRotation()
        {
            var inertia = new Matrix3(1.5, 0, 0, 0, 2.0, 0, 0, 0, 2.5);
            var qA = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.4f);
            var qB = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.7f);
            var posA = new Vector3(0.3f, 0.2f, -0.1f);
            var posB = new Vector3(-0.2f, 0.5f, 0.4f);

            var quaternionParts = new List<(MassProps, Pose)>
            {
                (new MassProps(1.0, Vector3.Zero, inertia), new Pose(posA, qA)),
                (new MassProps(2.0, Vector3.Zero, inertia), new Pose(posB, qB)),
            };
            var matrixParts = new List<(MassProps, Matrix3, Vector3)>
            {
                (new MassProps(1.0, Vector3.Zero, inertia), Matrix3.FromQuaternion(qA), posA),
                (new MassProps(2.0, Vector3.Zero, inertia), Matrix3.FromQuaternion(qB), posB),
            };

            MassProps viaQuaternion = InertialAggregator.Combine(quaternionParts);
            MassProps viaMatrix3 = InertialAggregator.Combine(matrixParts);

            Assert.Equal(viaQuaternion.Mass, viaMatrix3.Mass, 9);
            Assert.Equal(viaQuaternion.ComLocal.X, viaMatrix3.ComLocal.X, 6);
            Assert.Equal(viaQuaternion.ComLocal.Y, viaMatrix3.ComLocal.Y, 6);
            Assert.Equal(viaQuaternion.ComLocal.Z, viaMatrix3.ComLocal.Z, 6);
            Assert.Equal(viaQuaternion.InertiaAtComLocal.M11, viaMatrix3.InertiaAtComLocal.M11, 6);
            Assert.Equal(viaQuaternion.InertiaAtComLocal.M22, viaMatrix3.InertiaAtComLocal.M22, 6);
            Assert.Equal(viaQuaternion.InertiaAtComLocal.M33, viaMatrix3.InertiaAtComLocal.M33, 6);
            Assert.Equal(viaQuaternion.InertiaAtComLocal.M12, viaMatrix3.InertiaAtComLocal.M12, 6);
            Assert.Equal(viaQuaternion.InertiaAtComLocal.M13, viaMatrix3.InertiaAtComLocal.M13, 6);
            Assert.Equal(viaQuaternion.InertiaAtComLocal.M23, viaMatrix3.InertiaAtComLocal.M23, 6);
        }

        [Fact]
        public void CombineWithAnchor_Matrix3Overload_PartAtAnchor_RebasesBackToPartLocal()
        {
            // Mirrors InertialAggregatorTests.CombineWithLinkAnchor_SinglePartAtAnchor_RebasesBackToPartLocal
            // for the Matrix3 overload: when a part's own frame equals the
            // rebase anchor, the two transforms must cancel exactly,
            // regardless of what that shared rotation actually is.
            var partLocalCom = new Vector3(0f, 0f, 0.15f);
            var partInertia = new Matrix3(0.003, 0, 0, 0, 0.003, 0, 0, 0, 0.0001);
            var p = new MassProps(0.5, partLocalCom, partInertia);

            Matrix3 anchorR = RotZ(0.5);
            Vector3 anchorT = new Vector3(1.0f, -2.0f, 0.4f);

            var parts = new List<(MassProps, Matrix3, Vector3)> { (p, anchorR, anchorT) };
            MassProps rebased = InertialAggregator.Combine(parts, anchorR, anchorT);

            Assert.Equal(0.5, rebased.Mass, 6);
            Assert.Equal(partLocalCom.X, rebased.ComLocal.X, 5);
            Assert.Equal(partLocalCom.Y, rebased.ComLocal.Y, 5);
            Assert.Equal(partLocalCom.Z, rebased.ComLocal.Z, 5);
            Assert.Equal(partInertia.M11, rebased.InertiaAtComLocal.M11, 5);
            Assert.Equal(partInertia.M22, rebased.InertiaAtComLocal.M22, 5);
            Assert.Equal(partInertia.M33, rebased.InertiaAtComLocal.M33, 5);
        }

        [Fact]
        public void Combine_Matrix3Overload_Null_ReturnsIdentity()
        {
            var result = InertialAggregator.Combine((List<(MassProps, Matrix3, Vector3)>)null);
            Assert.Equal(0.0, result.Mass);
        }

        [Fact]
        public void Combine_Matrix3Overload_EmptyList_ReturnsIdentity()
        {
            var result = InertialAggregator.Combine(new List<(MassProps, Matrix3, Vector3)>());
            Assert.Equal(0.0, result.Mass);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj -c Release --filter "FullyQualifiedName~InertialAggregatorMatrixTests"`
Expected: build ERROR — `InertialAggregator.Combine` has no overload taking `(MassProps, Matrix3, Vector3)` tuples or `(parts, Matrix3, Vector3)`.

- [ ] **Step 3: Implement the Matrix3 overloads**

Replace the full contents of `SW2GZ/Build/InertialAggregator.cs` with:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SW2GZ.Math;

namespace SW2GZ.Build
{
    public static class InertialAggregator
    {
        // Combine N parts at given poses into a single rigid-body MassProps at the assembly origin.
        // Steps:
        //   1) total mass
        //   2) mass-weighted COM, with each part's local COM rotated into the assembly frame
        //      via R_f before translation
        //   3) for each part, transform its inertia tensor from part frame to assembly frame
        //      as I_a = R_f · I_part · R_fᵀ, then translate to the combined COM via the
        //      parallel-axis theorem, and sum.
        // If frame.Rotation is Quaternion.Identity, R_f is Identity and the result is
        // byte-equivalent to the pre-P3 translation-only behavior.
        //
        // The returned MassProps reports COM and inertia in the ASSEMBLY frame
        // (rotation + translation). URDF's <inertial> block wants both in the
        // link-local frame — use the (parts, linkAnchor) overload below for that.
        public static MassProps Combine(IReadOnlyList<(MassProps Props, Pose Frame)> parts)
        {
            if (parts == null) return new MassProps(0, Vector3.Zero, Matrix3.Identity);
            var matrixParts = parts
                .Select(p => (p.Props, Matrix3.FromQuaternion(p.Frame.Rotation), p.Frame.Position))
                .ToList();
            return CombineCore(matrixParts);
        }

        // Matrix3-parameterized twin of the overload above. Same algorithm,
        // same result for an equivalent rotation — exists so callers that
        // already work entirely in Matrix3/Vector3 (e.g. Sw2gzRobotExporter,
        // which reads SolidWorks component poses as Matrix3 directly) never
        // need to construct a Quaternion just to call in here. Deliberately
        // NOT implemented by converting to Quaternion and delegating to the
        // overload above — a Matrix3-to-Quaternion conversion is new
        // coordinate-conversion code, exactly the category that has already
        // produced two real bugs in this codebase (the Transform2.ArrayData
        // column-major bug, the mate-classification bug). Both overloads
        // instead share CombineCore; only the Quaternion overload ever
        // converts (Quaternion -> Matrix3, an already-proven, already-used
        // direction), never the reverse.
        public static MassProps Combine(IReadOnlyList<(MassProps Props, Matrix3 Rotation, Vector3 Position)> parts)
        {
            if (parts == null) return new MassProps(0, Vector3.Zero, Matrix3.Identity);
            return CombineCore(parts);
        }

        private static MassProps CombineCore(IReadOnlyList<(MassProps Props, Matrix3 R, Vector3 Position)> parts)
        {
            if (parts.Count == 0)
                return new MassProps(0, Vector3.Zero, Matrix3.Identity);

            double totalMass = parts.Sum(p => p.Props.Mass);
            if (totalMass <= 0)
                return new MassProps(0, Vector3.Zero, Matrix3.Identity);

            // Per-part rotated COM offset in assembly frame (double precision),
            // i.e. (pos + R * p.ComLocal). Reused for the parallel-axis d.
            var partComsX = new double[parts.Count];
            var partComsY = new double[parts.Count];
            var partComsZ = new double[parts.Count];

            double comX = 0.0, comY = 0.0, comZ = 0.0;
            for (int i = 0; i < parts.Count; i++)
            {
                var (p, R_f, pos) = parts[i];
                var (rx, ry, rz) = R_f.Mul((double)p.ComLocal.X, p.ComLocal.Y, p.ComLocal.Z);
                double pcx = pos.X + rx;
                double pcy = pos.Y + ry;
                double pcz = pos.Z + rz;
                partComsX[i] = pcx; partComsY[i] = pcy; partComsZ[i] = pcz;
                double w = p.Mass / totalMass;
                comX += w * pcx; comY += w * pcy; comZ += w * pcz;
            }
            var com = new Vector3((float)comX, (float)comY, (float)comZ);

            // Parallel-axis: I_parent = sum_i ( R_i I_i R_iᵀ + m_i * (||d_i||^2 * I - d_i * d_i^T) )
            var I = new double[3, 3];
            for (int i = 0; i < parts.Count; i++)
            {
                var (p, R_f, _) = parts[i];
                var I_rot = R_f * p.InertiaAtComLocal * R_f.Transpose();

                double dx = partComsX[i] - comX;
                double dy = partComsY[i] - comY;
                double dz = partComsZ[i] - comZ;
                double d2 = dx * dx + dy * dy + dz * dz;

                I[0, 0] += I_rot.M11 + p.Mass * (d2 - dx * dx);
                I[0, 1] += I_rot.M12 + p.Mass * (    - dx * dy);
                I[0, 2] += I_rot.M13 + p.Mass * (    - dx * dz);
                I[1, 0] += I_rot.M21 + p.Mass * (    - dy * dx);
                I[1, 1] += I_rot.M22 + p.Mass * (d2 - dy * dy);
                I[1, 2] += I_rot.M23 + p.Mass * (    - dy * dz);
                I[2, 0] += I_rot.M31 + p.Mass * (    - dz * dx);
                I[2, 1] += I_rot.M32 + p.Mass * (    - dz * dy);
                I[2, 2] += I_rot.M33 + p.Mass * (d2 - dz * dz);
            }

            return new MassProps(totalMass, com,
                new Matrix3(I[0,0], I[0,1], I[0,2],
                            I[1,0], I[1,1], I[1,2],
                            I[2,0], I[2,1], I[2,2]));
        }

        // Combine + rebase into the link-local frame defined by `linkAnchor`
        // (assembly-frame pose of the link's anchor part). URDF's <inertial>
        // wants COM and inertia expressed in the link's own frame; the base
        // Combine() returns both in the assembly frame, which is wrong as soon
        // as the link anchor is not at the assembly origin.
        //
        // Rebase math (R = R_linkAnchor):
        //   COM_link  = R^-1 · (COM_assembly − linkAnchor.Position)
        //   I_link    = R^-1 · I_assembly · R
        // Mass is invariant.
        //
        // When linkAnchor == Pose.Identity, R = I and the result equals
        // Combine(parts) byte-for-byte → existing goldens stay green.
        public static MassProps Combine(
            IReadOnlyList<(MassProps Props, Pose Frame)> parts,
            Pose linkAnchor)
        {
            MassProps assemblyFrame = Combine(parts);
            if (linkAnchor == null || linkAnchor == Pose.Identity) return assemblyFrame;
            if (assemblyFrame.Mass <= 0) return assemblyFrame;

            Matrix3 R = Matrix3.FromQuaternion(linkAnchor.Rotation);
            return RebaseCore(assemblyFrame, R, linkAnchor.Position);
        }

        // Matrix3-parameterized twin of the rebase overload above — see the
        // Combine(parts) overload for why this exists instead of routing
        // through Quaternion.
        public static MassProps Combine(
            IReadOnlyList<(MassProps Props, Matrix3 Rotation, Vector3 Position)> parts,
            Matrix3 anchorR, Vector3 anchorT)
        {
            MassProps assemblyFrame = Combine(parts);
            if (assemblyFrame.Mass <= 0) return assemblyFrame;
            return RebaseCore(assemblyFrame, anchorR, anchorT);
        }

        private static MassProps RebaseCore(MassProps assemblyFrame, Matrix3 R, Vector3 anchorPosition)
        {
            // R = link anchor rotation. We need a vector expressed in the
            // LINK frame, so we apply Rᵀ (R^-1 for an orthonormal rotation).
            Matrix3 Rinv = R.Transpose();

            double dx = assemblyFrame.ComLocal.X - anchorPosition.X;
            double dy = assemblyFrame.ComLocal.Y - anchorPosition.Y;
            double dz = assemblyFrame.ComLocal.Z - anchorPosition.Z;
            var (lx, ly, lz) = Rinv.Mul(dx, dy, dz);
            var comLink = new Vector3((float)lx, (float)ly, (float)lz);

            // I_link = R^-1 · I_assembly · R (same tensor at the same point,
            // re-expressed in the rotated basis).
            Matrix3 Ilink = Rinv * assemblyFrame.InertiaAtComLocal * R;

            return new MassProps(assemblyFrame.Mass, comLink, Ilink);
        }
    }
}
```

- [ ] **Step 4: Run the new tests and the full existing InertialAggregator suite**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj -c Release --filter "FullyQualifiedName~InertialAggregator"`
Expected: PASS — all of `InertialAggregatorTests`, `InertialAggregatorRotationTests` (both existing, must stay green — they exercise the `Quaternion` overloads which now call `CombineCore`/`RebaseCore` but must produce byte-identical results), and the new `InertialAggregatorMatrixTests`.

- [ ] **Step 5: Commit**

```bash
git add SW2GZ/Build/InertialAggregator.cs Test/Build/InertialAggregatorMatrixTests.cs
git commit -m "feat(robot): Matrix3-parameterized InertialAggregator.Combine overloads"
```

---

### Task 2: `Sw2gzRobotExporter` — parent-relative joint origin + tree-based root detection

Mesh and mass logic are **not** touched in this task (still single-component,
exactly as today) — this is deliberately the smallest possible first slice
of the exporter change, isolating the joint-origin/root-detection fix so it
can be verified on its own before Tasks 3/4 layer multi-mesh union and
multi-part mass on top. (See `agent-progress/progress.md` — this exact
class of change has broken live in SolidWorks before despite green tests;
smallest-step-first is the standing lesson from that.)

**Files:**
- Modify: `SW2GZ/URDFExport/Sw2gzRobotExporter.cs`
- Test: `Test/URDFExport/Sw2gzRobotExporterTests.cs`

- [ ] **Step 1: Write the failing tests**

Add these two `[Fact]` methods to the `Sw2gzRobotExporterTests` class in
`Test/URDFExport/Sw2gzRobotExporterTests.cs` (add them after
`Export_JointRotationIsRealRelativeRotation_NotIdentity`):

```csharp
        [Fact]
        public void Export_GrandchildJointOrigin_IsRelativeToItsOwnParent_NotRoot()
        {
            var links = new List<LinkDef>
            {
                new LinkDef { Name = "base_link", ComponentIds = { "base-1@asm" }, ParentName = "" },
                new LinkDef { Name = "mid_link",  ComponentIds = { "mid-1@asm" },  ParentName = "base_link" },
                new LinkDef { Name = "leaf_link", ComponentIds = { "leaf-1@asm" }, ParentName = "mid_link" },
            };
            var cfg = new Sw2gzExportConfig
            {
                Mode = SW2GZ.Ros2.ExportMode.RobotPackage,
                PackageName = "my_robot",
                RobotLinks = links,
            };
            var poses = new Dictionary<string, (Matrix3, Vector3)>
            {
                ["base-1@asm"] = (Matrix3.Identity, new Vector3(0, 0, 0)),
                ["mid-1@asm"]  = (Matrix3.Identity, new Vector3(1, 0, 0)),
                ["leaf-1@asm"] = (Matrix3.Identity, new Vector3(1, 5, 0)),
            };

            Sw2gzRobotExporter.Export(
                new FakeTess(), new FakeMassProps(), new FakePoses(poses), cfg, _dir, Matrix3.Identity);

            XElement root = XElement.Load(Path.Combine(_dir, "my_robot_ws", "src", "my_robot", "urdf", "my_robot.urdf.xacro"));
            XElement leafJoint = root.Elements("joint").Single(j => (string)j.Attribute("name") == "mid_link_to_leaf_link");
            Assert.Equal("mid_link", (string)leafJoint.Element("parent").Attribute("link"));

            // leaf is at (1,5,0), its real parent mid_link is at (1,0,0) — the
            // relative offset is (0,5,0). If this were still computed relative
            // to ROOT (0,0,0) instead of mid_link, it would wrongly read (1,5,0).
            string[] xyz = ((string)leafJoint.Element("origin").Attribute("xyz")).Split(' ');
            Assert.Equal(0.0, double.Parse(xyz[0]), 3);
            Assert.Equal(5.0, double.Parse(xyz[1]), 3);
            Assert.Equal(0.0, double.Parse(xyz[2]), 3);
        }

        [Fact]
        public void Export_RootDetectedByTreeStructure_NotListPosition()
        {
            // Simulates a post-reroot doc: mid_link is now the actual root
            // (ParentName == ""), but sits at list position [1], not [0] —
            // exactly what LinkTreeView's "Set as base link" produces (it
            // edits ParentName pointers, never reorders Robot.Links).
            var links = new List<LinkDef>
            {
                new LinkDef { Name = "leaf_link", ComponentIds = { "leaf-1@asm" }, ParentName = "mid_link" },
                new LinkDef { Name = "mid_link",  ComponentIds = { "mid-1@asm" },  ParentName = "" },
            };
            var cfg = new Sw2gzExportConfig
            {
                Mode = SW2GZ.Ros2.ExportMode.RobotPackage,
                PackageName = "my_robot",
                RobotLinks = links,
            };
            var poses = new Dictionary<string, (Matrix3, Vector3)>
            {
                ["mid-1@asm"]  = (Matrix3.Identity, new Vector3(5, 0, 0)),
                ["leaf-1@asm"] = (Matrix3.Identity, new Vector3(5, 2, 0)),
            };

            Sw2gzRobotExporter.Export(
                new FakeTess(), new FakeMassProps(), new FakePoses(poses), cfg, _dir, Matrix3.Identity);

            XElement root = XElement.Load(Path.Combine(_dir, "my_robot_ws", "src", "my_robot", "urdf", "my_robot.urdf.xacro"));
            XElement joint = root.Elements("joint").Single();
            Assert.Equal("mid_link", (string)joint.Element("parent").Attribute("link"));
            Assert.Equal("leaf_link", (string)joint.Element("child").Attribute("link"));

            // leaf (5,2,0) relative to its real parent mid_link (5,0,0) = (0,2,0).
            // If root were still wrongly detected as leaf_link (list position
            // [0]), this would never be computed at all (falls back to 0 0 0).
            string[] xyz = ((string)joint.Element("origin").Attribute("xyz")).Split(' ');
            Assert.Equal(0.0, double.Parse(xyz[0]), 3);
            Assert.Equal(2.0, double.Parse(xyz[1]), 3);
            Assert.Equal(0.0, double.Parse(xyz[2]), 3);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj -c Release --filter "FullyQualifiedName~Export_GrandchildJointOrigin_IsRelativeToItsOwnParent_NotRoot|FullyQualifiedName~Export_RootDetectedByTreeStructure_NotListPosition"`
Expected: FAIL — `Export_GrandchildJointOrigin_IsRelativeToItsOwnParent_NotRoot` fails because the current code computes `leaf_link`'s origin relative to `base_link` (root), giving `xyz="1 5 0"` not `"0 5 0"`. `Export_RootDetectedByTreeStructure_NotListPosition` fails because current code treats `leaf_link` (list position `[0]`) as the base, so `mid_link` never gets a computed origin and the single joint falls back to `xyz="0 0 0"` instead of `"0 2 0"`.

- [ ] **Step 3: Implement the parent-relative fix + tree-based root detection**

In `SW2GZ/URDFExport/Sw2gzRobotExporter.cs`, replace the `Export` method
body (everything from `List<LinkDef> links = config.RobotLinks` down to
just before the closing brace, i.e. lines 69–155 of the current file) with:

```csharp
            List<LinkDef> links = config.RobotLinks ?? new List<LinkDef>();
            if (links.Count == 0)
                throw new SW2GZ.Exceptions.Sw2gzExportException(
                    "No links defined — open Create Robot and add at least one link.");

            string pkg = PackageNameSanitizer.Sanitize(config.PackageName).Value;
            string workspace = Path.Combine(outputDir, pkg + "_ws");
            string root = Path.Combine(workspace, "src", pkg);
            string urdfDir = Path.Combine(root, "urdf");
            string meshesDir = Path.Combine(root, "meshes");
            Directory.CreateDirectory(urdfDir);
            Directory.CreateDirectory(meshesDir);

            var issues = new List<ValidationIssue>();
            var meshFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            var masses = new Dictionary<string, MassProps>(StringComparer.Ordinal);
            var jointOrigins = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            var jointRpys = new Dictionary<string, (double, double, double)>(StringComparer.Ordinal);

            // Root = whichever link the TREE says has no parent, not
            // links[0]. "Set as base link" (re-root, in LinkTreeView) edits
            // ParentName pointers but never reorders Robot.Links, so list
            // position [0] can silently stop being the real root.
            LinkDef baseLink = LinkHierarchy.Roots(links).FirstOrDefault() ?? links[0];
            string baseLinkName = baseLink.Name;

            // Pass 1: every link's own reference pose (its first assigned
            // component), read once up front. A child can be positioned
            // before its parent in this list after a drag-drop reparent
            // (reparenting only edits ParentName, never reorders Links), so
            // pass 2 needs random access to ANY link's pose by name, not
            // list order.
            var linkPoses = new Dictionary<string, (Matrix3 R, Vector3 T)>(StringComparer.Ordinal);
            foreach (LinkDef link in links)
            {
                string refComp = link.ComponentIds?.FirstOrDefault();
                linkPoses[link.Name] = TryGetPose(poses, refComp, issues, link.Name);
            }

            foreach (LinkDef link in links)
            {
                string compName = link.ComponentIds?.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(compName)) continue;

                (Matrix3 linkR, Vector3 linkT) = linkPoses[link.Name];

                MeshData meshWorld;
                try
                {
                    meshWorld = tess.Tessellate(compName, TessellationLod.Fine);
                }
                catch (Exception ex)
                {
                    issues.Add(new ValidationIssue(IssueSeverity.Warning, "ROBOT.MESH",
                        "Link '" + link.Name + "' — could not tessellate '" + compName + "': " + ex.Message,
                        "Sw2gzRobotExporter"));
                    meshWorld = null;
                }

                if (meshWorld != null)
                {
                    MeshData meshLocal = link.Name == baseLinkName
                        ? Translate(meshWorld, -linkT)
                        : UnbakeToLocal(meshWorld, linkR, linkT);

                    string daeFile = link.Name + ".dae";
                    DaeWriter.Write(meshLocal, Path.Combine(meshesDir, daeFile), withNormals: true);
                    meshFiles[link.Name] = daeFile;
                }

                // Joint origin relative to THIS link's own declared parent —
                // not always the root. Same formula as before; only the
                // source of "parent pose" changed.
                if (!string.IsNullOrEmpty(link.ParentName) &&
                    linkPoses.TryGetValue(link.ParentName, out (Matrix3 R, Vector3 T) parentPose))
                {
                    Matrix3 rJoint = parentPose.R.Transpose() * linkR;
                    Vector3 tJoint = parentPose.R.Transpose().Mul(linkT - parentPose.T);
                    jointOrigins[link.Name] = tJoint;
                    jointRpys[link.Name] = rJoint.ToRpy();
                }

                try
                {
                    masses[link.Name] = massProps.Get(compName);
                }
                catch (Exception ex)
                {
                    // ponytail: no SW material on this part → placeholder mass/
                    // inertia. Physical accuracy is out of scope for this
                    // validating cut (no actuation/physics sim runs on the
                    // export yet); revisit once Robot mode gets a real
                    // inertia pipeline.
                    masses[link.Name] = new MassProps(0.1, Vector3.Zero, Matrix3.Identity);
                    issues.Add(new ValidationIssue(IssueSeverity.Warning, "ROBOT.MASS",
                        "Link '" + link.Name + "' — no material on '" + compName + "', using placeholder mass: " + ex.Message,
                        "Sw2gzRobotExporter"));
                }
            }

            string urdfPath = Path.Combine(urdfDir, pkg + ".urdf.xacro");
            WriteUrdf(urdfPath, pkg, baseLinkName, links, meshFiles, masses, jointOrigins, jointRpys, config.EmitWorldLink, swToRos);

            return new ValidationReport(issues);
        }
```

Then update the `WriteUrdf` method signature and body — replace:

```csharp
        private static void WriteUrdf(
            string path, string pkg, List<LinkDef> links,
            Dictionary<string, string> meshFiles, Dictionary<string, MassProps> masses,
            Dictionary<string, Vector3> jointOrigins, Dictionary<string, (double, double, double)> jointRpys,
            bool emitWorldLink, Matrix3 swToRos)
        {
            var uw = new URDFWriter(path);
            System.Xml.XmlWriter w = uw.writer;
            w.WriteStartDocument();
            w.WriteStartElement("robot");
            w.WriteAttributeString("name", pkg);

            string baseLinkName = links[0].Name;
```

with:

```csharp
        private static void WriteUrdf(
            string path, string pkg, string baseLinkName, List<LinkDef> links,
            Dictionary<string, string> meshFiles, Dictionary<string, MassProps> masses,
            Dictionary<string, Vector3> jointOrigins, Dictionary<string, (double, double, double)> jointRpys,
            bool emitWorldLink, Matrix3 swToRos)
        {
            var uw = new URDFWriter(path);
            System.Xml.XmlWriter w = uw.writer;
            w.WriteStartDocument();
            w.WriteStartElement("robot");
            w.WriteAttributeString("name", pkg);
```

(`baseLinkName` is now a parameter — the caller already resolved it via
`LinkHierarchy.Roots`, so `WriteUrdf` must not independently recompute it
from `links[0]`, which would silently reintroduce the exact bug this task
fixes.)

Also add `using SW2GZ.Build;` to the top of the file if it is not already
present — it is (line 45 in the current file), so no change needed there.

- [ ] **Step 4: Run the new tests and the full existing Sw2gzRobotExporterTests suite**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj -c Release --filter "FullyQualifiedName~Sw2gzRobotExporterTests"`
Expected: PASS — all of `Export_WritesUrdfWithLinksMeshesAndFixedJoint`,
`Export_JointOriginIsRealTranslationDelta_NotIdentity`,
`Export_JointRotationIsRealRelativeRotation_NotIdentity`,
`Export_NoLinks_Throws`, `Export_MissingMaterial_FallsBackToPlaceholderMassAndWarns`,
`Export_EmitWorldLink_AddsWorldJointWithRotation` (all existing — these are
all 2-level trees, so parent-relative and root-relative give the same
answer; they must stay green as the degenerate case), plus the two new
tests from Step 1.

- [ ] **Step 5: Commit**

```bash
git add SW2GZ/URDFExport/Sw2gzRobotExporter.cs Test/URDFExport/Sw2gzRobotExporterTests.cs
git commit -m "fix(robot): joint origin relative to declared parent, root by tree structure"
```

---

### Task 3: `Sw2gzRobotExporter` — multi-mesh union per link

Every component assigned to a link (`LinkDef.ComponentIds`, already a list
— the wizard's mesh picker already supports multi-select) currently has
only its first entry tessellated; the rest are silently dropped from the
export. This task unions all of them into one mesh per link.

**Files:**
- Modify: `SW2GZ/URDFExport/Sw2gzRobotExporter.cs`
- Test: `Test/URDFExport/Sw2gzRobotExporterTests.cs`

- [ ] **Step 1: Write the failing test**

Add a `FakeMultiTess` test double and a new test to
`Sw2gzRobotExporterTests` — add `FakeMultiTess` right after the existing
`FakeTess` class:

```csharp
        private sealed class FakeMultiTess : IMeshTessellator
        {
            private readonly Dictionary<string, MeshData> _meshes;
            public FakeMultiTess(Dictionary<string, MeshData> meshes) => _meshes = meshes;
            public MeshData Tessellate(string n, TessellationLod lod) =>
                _meshes.TryGetValue(n, out MeshData m) ? m : new MeshData(Array.Empty<Vector3>(), Array.Empty<int>(), null);
        }
```

Add this test after `Export_RootDetectedByTreeStructure_NotListPosition`
(from Task 2). It needs `System.Globalization` for culture-invariant
parsing — add `using System.Globalization;` to the file's using block:

```csharp
        [Fact]
        public void Export_MultiComponentLink_UnionsAllMeshesInLinkReferenceFrame()
        {
            var links = new List<LinkDef>
            {
                new LinkDef { Name = "base_link", ComponentIds = { "base-1@asm" }, ParentName = "" },
                new LinkDef { Name = "arm_link",  ComponentIds = { "arm-a@asm", "arm-b@asm" }, ParentName = "base_link" },
            };
            var cfg = new Sw2gzExportConfig
            {
                Mode = SW2GZ.Ros2.ExportMode.RobotPackage,
                PackageName = "my_robot",
                RobotLinks = links,
            };
            var poses = new Dictionary<string, (Matrix3, Vector3)>
            {
                ["base-1@asm"] = (Matrix3.Identity, Vector3.Zero),
                ["arm-a@asm"]  = (Matrix3.Identity, new Vector3(1, 0, 0)),
                ["arm-b@asm"]  = (Matrix3.Identity, new Vector3(1, 0, 0)),
            };
            var meshA = new MeshData(
                new[] { new Vector3(1, 0, 0), new Vector3(2, 0, 0), new Vector3(1, 1, 0) },
                new[] { 0, 1, 2 }, null);
            var meshB = new MeshData(
                new[] { new Vector3(1, 0, 5), new Vector3(2, 0, 5), new Vector3(1, 1, 5) },
                new[] { 0, 1, 2 }, null);
            var tess = new FakeMultiTess(new Dictionary<string, MeshData>
            {
                ["base-1@asm"] = new MeshData(new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) }, new[] { 0, 1, 2 }, null),
                ["arm-a@asm"]  = meshA,
                ["arm-b@asm"]  = meshB,
            });

            Sw2gzRobotExporter.Export(tess, new FakeMassProps(), new FakePoses(poses), cfg, _dir, Matrix3.Identity);

            string daePath = Path.Combine(_dir, "my_robot_ws", "src", "my_robot", "meshes", "arm_link.dae");
            Assert.True(File.Exists(daePath));

            XNamespace ns = "http://www.collada.org/2005/11/COLLADASchema";
            XDocument dae = XDocument.Load(daePath);
            XElement posArray = dae.Descendants(ns + "float_array")
                .Single(e => (string)e.Attribute("id") == "g0-pos-array");
            int floatCount = int.Parse((string)posArray.Attribute("count"));

            // Both components' triangles survive the union: 3 verts each, 3
            // floats per vert = 18 total (not 9 — which is what a
            // "first component only" regression would silently produce).
            Assert.Equal(18, floatCount);

            // arm-b's vertices sit at z=5 in its own (identity-rotation,
            // translation (1,0,0)) frame; arm_link's reference frame is
            // arm-a's pose (also (1,0,0), identity) — so after un-baking,
            // arm-b's local vertices should still carry that z=5 offset
            // (proves it was folded into the SAME shared frame as arm-a,
            // not silently dropped or mis-transformed).
            string[] floats = posArray.Value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var zValues = new List<double>();
            for (int i = 2; i < floats.Length; i += 3)
                zValues.Add(double.Parse(floats[i], CultureInfo.InvariantCulture));
            Assert.Contains(zValues, z => System.Math.Abs(z - 5.0) < 1e-3);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj -c Release --filter "FullyQualifiedName~Export_MultiComponentLink_UnionsAllMeshesInLinkReferenceFrame"`
Expected: FAIL — `floatCount` is `9` (only `arm-a`'s 3 vertices), not `18`;
`arm-b`'s mesh was never tessellated because `Export` still reads
`ComponentIds.FirstOrDefault()`.

- [ ] **Step 3: Implement the mesh-union helper and wire it in**

In `SW2GZ/URDFExport/Sw2gzRobotExporter.cs`, add `using System.Drawing;` to
the using block (needed for the `Color?` local in the new helper).

Replace this block inside the `Export` loop:

```csharp
                if (meshWorld != null)
                {
                    MeshData meshLocal = link.Name == baseLinkName
                        ? Translate(meshWorld, -linkT)
                        : UnbakeToLocal(meshWorld, linkR, linkT);

                    string daeFile = link.Name + ".dae";
                    DaeWriter.Write(meshLocal, Path.Combine(meshesDir, daeFile), withNormals: true);
                    meshFiles[link.Name] = daeFile;
                }
```

and the tessellate-try/catch immediately above it:

```csharp
                MeshData meshWorld;
                try
                {
                    meshWorld = tess.Tessellate(compName, TessellationLod.Fine);
                }
                catch (Exception ex)
                {
                    issues.Add(new ValidationIssue(IssueSeverity.Warning, "ROBOT.MESH",
                        "Link '" + link.Name + "' — could not tessellate '" + compName + "': " + ex.Message,
                        "Sw2gzRobotExporter"));
                    meshWorld = null;
                }
```

with a single call to the new union helper (root uses a forced-identity
reference rotation — same "root's own rotation stays baked into the mesh,
not represented as TF orientation" convention as before; every other link
uses its real reference rotation):

```csharp
                MeshData meshLocal = link.Name == baseLinkName
                    ? UnionMeshInLocalFrame(tess, link.ComponentIds, Matrix3.Identity, linkT, issues, link.Name)
                    : UnionMeshInLocalFrame(tess, link.ComponentIds, linkR, linkT, issues, link.Name);

                if (meshLocal != null)
                {
                    string daeFile = link.Name + ".dae";
                    DaeWriter.Write(meshLocal, Path.Combine(meshesDir, daeFile), withNormals: true);
                    meshFiles[link.Name] = daeFile;
                }
```

Delete the now-dead `Translate` and `UnbakeToLocal` private methods (both
fully subsumed by `UnionMeshInLocalFrame` — a single-component link is just
a 1-element union):

```csharp
        private static MeshData Translate(MeshData mesh, Vector3 t)
        {
            if (mesh?.Vertices == null || mesh.Vertices.Length == 0) return mesh;
            var shifted = new Vector3[mesh.Vertices.Length];
            for (int i = 0; i < shifted.Length; i++) shifted[i] = mesh.Vertices[i] + t;
            return new MeshData(shifted, mesh.Triangles, mesh.MaterialColor);
        }

        // p_local = R^T * (p_world - t) — reverses the tessellator's bake so the
        // mesh sits in the component's own native part frame.
        private static MeshData UnbakeToLocal(MeshData mesh, Matrix3 r, Vector3 t)
        {
            if (mesh?.Vertices == null || mesh.Vertices.Length == 0) return mesh;
            Matrix3 rInv = r.Transpose();
            var local = new Vector3[mesh.Vertices.Length];
            for (int i = 0; i < local.Length; i++) local[i] = rInv.Mul(mesh.Vertices[i] - t);
            return new MeshData(local, mesh.Triangles, mesh.MaterialColor);
        }
```

Add the new helper in their place:

```csharp
        // Every component assigned to a link gets tessellated and folded
        // into ONE mesh, all expressed in the SAME reference frame (refR,
        // refT) — not each component's own frame, which would scatter the
        // pieces apart. Generalizes the old single-component un-bake to N
        // components via the same vertex-offset + index-shift pattern
        // SolidWorksMeshTessellator already uses internally to union
        // multiple solid bodies within one component.
        private static MeshData UnionMeshInLocalFrame(
            IMeshTessellator tess, IReadOnlyList<string> componentIds,
            Matrix3 refR, Vector3 refT, List<ValidationIssue> issues, string linkName)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            Color? color = null;
            Matrix3 refRInv = refR.Transpose();

            foreach (string compName in componentIds ?? (IReadOnlyList<string>)Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(compName)) continue;

                MeshData meshWorld;
                try
                {
                    meshWorld = tess.Tessellate(compName, TessellationLod.Fine);
                }
                catch (Exception ex)
                {
                    issues.Add(new ValidationIssue(IssueSeverity.Warning, "ROBOT.MESH",
                        "Link '" + linkName + "' — could not tessellate '" + compName + "': " + ex.Message,
                        "Sw2gzRobotExporter"));
                    continue;
                }
                if (meshWorld?.Vertices == null || meshWorld.Vertices.Length == 0) continue;

                color ??= meshWorld.MaterialColor;
                int baseIdx = verts.Count;
                foreach (Vector3 v in meshWorld.Vertices) verts.Add(refRInv.Mul(v - refT));
                foreach (int idx in meshWorld.Triangles) tris.Add(baseIdx + idx);
            }

            return verts.Count == 0 ? null : new MeshData(verts.ToArray(), tris.ToArray(), color);
        }
```

- [ ] **Step 4: Run the new test and the full existing Sw2gzRobotExporterTests suite**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj -c Release --filter "FullyQualifiedName~Sw2gzRobotExporterTests"`
Expected: PASS — all existing tests (single-component links are a 1-element
union, byte-identical output) plus the new multi-component test.

- [ ] **Step 5: Commit**

```bash
git add SW2GZ/URDFExport/Sw2gzRobotExporter.cs Test/URDFExport/Sw2gzRobotExporterTests.cs
git commit -m "feat(robot): union every assigned mesh component per link, not just the first"
```

---

### Task 4: `Sw2gzRobotExporter` — multi-part mass/inertia combination

**Files:**
- Modify: `SW2GZ/URDFExport/Sw2gzRobotExporter.cs`
- Test: `Test/URDFExport/Sw2gzRobotExporterTests.cs`

- [ ] **Step 1: Write the failing test**

Add a `FakeMultiMassProps` test double (after `FakeMultiTess` from Task 3)
and a new test (after `Export_MultiComponentLink_UnionsAllMeshesInLinkReferenceFrame`
from Task 3):

```csharp
        private sealed class FakeMultiMassProps : IMassProperties
        {
            private readonly Dictionary<string, MassProps> _masses;
            public FakeMultiMassProps(Dictionary<string, MassProps> masses) => _masses = masses;
            public MassProps Get(string componentPathName) =>
                _masses.TryGetValue(componentPathName, out MassProps m) ? m : new MassProps(0.1, Vector3.Zero, Matrix3.Identity);
        }
```

```csharp
        [Fact]
        public void Export_MultiComponentLink_CombinesMassOfAllAssignedComponents()
        {
            var links = new List<LinkDef>
            {
                new LinkDef { Name = "base_link", ComponentIds = { "base-1@asm" }, ParentName = "" },
                new LinkDef { Name = "arm_link",  ComponentIds = { "arm-a@asm", "arm-b@asm" }, ParentName = "base_link" },
            };
            var cfg = new Sw2gzExportConfig
            {
                Mode = SW2GZ.Ros2.ExportMode.RobotPackage,
                PackageName = "my_robot",
                RobotLinks = links,
            };
            var poses = new Dictionary<string, (Matrix3, Vector3)>
            {
                ["base-1@asm"] = (Matrix3.Identity, Vector3.Zero),
                ["arm-a@asm"]  = (Matrix3.Identity, new Vector3(1, 0, 0)),
                ["arm-b@asm"]  = (Matrix3.Identity, new Vector3(1, 0, 0)),
            };
            var massProps = new FakeMultiMassProps(new Dictionary<string, MassProps>
            {
                ["base-1@asm"] = new MassProps(9.0, Vector3.Zero, Matrix3.Identity),
                ["arm-a@asm"]  = new MassProps(1.5, Vector3.Zero, Matrix3.Identity),
                ["arm-b@asm"]  = new MassProps(2.5, Vector3.Zero, Matrix3.Identity),
            });

            Sw2gzRobotExporter.Export(new FakeTess(), massProps, new FakePoses(poses), cfg, _dir, Matrix3.Identity);

            XElement root = XElement.Load(Path.Combine(_dir, "my_robot_ws", "src", "my_robot", "urdf", "my_robot.urdf.xacro"));
            XElement armLink = root.Elements("link").Single(l => (string)l.Attribute("name") == "arm_link");
            double mass = double.Parse((string)armLink.Element("inertial").Element("mass").Attribute("value"), CultureInfo.InvariantCulture);

            // 1.5 + 2.5, not just arm-a's 1.5 (a "first component only"
            // regression would report 1.5, silently dropping arm-b).
            Assert.Equal(4.0, mass, 3);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj -c Release --filter "FullyQualifiedName~Export_MultiComponentLink_CombinesMassOfAllAssignedComponents"`
Expected: FAIL — reported mass is `1.5` (only `arm-a`), not `4.0`.

- [ ] **Step 3: Implement the mass-combination helper and wire it in**

Replace this block inside the `Export` loop:

```csharp
                try
                {
                    masses[link.Name] = massProps.Get(compName);
                }
                catch (Exception ex)
                {
                    // ponytail: no SW material on this part → placeholder mass/
                    // inertia. Physical accuracy is out of scope for this
                    // validating cut (no actuation/physics sim runs on the
                    // export yet); revisit once Robot mode gets a real
                    // inertia pipeline.
                    masses[link.Name] = new MassProps(0.1, Vector3.Zero, Matrix3.Identity);
                    issues.Add(new ValidationIssue(IssueSeverity.Warning, "ROBOT.MASS",
                        "Link '" + link.Name + "' — no material on '" + compName + "', using placeholder mass: " + ex.Message,
                        "Sw2gzRobotExporter"));
                }
```

with:

```csharp
                masses[link.Name] = CombineMass(massProps, poses, link.ComponentIds, linkR, linkT, issues, link.Name);
```

Add the new helper next to `UnionMeshInLocalFrame`:

```csharp
        // Combines every assigned component's own mass/COM/inertia
        // (parallel-axis, via InertialAggregator) into one MassProps rebased
        // into the link's own reference frame (linkR/linkT — the SAME pose
        // used for the joint and mesh math). For a single-component link
        // this is byte-identical to using that component's raw MassProps
        // directly: InertialAggregator's rebase exactly cancels when a
        // part's own frame equals the anchor (see
        // CombineWithLinkAnchor_SinglePartAtAnchor_RebasesBackToPartLocal /
        // its Matrix3 twin). Mass/inertia physical accuracy beyond this is
        // already out of scope for this validating cut (see the ponytail
        // note this replaces) — this does not force the base_link
        // identity-orientation convention the way mesh un-baking does,
        // since that would only matter for a multi-component root with a
        // non-identity native rotation, which isn't exercised yet.
        private static MassProps CombineMass(
            IMassProperties massProps, IComponentPoses poses, IReadOnlyList<string> componentIds,
            Matrix3 linkR, Vector3 linkT, List<ValidationIssue> issues, string linkName)
        {
            var parts = new List<(MassProps Props, Matrix3 R, Vector3 T)>();
            foreach (string compName in componentIds ?? (IReadOnlyList<string>)Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(compName)) continue;

                MassProps mp;
                try
                {
                    mp = massProps.Get(compName);
                }
                catch (Exception ex)
                {
                    mp = new MassProps(0.1, Vector3.Zero, Matrix3.Identity);
                    issues.Add(new ValidationIssue(IssueSeverity.Warning, "ROBOT.MASS",
                        "Link '" + linkName + "' — no material on '" + compName + "', using placeholder mass: " + ex.Message,
                        "Sw2gzRobotExporter"));
                }
                (Matrix3 compR, Vector3 compT) = TryGetPose(poses, compName, issues, linkName);
                parts.Add((mp, compR, compT));
            }

            if (parts.Count == 0) return new MassProps(0.1, Vector3.Zero, Matrix3.Identity);
            return InertialAggregator.Combine(parts, linkR, linkT);
        }
```

- [ ] **Step 4: Run the new test and the full existing Sw2gzRobotExporterTests suite**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj -c Release --filter "FullyQualifiedName~Sw2gzRobotExporterTests"`
Expected: PASS — including `Export_WritesUrdfWithLinksMeshesAndFixedJoint`
(still asserts `arm_link`'s mass is exactly `"2.5"` — this is the
single-component byte-identical regression check: `arm_link`'s one
component's own pose equals its reference pose, so `InertialAggregator`'s
rebase cancels exactly, mass stays exactly 2.5).

- [ ] **Step 5: Run the full test suite (not just this file) to confirm no cross-file regressions**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj -c Release`
Expected: PASS — 470 baseline + all new tests from Tasks 1-4 (should be
around 480).

- [ ] **Step 6: Commit**

```bash
git add SW2GZ/URDFExport/Sw2gzRobotExporter.cs Test/URDFExport/Sw2gzRobotExporterTests.cs
git commit -m "feat(robot): combine mass/inertia of every assigned mesh component per link"
```

---

### Task 5: UI — surface which mesh component is "primary"

The first `ComponentIds` entry now silently defines a link's whole frame
(mesh anchor, joint origin, inertial rebase — Tasks 2-4). That needs to be
visible in the wizard, not implicit.

**Files:**
- Modify: `SW2GZ/UI/Pmp/Sw2gzCreateRobotPmp.cs`
- Modify: `SW2GZ/UI/LinkTreeView.cs`

No test file — `Sw2gzCreateRobotPmp.cs` is `#if SW_INTEROP`-gated (COM,
not unit-testable outside SolidWorks) and `LinkTreeView.cs` is explicitly
"not source-linked into the net8 test project" per its own header comment.
Verified by compiling + the live SW check in Task 6.

- [ ] **Step 1: Mark the primary mesh in `Sw2gzCreateRobotPmp`'s selected-info label**

In `SW2GZ/UI/Pmp/Sw2gzCreateRobotPmp.cs`, replace:

```csharp
        private void RefreshSelectedInfo(LinkDef link)
        {
            if (_selectedInfoLabel == null) return;
            _selectedInfoLabel.Caption = link == null
                ? "Selected: (none)"
                : "Selected: " + link.Name + "    Mesh: "
                    + (link.ComponentIds.Count == 0 ? "(none)" : string.Join(", ", link.ComponentIds));
        }
```

with:

```csharp
        private void RefreshSelectedInfo(LinkDef link)
        {
            if (_selectedInfoLabel == null) return;
            _selectedInfoLabel.Caption = link == null
                ? "Selected: (none)"
                : "Selected: " + link.Name + "    Mesh: " + DescribeMeshes(link.ComponentIds);
        }

        // The first component defines the link's own frame (mesh anchor,
        // joint origin, inertial rebase — see
        // docs/superpowers/specs/2026-07-02-robot-joint-relative-pose-design.md),
        // so it's marked (primary) wherever the mesh list is shown —
        // otherwise which one drives the frame is invisible.
        private static string DescribeMeshes(List<string> componentIds)
        {
            if (componentIds == null || componentIds.Count == 0) return "(none)";
            var parts = new List<string>(componentIds.Count);
            for (int i = 0; i < componentIds.Count; i++)
                parts.Add(i == 0 ? componentIds[i] + " (primary)" : componentIds[i]);
            return string.Join(", ", parts);
        }
```

- [ ] **Step 2: Mark the primary mesh in `LinkTreeView`'s node tooltip**

In `SW2GZ/UI/LinkTreeView.cs`, the constructor already sets
`ShowNodeToolTips = true;` but no node ever gets a `ToolTipText` — this
step finishes wiring that already-declared-but-unused feature instead of
adding new UI surface.

Replace:

```csharp
        private TreeNode BuildNode(LinkDef link)
        {
            bool isRoot = string.IsNullOrEmpty(link.ParentName);
            int n = link.ComponentIds?.Count ?? 0;
            // Links only — the component-name leaf duplicated the link name and added
            // no information; show the part count as a suffix instead.
            string label = (link.Name ?? "")
                + (isRoot ? "  (base)" : "")
                + "  [" + n + (n == 1 ? " part]" : " parts]");
            var node = new TreeNode(label) { Tag = link };
            if (n == 0) node.ForeColor = System.Drawing.Color.Firebrick;   // unassigned = needs attention
            foreach (LinkDef child in LinkHierarchy.ChildrenOf(links, link.Name))
                node.Nodes.Add(BuildNode(child));
            return node;
        }
```

with:

```csharp
        private TreeNode BuildNode(LinkDef link)
        {
            bool isRoot = string.IsNullOrEmpty(link.ParentName);
            int n = link.ComponentIds?.Count ?? 0;
            // Links only — the component-name leaf duplicated the link name and added
            // no information; show the part count as a suffix instead.
            string label = (link.Name ?? "")
                + (isRoot ? "  (base)" : "")
                + "  [" + n + (n == 1 ? " part]" : " parts]");
            var node = new TreeNode(label) { Tag = link };
            if (n == 0) node.ForeColor = System.Drawing.Color.Firebrick;   // unassigned = needs attention
            node.ToolTipText = DescribePrimary(link);
            foreach (LinkDef child in LinkHierarchy.ChildrenOf(links, link.Name))
                node.Nodes.Add(BuildNode(child));
            return node;
        }

        // The first ComponentIds entry defines this link's whole frame
        // (mesh anchor, joint origin, inertial rebase) — surfaced on hover
        // since the compact node label has no room for it.
        private static string DescribePrimary(LinkDef link)
        {
            List<string> ids = link.ComponentIds;
            if (ids == null || ids.Count == 0) return "no mesh assigned";
            if (ids.Count == 1) return ids[0];
            return "primary: " + ids[0] + "  |  also: " + string.Join(", ", ids.GetRange(1, ids.Count - 1));
        }
```

Then update `RefreshActiveNodeLabel` (keeps the tooltip in sync after an
F2/inline rename, which rebuilds the label text without a full `Rebuild()`)
— replace:

```csharp
        public void RefreshActiveNodeLabel()
        {
            TreeNode n = SelectedNode;
            if (n == null || !(n.Tag is LinkDef link)) return;
            bool isRoot = string.IsNullOrEmpty(link.ParentName);
            int parts = link.ComponentIds?.Count ?? 0;
            n.Text = (link.Name ?? "")
                + (isRoot ? "  (base)" : "")
                + "  [" + parts + (parts == 1 ? " part]" : " parts]");
        }
```

with:

```csharp
        public void RefreshActiveNodeLabel()
        {
            TreeNode n = SelectedNode;
            if (n == null || !(n.Tag is LinkDef link)) return;
            bool isRoot = string.IsNullOrEmpty(link.ParentName);
            int parts = link.ComponentIds?.Count ?? 0;
            n.Text = (link.Name ?? "")
                + (isRoot ? "  (base)" : "")
                + "  [" + parts + (parts == 1 ? " part]" : " parts]");
            n.ToolTipText = DescribePrimary(link);
        }
```

- [ ] **Step 3: Commit**

```bash
git add SW2GZ/UI/Pmp/Sw2gzCreateRobotPmp.cs SW2GZ/UI/LinkTreeView.cs
git commit -m "feat(robot): surface which mesh component is primary in the Links UI"
```

---

### Task 6: Full verification — build, test, live SW check, deploy

**Files:** none (build/test/deploy only)

- [ ] **Step 1: Build the add-in**

Run (PowerShell, SolidWorks closed):
```powershell
$msb = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
& $msb "C:\aryan\SW2GZ\SW2GZ\SW2GZ.csproj" /p:Configuration=Release /p:SolutionDir=C:\aryan\SW2GZ\ /t:Build /v:minimal /nologo /m
```
Expected: `SW2GZ -> C:\aryan\SW2GZ\SW2GZ\bin\Release\SW2GZ.dll`. The
`MSB3216`/regasm access-denied line is a known non-fatal warning (see
memory `sw2gz-build-deploy`) — the DLL still compiles.

- [ ] **Step 2: Build the test project**

```powershell
& $msb "C:\aryan\SW2GZ\Test\SW2GZ.Writers.Test.csproj" /p:Configuration=Release /p:SolutionDir=C:\aryan\SW2GZ\ /t:Build /v:minimal /nologo /m
```
Expected: `SW2GZ.Writers.Test -> C:\aryan\SW2GZ\Test\bin\Release\net8.0\SW2GZ.Writers.Test.dll`, no errors.

- [ ] **Step 3: Run the full test suite**

```bash
dotnet test Test/SW2GZ.Writers.Test.csproj -c Release --no-build
```
Expected: `Passed!` with 0 failures. Count should be the 470 baseline plus
the new tests from Tasks 1–4 (5 in `InertialAggregatorMatrixTests` + 4 in
`Sw2gzRobotExporterTests` = 9 new → ~479).

- [ ] **Step 4: Deploy**

Confirm SolidWorks is closed, then elevated-copy the fresh DLL:
```powershell
Get-Process SLDWORKS -ErrorAction SilentlyContinue   # must return nothing
Copy-Item "C:\aryan\SW2GZ\SW2GZ\bin\Release\SW2GZ.dll" "C:\Program Files\SW2GZ\SW2GZ.dll" -Force
```
(Needs admin — run via an elevated `Start-Process powershell -Verb RunAs`
if the direct copy is denied.) Verify: `Get-Item "C:\Program Files\SW2GZ\SW2GZ.dll"`
timestamp matches the fresh `bin\Release` build.

- [ ] **Step 5: Live SW check on `FULL_ARM.SLDASM`**

Open Create Robot. Build a 3-level chain: pick a part → Add link (becomes
`base_link`) → pick another part → name it → click `base_link` in the tree
→ Add link (child of base) → pick a third part → name it → click the
*previous child* in the tree → Add link (grandchild). Then build a
multi-mesh link: pick two parts at once in the Mesh selector → name → Add.
Finish the wizard, Export (or Preview). Confirm in the output/preview:
- The grandchild's mesh and joint sit at the correct relative position —
  not offset as if computed relative to `base_link`.
- The 2-part link's mesh shows BOTH parts, correctly positioned relative to
  each other (not just the first one picked).
- Removing/re-adding does not crash the PMP (same live-checkpoint standard
  as the two prior UI-only sessions).

- [ ] **Step 6: Update progress notes**

Add a session entry to `agent-progress/progress.md` under the "CONTINUE
HERE" section summarizing what shipped (parent-relative joints, tree-based
root detection, multi-mesh union, multi-part mass combination, primary-mesh
UI indicator), the test count, and the live-check result. Follow the same
format as the two prior entries in that file from this session.

- [ ] **Step 7: Commit the progress note**

```bash
git add agent-progress/progress.md
git commit -m "docs(progress): parent-relative joints + multi-mesh links shipped, live-tested"
```
