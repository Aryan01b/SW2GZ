# Planar + Floating Joint Types Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the two URDF joint types the mate-driven pipeline is missing — `planar` and `floating` — end to end (enums → mate mapping → seeding → URDF serialization → validation), with unit tests for every pure-code path.

**Architecture:** Two enum values (`Planar`, `Floating`) flow through the existing path (`MateInfo` → `JointDef` → `MateSpec` → `UrdfJoint` → URDF XML). Floating = a joint with no mate assigned (6-DOF, no axis/limit); planar = a coincident planar-face mate (axis = face normal, no limit). Each touched file keeps its single responsibility; we only widen switches and one default. Mimic and ball are out of scope (mimic deferred; ball is SDF-only → Spec B).

**Tech Stack:** C# / .NET Framework 4.8 (`net48`), xUnit (`Test/SW2GZ.Writers.Test.csproj`, `net8.0`), legacy MSBuild csproj (files listed explicitly). Build via VS BuildTools MSBuild (see `memory/sw2gz-build-deploy`).

**Spec:** `docs/superpowers/specs/2026-06-03-planar-floating-joints-design.md`

**Test command (pure tests, no SolidWorks needed):**
```
dotnet test Test/SW2GZ.Writers.Test.csproj
```

**Conventions:**
- New `.cs` source files must be added to BOTH `SW2GZ/SW2GZ.csproj` and `Test/SW2GZ.Writers.Test.csproj`. This plan adds tests to **existing** test files only (no csproj edits needed). No new source files are created.
- Commit messages: no AI attribution (`memory/no-ai-attribution`). Use PowerShell here-strings; avoid `"` double-quotes in the message body.
- `git` runs from repo root `C:\aryan\SW2GZ`.

---

## Task 1: Extend enums + URDF serialization for planar/floating

Adds `Planar`/`Floating` to both joint enums, teaches the URDF serializer their type strings and axis/limit rules, and stops the structural validator from rejecting a (legitimately axis-less) floating joint.

**Files:**
- Modify: `SW2GZ/Build/Urdf/UrdfJoint.cs:3`
- Modify: `SW2GZ/Build/MateSpec.cs:8`
- Modify: `SW2GZ/Write/Urdf/UrdfSerializer.cs:213` (axis condition) and `:240` (`JointTypeString`)
- Modify: `SW2GZ/Validate/RobotModelValidator.cs:338` (axis check skip)
- Test: `Test/Write/Urdf/UrdfSerializerJointRpyTests.cs` (add to existing file)

- [ ] **Step 1: Write the failing tests**

Append these two tests inside the `UrdfSerializerJointRpyTests` class in `Test/Write/Urdf/UrdfSerializerJointRpyTests.cs` (the helpers `Meta(...)` and `Link(...)` already exist in that file):

```csharp
        [Fact]
        public void PlanarJoint_EmitsTypeAndAxis_NoLimit()
        {
            var joint = new UrdfJoint("plane_j", UrdfJointType.Planar, "base_link", "arm1",
                Pose.Identity, Vector3.UnitZ, null, null, 0, 0, UrdfCmdInterface.Position);
            var model = RobotModelBuilder.Build(Meta(),
                new[] { Link("base_link"), Link("arm1") }, new[] { joint });

            string xml = UrdfSerializer.SerializeBody(model);

            Assert.Contains("type=\"planar\"", xml);
            Assert.Contains("<axis xyz=\"0 0 1\"/>", xml);
            Assert.DoesNotContain("<limit", xml);
        }

        [Fact]
        public void FloatingJoint_EmitsType_NoAxisNoLimit()
        {
            // Floating is axis-less by definition (zero axis must NOT error).
            var joint = new UrdfJoint("free_j", UrdfJointType.Floating, "base_link", "arm1",
                Pose.Identity, Vector3.Zero, null, null, 0, 0, UrdfCmdInterface.Position);
            var model = RobotModelBuilder.Build(Meta(),
                new[] { Link("base_link"), Link("arm1") }, new[] { joint });

            string xml = UrdfSerializer.SerializeBody(model);

            Assert.Contains("type=\"floating\"", xml);
            Assert.DoesNotContain("<axis", xml);
            Assert.DoesNotContain("<limit", xml);
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter "FullyQualifiedName~UrdfSerializerJointRpyTests"`
Expected: BUILD FAILS — `UrdfJointType` has no member `Planar`/`Floating` (CS0117). (That is the "failing" state for an enum-driven change.)

- [ ] **Step 3: Add the enum values**

In `SW2GZ/Build/Urdf/UrdfJoint.cs` line 3, change:
```csharp
    public enum UrdfJointType { Fixed, Revolute, Continuous, Prismatic }
```
to:
```csharp
    public enum UrdfJointType { Fixed, Revolute, Continuous, Prismatic, Planar, Floating }
```

In `SW2GZ/Build/MateSpec.cs` line 8, change:
```csharp
    public enum MateKind { Fixed, Revolute, Continuous, Prismatic }
```
to:
```csharp
    public enum MateKind { Fixed, Revolute, Continuous, Prismatic, Planar, Floating }
```

- [ ] **Step 4: Teach the serializer the type strings**

In `SW2GZ/Write/Urdf/UrdfSerializer.cs`, update `JointTypeString` (line ~240) to add the two cases before the throw:
```csharp
        private static string JointTypeString(UrdfJointType t) => t switch
        {
            UrdfJointType.Fixed      => "fixed",
            UrdfJointType.Revolute   => "revolute",
            UrdfJointType.Continuous => "continuous",
            UrdfJointType.Prismatic  => "prismatic",
            UrdfJointType.Planar     => "planar",
            UrdfJointType.Floating   => "floating",
            _                        => throw new System.InvalidOperationException($"Unhandled UrdfJointType: {t}"),
        };
```

- [ ] **Step 5: Fix the axis-emit condition (skip Floating)**

In `SW2GZ/Write/Urdf/UrdfSerializer.cs` `AppendJoint` (line ~213), change:
```csharp
            if (j.Type != UrdfJointType.Fixed)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "    <axis xyz=\"{0} {1} {2}\"/>",
                    j.Axis.X, j.Axis.Y, j.Axis.Z));
            }
```
to:
```csharp
            // URDF axis applies to revolute/continuous/prismatic/planar (planar's
            // axis is the plane normal). Fixed and floating have no axis.
            if (j.Type != UrdfJointType.Fixed && j.Type != UrdfJointType.Floating)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "    <axis xyz=\"{0} {1} {2}\"/>",
                    j.Axis.X, j.Axis.Y, j.Axis.Z));
            }
```
The existing `<limit>` block (revolute/prismatic + continuous) is left unchanged — planar and floating correctly fall through and emit no `<limit>`.

- [ ] **Step 6: Stop the structural validator rejecting floating's zero axis**

In `SW2GZ/Validate/RobotModelValidator.cs` `CheckJointAxes` (line ~338), change:
```csharp
                if (j.Type == UrdfJointType.Fixed) continue;
```
to:
```csharp
                // Fixed and floating carry no axis; floating is 6-DOF by definition.
                if (j.Type == UrdfJointType.Fixed || j.Type == UrdfJointType.Floating) continue;
```
(Planar stays in the check — a planar joint with zero axis is a genuine error, consistent with it needing a plane normal.)

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter "FullyQualifiedName~UrdfSerializerJointRpyTests"`
Expected: PASS (all tests in the class, including the two new ones).

- [ ] **Step 8: Commit**

```
git add SW2GZ/Build/Urdf/UrdfJoint.cs SW2GZ/Build/MateSpec.cs SW2GZ/Write/Urdf/UrdfSerializer.cs SW2GZ/Validate/RobotModelValidator.cs Test/Write/Urdf/UrdfSerializerJointRpyTests.cs
git commit -m @'
Add Planar + Floating to joint enums and URDF serialization

UrdfJointType + MateKind gain Planar/Floating. Serializer emits planar/
floating type strings, axis for planar (not floating), no limit for either.
RobotModelValidator no longer flags a floating joint zero axis as an error.
'@
```

---

## Task 2: Mate-kind → joint-type mappings (build path)

Wires the new kinds through the three switches that convert between `MateKind` and `UrdfJointType` on the build/export path so an assigned planar/floating mate produces the right `UrdfJoint`.

**Files:**
- Modify: `SW2GZ/Build/JointBuilder.cs:18`
- Modify: `SW2GZ/Build/JointSeeder.cs:24` (`ToJointType`)
- Modify: `SW2GZ/SwSurface/WizardAssemblyWalker.cs:91` (`ToMateKind`, `#if SW_INTEROP` — not unit-tested)
- Test: `Test/Build/JointSeederTests.cs` and `Test/Build/JointBuilderTests.cs` (add to existing files)

- [ ] **Step 1: Write the failing tests**

In `Test/Build/JointSeederTests.cs`, add two `[InlineData]` rows to the existing `ToJointType_MapsMateKind` theory (after the `Prismatic` row, line ~90):
```csharp
        [InlineData(MateKind.Planar, UrdfJointType.Planar)]
        [InlineData(MateKind.Floating, UrdfJointType.Floating)]
```

In `Test/Build/JointBuilderTests.cs`, add these two tests inside the `JointBuilderTests` class (the helper `L(string)` already exists):
```csharp
        [Fact]
        public void Build_PlanarMate_MapsToPlanarJoint()
        {
            var mate = new MateSpec("plane_j", MateKind.Planar,
                Pose.Identity, Vector3.UnitZ, null, null, 0, 0, UrdfCmdInterface.Position, "a", "b");

            var (joint, _) = JointBuilder.Build(mate, L("a"), L("b"));

            Assert.Equal(UrdfJointType.Planar, joint.Type);
            Assert.Equal(Vector3.UnitZ, joint.Axis);
        }

        [Fact]
        public void Build_FloatingMate_MapsToFloatingJoint()
        {
            var mate = new MateSpec("free_j", MateKind.Floating,
                Pose.Identity, Vector3.Zero, null, null, 0, 0, UrdfCmdInterface.Position, "a", "b");

            var (joint, _) = JointBuilder.Build(mate, L("a"), L("b"));

            Assert.Equal(UrdfJointType.Floating, joint.Type);
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter "FullyQualifiedName~JointBuilderTests|FullyQualifiedName~JointSeederTests"`
Expected: FAIL — `Build_PlanarMate_MapsToPlanarJoint` asserts `Planar` but `JointBuilder` maps the unknown kind to `Fixed` (`Assert.Equal` failure: Expected Planar, Actual Fixed); same for floating. The two new theory rows likewise fail (Actual Fixed).

- [ ] **Step 3: Add the cases to JointBuilder**

In `SW2GZ/Build/JointBuilder.cs` (line ~18), change the switch to:
```csharp
            UrdfJointType type = mate.Kind switch
            {
                MateKind.Fixed      => UrdfJointType.Fixed,
                MateKind.Revolute   => UrdfJointType.Revolute,
                MateKind.Continuous => UrdfJointType.Continuous,
                MateKind.Prismatic  => UrdfJointType.Prismatic,
                MateKind.Planar     => UrdfJointType.Planar,
                MateKind.Floating   => UrdfJointType.Floating,
                _                   => UrdfJointType.Fixed,
            };
```

- [ ] **Step 4: Add the cases to JointSeeder.ToJointType**

In `SW2GZ/Build/JointSeeder.cs` (line ~24), change the switch to:
```csharp
        public static UrdfJointType ToJointType(MateKind kind)
        {
            switch (kind)
            {
                case MateKind.Revolute:   return UrdfJointType.Revolute;
                case MateKind.Continuous: return UrdfJointType.Continuous;
                case MateKind.Prismatic:  return UrdfJointType.Prismatic;
                case MateKind.Planar:     return UrdfJointType.Planar;
                case MateKind.Floating:   return UrdfJointType.Floating;
                default:                  return UrdfJointType.Fixed;
            }
        }
```

- [ ] **Step 5: Add the cases to WizardAssemblyWalker.ToMateKind**

In `SW2GZ/SwSurface/WizardAssemblyWalker.cs` (line ~91), change the switch to:
```csharp
        private static MateKind ToMateKind(UrdfJointType t)
        {
            switch (t)
            {
                case UrdfJointType.Revolute:   return MateKind.Revolute;
                case UrdfJointType.Continuous: return MateKind.Continuous;
                case UrdfJointType.Prismatic:  return MateKind.Prismatic;
                case UrdfJointType.Planar:     return MateKind.Planar;
                case UrdfJointType.Floating:   return MateKind.Floating;
                default:                       return MateKind.Fixed;
            }
        }
```
(This file is `#if SW_INTEROP`, so it is not compiled into the test project. It is exercised on the export/COM path and verified on the workstation; the mapping is a trivial mirror of `JointBuilder`.)

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter "FullyQualifiedName~JointBuilderTests|FullyQualifiedName~JointSeederTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```
git add SW2GZ/Build/JointBuilder.cs SW2GZ/Build/JointSeeder.cs SW2GZ/SwSurface/WizardAssemblyWalker.cs Test/Build/JointBuilderTests.cs Test/Build/JointSeederTests.cs
git commit -m @'
Map Planar/Floating across MateKind<->UrdfJointType switches

JointBuilder, JointSeeder.ToJointType, and WizardAssemblyWalker.ToMateKind
now carry the planar and floating kinds through the build/export path.
'@
```

---

## Task 3: Unassigned joint defaults to Floating

A freshly seeded joint with no mate assigned now becomes `Floating` (6-DOF) instead of `Fixed`. A joint with an existing assignment is preserved unchanged.

**Files:**
- Modify: `SW2GZ/Build/JointSeeder.cs:66` (the `else` / new-joint branch in `Sync`)
- Test: `Test/Build/JointSeederTests.cs` (update one existing test, add one)

- [ ] **Step 1: Update the existing test + add a new one**

In `Test/Build/JointSeederTests.cs`, in `SeedsOneJointPerNonRootLink_RootHasNone`, change the final assertion (line ~28) from:
```csharp
            Assert.Equal(UrdfJointType.Fixed, j.Type);    // until a mate is assigned
```
to:
```csharp
            Assert.Equal(UrdfJointType.Floating, j.Type); // no mate assigned = floating (6-DOF)
```

Then add this new test to the class:
```csharp
        [Fact]
        public void NewlySeededJoint_DefaultsToFloating()
        {
            var links = new List<LinkDef> { L("base", ""), L("arm", "base") };

            JointDef j = Assert.Single(JointSeeder.Sync(links, null));

            Assert.Equal(UrdfJointType.Floating, j.Type);
            Assert.Equal(string.Empty, j.MateName);   // unassigned
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter "FullyQualifiedName~JointSeederTests"`
Expected: FAIL — `SeedsOneJointPerNonRootLink_RootHasNone` and `NewlySeededJoint_DefaultsToFloating` both expect `Floating`, but `Sync` creates new joints with the `JointDef` default `Fixed` (Assert.Equal: Expected Floating, Actual Fixed).

- [ ] **Step 3: Default new seeded joints to Floating**

In `SW2GZ/Build/JointSeeder.cs`, in the `else` branch of `Sync` (line ~66), change:
```csharp
                else
                {
                    result.Add(new JointDef
                    {
                        Name = JointName(parent, l.Name),
                        ParentLink = parent,
                        ChildLink = l.Name,
                    });
                }
```
to:
```csharp
                else
                {
                    // No mate assigned yet → floating (6-DOF). The user assigns a
                    // mate (e.g. LOCK → fixed) to constrain it.
                    result.Add(new JointDef
                    {
                        Name = JointName(parent, l.Name),
                        ParentLink = parent,
                        ChildLink = l.Name,
                        Type = UrdfJointType.Floating,
                    });
                }
```
(The preserved-assignment branch above it is left unchanged, so existing joints — including the `Revolute`-typed fixtures in `PreservesAssignmentsButNameTracksTree` and `ReparentingUpdatesParentLink_KeepsEdits` — keep their type.)

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter "FullyQualifiedName~JointSeederTests"`
Expected: PASS (all 8 tests in the class).

- [ ] **Step 5: Commit**

```
git add SW2GZ/Build/JointSeeder.cs Test/Build/JointSeederTests.cs
git commit -m @'
Default unassigned seeded joints to Floating

A newly seeded joint with no mate assigned is now Floating (6-DOF) instead
of Fixed; assign a LOCK mate to get a rigid weld. Existing assignments are
preserved on re-seed.
'@
```

---

## Task 4: Advisory validator — floating clean, planar needs axis

`JointDefValidator` (the wizard's non-blocking warnings) must not nag a floating joint about a missing axis, and must warn a planar joint that has none.

**Files:**
- Modify: `SW2GZ/Build/JointDefValidator.cs:24`
- Test: `Test/Build/JointDefValidatorTests.cs` (add to existing file)

- [ ] **Step 1: Write the failing tests**

In `Test/Build/JointDefValidatorTests.cs`, add these two tests to the class:
```csharp
        [Fact]
        public void FloatingJointWithNoAxis_NoWarning()
        {
            var joints = new List<JointDef>
            {
                new JointDef { Name = "free", Type = UrdfJointType.Floating },  // axis 0,0,0
            };
            Assert.DoesNotContain(JointDefValidator.Validate(joints), w => w.Contains("axis"));
        }

        [Fact]
        public void PlanarJointWithNoAxis_Warns()
        {
            var joints = new List<JointDef>
            {
                new JointDef { Name = "plane", Type = UrdfJointType.Planar },   // axis 0,0,0
            };
            Assert.Contains(JointDefValidator.Validate(joints), w => w.Contains("plane") && w.Contains("axis"));
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter "FullyQualifiedName~JointDefValidatorTests"`
Expected: FAIL — `FloatingJointWithNoAxis_NoWarning` fails because the current `moving = Type != Fixed` makes a floating joint "moving" and it warns about the missing axis. (`PlanarJointWithNoAxis_Warns` already passes under the same rule, but is added to lock the behavior in.)

- [ ] **Step 3: Replace the `moving` check with an explicit needs-axis set**

In `SW2GZ/Build/JointDefValidator.cs` (line ~24), change:
```csharp
                bool moving = j.Type != UrdfJointType.Fixed;
                if (moving && !j.HasAxis)
                    warnings.Add($"Joint '{j.Name}' is {j.Type.ToString().ToLowerInvariant()} " +
                                 "but has no axis — select or generate a reference axis.");
```
to:
```csharp
                // Axis is meaningful for revolute/continuous/prismatic and for
                // planar (the plane normal). Fixed and floating carry no axis.
                bool needsAxis = j.Type == UrdfJointType.Revolute
                              || j.Type == UrdfJointType.Continuous
                              || j.Type == UrdfJointType.Prismatic
                              || j.Type == UrdfJointType.Planar;
                if (needsAxis && !j.HasAxis)
                    warnings.Add($"Joint '{j.Name}' is {j.Type.ToString().ToLowerInvariant()} " +
                                 "but has no axis — select or generate a reference axis.");
```
The limit check below it is unchanged (planar/floating have no limits, so it does not apply to them).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter "FullyQualifiedName~JointDefValidatorTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```
git add SW2GZ/Build/JointDefValidator.cs Test/Build/JointDefValidatorTests.cs
git commit -m @'
Validator: floating needs no axis, planar does

JointDefValidator now requires an axis only for revolute/continuous/
prismatic/planar; floating no longer triggers a spurious missing-axis warning.
'@
```

---

## Task 5: COM — coincident planar-face mate → Planar (workstation-only)

Teaches the SolidWorks mate walker to surface a coincident planar-face mate as a `Planar` kind with the plane normal as its axis. This is `#if SW_INTEROP` COM code, not unit-testable; it is verified by the user on the SolidWorks workstation.

**Files:**
- Modify: `SW2GZ/SwSurface/SolidWorksAssemblyWalker.cs:276` (the `swMateType_e` switch in `TryAddMateInfo`) + add a private helper near `MateAxisDirection` (line ~433)

- [ ] **Step 1: Add a planar-face detector helper**

In `SW2GZ/SwSurface/SolidWorksAssemblyWalker.cs`, inside the `#if SW_INTEROP` region, add this helper directly after the `MateAxisDirection` method (after line ~457):
```csharp
        // True when the mate couples two planar faces (a face-coincident mate),
        // i.e. the references slide in-plane and rotate about the shared normal —
        // URDF planar-joint semantics. Uses Face2.GetSurface().IsPlane().
        private static bool BothEntitiesPlanarFaces(Mate2 mate)
        {
            int entCount = mate.GetMateEntityCount();
            int planarFaces = 0;
            for (int i = 0; i < entCount; i++)
            {
                MateEntity2 ent = mate.MateEntity(i);
                if (ent == null) continue;
                try
                {
                    var face = ent.Reference as Face2;
                    if (face == null) continue;
                    var surf = face.GetSurface() as Surface;
                    if (surf != null && surf.IsPlane()) planarFaces++;
                }
                finally { Marshal.ReleaseComObject(ent); }
            }
            return planarFaces >= 2;
        }
```

- [ ] **Step 2: Add the coincident → planar case to the mate switch**

In `SW2GZ/SwSurface/SolidWorksAssemblyWalker.cs` `TryAddMateInfo` (line ~276), change the switch to add a `swMateCOINCIDENT` case before `default`:
```csharp
                MateKind kind;
                switch ((swMateType_e)mate.Type)
                {
                    case swMateType_e.swMateLOCK:       kind = MateKind.Fixed;      break;
                    case swMateType_e.swMateCONCENTRIC: kind = hasLimit ? MateKind.Revolute : MateKind.Continuous; break;
                    case swMateType_e.swMateANGLE:      kind = hasLimit ? MateKind.Revolute : MateKind.Fixed; break;
                    case swMateType_e.swMateDISTANCE:   kind = hasLimit ? MateKind.Prismatic : MateKind.Fixed; break;
                    case swMateType_e.swMateSLOT:       kind = MateKind.Prismatic;  break;
                    case swMateType_e.swMateCOINCIDENT: kind = BothEntitiesPlanarFaces(mate) ? MateKind.Planar : MateKind.Fixed; break;
                    default:                            kind = MateKind.Fixed;      break;
                }

                Vector3 axis = kind == MateKind.Fixed ? new Vector3(0, 0, 1) : MateAxisDirection(mate);
```
(The existing axis line below the switch already gives planar its normal — `MateAxisDirection` returns the entity's `EntityParams` direction, which for a planar face is the plane normal. `Floating` is never produced by the walker; it arises from an unassigned joint, Task 3.)

- [ ] **Step 3: Verify the project compiles**

Run (SolidWorks must be CLOSED so the DLL copy is not locked):
```
& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" "C:\aryan\SW2GZ\SW2GZ\SW2GZ.csproj" /p:Configuration=Release /p:SolutionDir=C:\aryan\SW2GZ\ /t:Build /v:minimal /nologo /m
```
Expected: line `SW2GZ -> ...\bin\Release\SW2GZ.dll` appears. A trailing `MSB3216 ... access denied ... HKEY_CLASSES_ROOT` (regasm, non-admin) is EXPECTED and non-fatal — the DLL still built. `MSB3027 ... locked by SolidWorks` means SolidWorks is open — close it and re-run.

- [ ] **Step 4: Commit**

```
git add SW2GZ/SwSurface/SolidWorksAssemblyWalker.cs
git commit -m @'
Walk coincident planar-face mates as Planar joints

A swMateCOINCIDENT between two planar faces now surfaces as MateKind.Planar
with the plane normal as its axis (COM path, workstation-verified).
'@
```

---

## Task 6: Full test run + CHANGELOG

Final green check across the whole suite and a changelog note for the behavior change.

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj`
Expected: all tests PASS (542 prior + the new planar/floating tests; ~548 total). Note the exact passing count from the output.

- [ ] **Step 2: Add a CHANGELOG entry**

In `CHANGELOG.md`, under the `## [2.1.0] — 2026-06-03` entry, add to the `### Added — Phase 1 wizard + robot-model export` list (or a new `### Changed` bullet group) these lines:
```markdown
- **Joint types** — added URDF `planar` (from a coincident planar-face mate, axis = plane normal) and `floating` joint types. An unassigned joint now defaults to **floating** (6-DOF) instead of fixed; assign a LOCK mate for a rigid weld. (Mimic deferred; ball is SDF-only — future gz-asset/world modes.)
```

- [ ] **Step 3: Commit**

```
git add CHANGELOG.md
git commit -m @'
Changelog: planar + floating joint types

Note the new planar/floating URDF joint types and the unassigned-joint
default change (fixed -> floating).
'@
```

- [ ] **Step 4: Push**

```
git push origin v2.1-revamp
```
Expected: push succeeds to `origin/v2.1-revamp`.

---

## Notes for the implementer

- **Behavior change to call out in review:** unassigned joints were `Fixed` in Phase 1 and are now `Floating`. This is intentional and user-approved (Task 3, CHANGELOG).
- **Not unit-tested (COM, workstation-only):** Task 5's `swMateCOINCIDENT` planar-face detection and Task 2's `WizardAssemblyWalker.ToMateKind` (both `#if SW_INTEROP`). The user validates these by exporting an assembly with a planar mate and confirming `type="planar"` in the emitted `urdf`.
- **Out of scope (do NOT implement here):** mimic (`<mimic>` + gear-mate ratio), ball joint, SDF planar/floating semantics. Ball + SDF mapping belong to Spec B (gz asset/world modes).
- **Fixed fallback for other mate types** is already implemented — `TryAddMateInfo`'s `default:` case maps any unhandled mate to `MateKind.Fixed`. No change needed.
