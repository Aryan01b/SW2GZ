# Export Modes Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Route all three export modes (Robot Package / GZ Asset / GZ World) through the real `Sw2gzPipeline`, with the two gz modes emitting standard gz Harmonic model directories instead of the dead name-only SDF path.

**Architecture:** The pipeline's front half (walk assembly → `RobotModel` with meshes/materials/joints/inertials) is shared. The write stage branches on `ExportMode`: `RobotPackage` emits the current URDF/xacro package; `SdfModel` (asset) and `SdfWorld` emit a gz model directory (`models/<pkg>/model.config` + `model.sdf` + `meshes/`) plus a mode-specific world and `.launch.py`. Asset spawns the model into an empty world at launch; World bakes an `<include>` of the model into the world SDF and just loads it.

**Tech Stack:** C# (net8.0 test project source-links pure-C# files; net48 SW addin uses COM). xUnit + Moq. Output is ROS 2 Jazzy + Gz Sim Harmonic (SDF 1.10).

**Build/verify constraints (read before starting):**
- The verification gate is `dotnet test` on `Test/SW2GZ.Writers.Test.csproj`. It compiles only the pure-C# files explicitly listed as `<Compile Include … Link=…/>` in `Test/SW2GZ.Writers.Test.csproj`. **Any NEW `.cs` file consumed by tests must be added to that list.**
- `SW2GZ/URDFExport/ExportHelper.cs`, `Sw2gzModelExporter.cs`, `Sw2gzExportPmp.cs`, `Sw2gzPipelineExportRunner.cs` are **net48 / COM-only** — NOT in the test build. They compile only via the full MSBuild SW build (needs SolidWorks interop DLLs, per `BUILD.md`). Tasks 1–5 keep `dotnet test` green; the net48 SW build is only restored at the end of **Task 6**. Verify Task 6's net48 edits by `dotnet build SW2GZ/SW2GZ.csproj` if SW DLLs are present, otherwise by careful review.
- Test command used throughout: `dotnet test Test/SW2GZ.Writers.Test.csproj`.

**Scope boundaries (do NOT exceed):**
- GZ World emits ONE model (the active assembly); structured for composition (more `<include>` + model dirs later) but no multi-assembly UI.
- Materials/color via DAE + SDF `<material>`; no texture maps.
- SDF joint type coverage: `Fixed→fixed`, `Revolute→revolute`, `Continuous→revolute` (no limit), `Prismatic→prismatic`. SDF has no planar/floating joint — `Planar`/`Floating` map to `fixed` (documented limitation; matches roadmap).
- Robot Package output is unchanged (byte-parity guarded by existing golden tests).

---

### Task 1: `SdfModelWriter` — emit real geometry from `RobotModel` (additive)

Add static `Serialize(RobotModel)`/`Write(RobotModel, dir)` methods to `SdfModelWriter`. **Keep the existing `SdfModelWriter(SdfModelInput)` instance API for now** so the net48 `ExportHelper` still compiles; it is removed in Task 6.

**Files:**
- Modify: `SW2GZ/Gz/SdfModelWriter.cs`
- Test: `Test/Writers/TestSdfModelWriter.cs` (add new cases; leave existing `SdfModelInput` cases untouched)

- [ ] **Step 1: Write the failing tests**

Add to `Test/Writers/TestSdfModelWriter.cs` (inside the existing `TestSdfModelWriter` class). These use the `RobotModel` builders already source-linked in the test project:

```csharp
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using SW2GZ.Write.Urdf; // not required, kept for parity if needed

// ---- helpers ----
// MeshData ctor is (Vector3[] Vertices, int[] Triangles, Color? MaterialColor).
private static MeshData OneTri() => new MeshData(
    new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) },
    new[] { 0, 1, 2 }, null);

private static RobotModel TwoLinkModel()
{
    var l0 = LinkBuilder.Build("base_link",
        new MassProps(2.0, Vector3.Zero, Matrix3.Identity), OneTri(), OneTri());
    var l1 = LinkBuilder.Build("arm",
        new MassProps(1.0, Vector3.Zero, Matrix3.Identity), OneTri(), OneTri());
    var links = new[]
    {
        new ModelLink(l0, "blue", null),
        new ModelLink(l1, null, null),
    };
    var joints = new[]
    {
        new UrdfJoint("shoulder", UrdfJointType.Revolute, "base_link", "arm",
            Pose.Identity, Vector3.UnitZ, -1.0, 1.0, 10.0, 2.0, UrdfCmdInterface.Position),
    };
    var mats = new[] { new MaterialDef("blue", 0.0, 0.0, 1.0, 1.0) };
    var meta = new RobotMeta("my_asset", "A", "a@b", "MIT", CoordinateConvention.Identity);
    // ControlSpec has no static Default — construct it (it is unused by the SDF writer).
    var control = new ControlSpec(new[] { "shoulder" }, ControlSpec.DefaultJointStateBroadcaster);
    return new RobotModel(meta, links, joints, mats,
        System.Array.Empty<SensorDef>(), control);
}

[Fact]
[Trait("Category", "Unit")]
public void Serialize_EmitsModelWithLinksVisualCollisionInertial()
{
    string sdf = SdfModelWriter.Serialize(TwoLinkModel());
    Assert.Contains("<sdf version=\"1.10\">", sdf);
    Assert.Contains("<model name=\"my_asset\">", sdf);
    Assert.Contains("<link name=\"base_link\">", sdf);
    Assert.Contains("<inertial>", sdf);
    Assert.Contains("<mass>2</mass>", sdf);
    Assert.Contains("<visual name=\"base_link_visual\">", sdf);
    Assert.Contains("<collision name=\"base_link_collision\">", sdf);
}

[Fact]
[Trait("Category", "Unit")]
public void Serialize_MeshUrisUseModelScheme()
{
    string sdf = SdfModelWriter.Serialize(TwoLinkModel());
    Assert.Contains("<uri>model://my_asset/meshes/base_link.dae</uri>", sdf);
    Assert.Contains("<uri>model://my_asset/meshes/base_link_collision.stl</uri>", sdf);
    Assert.DoesNotContain("package://", sdf);
}

[Fact]
[Trait("Category", "Unit")]
public void Serialize_EmitsMaterialColorWhenNamed()
{
    string sdf = SdfModelWriter.Serialize(TwoLinkModel());
    Assert.Contains("<diffuse>0 0 1 1</diffuse>", sdf);
}

[Fact]
[Trait("Category", "Unit")]
public void Serialize_EmitsSdfJointWithParentChildAxisLimit()
{
    string sdf = SdfModelWriter.Serialize(TwoLinkModel());
    Assert.Contains("<joint name=\"shoulder\" type=\"revolute\">", sdf);
    Assert.Contains("<parent>base_link</parent>", sdf);
    Assert.Contains("<child>arm</child>", sdf);
    Assert.Contains("<xyz>0 0 1</xyz>", sdf);
    Assert.Contains("<lower>-1</lower><upper>1</upper><effort>10</effort><velocity>2</velocity>", sdf);
}

[Fact]
[Trait("Category", "Unit")]
public void Write_RobotModel_WritesModelSdfFile()
{
    SdfModelWriter.Write(TwoLinkModel(), TempDir);
    Assert.True(Exists("model.sdf"));
}
```

Note: confirm `LinkBuilder.Build`, `MassProps(double, Vector3, Matrix3)`, and `ControlSpec.Default` signatures by opening those files; adjust the helper if the actual ctors differ (e.g. `ControlSpec` may have a different default accessor — use whatever `RobotModelBuilder.Build` passes). The assertion strings are the contract.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter "FullyQualifiedName~TestSdfModelWriter"`
Expected: FAIL — `SdfModelWriter` has no `Serialize`/`Write(RobotModel,…)` overload (compile error).

- [ ] **Step 3: Add the static API to `SdfModelWriter`**

Replace the body of `SW2GZ/Gz/SdfModelWriter.cs` with the following (keeps the old instance ctor + `Write(dir)` for net48 compatibility, adds the new static methods):

```csharp
/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Locked to Gz Sim Harmonic — SDF version 1.10. Emits a standard gz model
directory's model.sdf with REAL geometry (visual/collision meshes, material
color, inertial, joints) from the immutable RobotModel. Mesh URIs use the
model:// scheme so the model resolves under GZ_SIM_RESOURCE_PATH.

The legacy name-only SdfModelInput ctor is retained transiently for the
net48 ExportHelper call site and is removed once that is rerouted through
the pipeline.
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Xml.Linq;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;

namespace SW2GZ.Gz
{
    public class SdfModelWriter
    {
        private readonly SdfModelInput _input;

        public SdfModelWriter(SdfModelInput input, object profile = null)
        {
            _input = input;
        }

        // Legacy name-only emit (transitional — removed in Task 6).
        public void Write(string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            var model = new XElement("model", new XAttribute("name", _input.Name));
            foreach (var l in _input.Links)
                model.Add(new XElement("link", new XAttribute("name", l.Name)));
            foreach (var j in _input.Joints)
                model.Add(new XElement("joint",
                    new XAttribute("name", j.Name),
                    new XAttribute("type", j.Type),
                    new XElement("parent", j.Parent),
                    new XElement("child", j.Child)));
            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("sdf", new XAttribute("version", "1.10"), model));
            doc.Save(Path.Combine(outputDir, "model.sdf"));
        }

        // ── New RobotModel-based emit ──────────────────────────────────────
        public static void Write(RobotModel model, string outputDir)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(outputDir, "model.sdf"), Serialize(model));
        }

        public static string Serialize(RobotModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            string modelEsc = SecurityElement.Escape(model.Meta.PackageName);

            var mats = new Dictionary<string, MaterialDef>(StringComparer.Ordinal);
            foreach (MaterialDef m in model.Materials) mats[m.Name] = m;

            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<sdf version=\"1.10\">");
            sb.AppendLine($"  <model name=\"{modelEsc}\">");
            foreach (ModelLink ml in model.Links) AppendLink(sb, ml, modelEsc, mats);
            foreach (UrdfJoint j in model.Joints) AppendJoint(sb, j);
            sb.AppendLine("  </model>");
            sb.AppendLine("</sdf>");
            return sb.ToString();
        }

        private static void AppendLink(StringBuilder sb, ModelLink ml, string modelEsc,
                                       IReadOnlyDictionary<string, MaterialDef> mats)
        {
            UrdfLink link = ml.Link;
            string linkEsc = SecurityElement.Escape(link.Name);
            sb.AppendLine($"    <link name=\"{linkEsc}\">");

            sb.AppendLine("      <inertial>");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "        <pose>{0} {1} {2} 0 0 0</pose>",
                link.ComLocal.X, link.ComLocal.Y, link.ComLocal.Z));
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "        <mass>{0}</mass>", link.Mass));
            Matrix3 I = link.InertiaAtComLocal;
            sb.AppendLine("        <inertia>");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "          <ixx>{0}</ixx><ixy>{1}</ixy><ixz>{2}</ixz><iyy>{3}</iyy><iyz>{4}</iyz><izz>{5}</izz>",
                I.M11, I.M12, I.M13, I.M22, I.M23, I.M33));
            sb.AppendLine("        </inertia>");
            sb.AppendLine("      </inertial>");

            sb.AppendLine($"      <visual name=\"{linkEsc}_visual\">");
            sb.AppendLine("        <geometry>");
            sb.AppendLine($"          <mesh><uri>model://{modelEsc}/meshes/{SecurityElement.Escape(link.VisualMeshFile)}</uri></mesh>");
            sb.AppendLine("        </geometry>");
            if (ml.MaterialName != null && mats.TryGetValue(ml.MaterialName, out MaterialDef mat))
            {
                sb.AppendLine("        <material>");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "          <ambient>{0} {1} {2} {3}</ambient>", mat.R, mat.G, mat.B, mat.A));
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "          <diffuse>{0} {1} {2} {3}</diffuse>", mat.R, mat.G, mat.B, mat.A));
                sb.AppendLine("        </material>");
            }
            sb.AppendLine("      </visual>");

            sb.AppendLine($"      <collision name=\"{linkEsc}_collision\">");
            sb.AppendLine("        <geometry>");
            sb.AppendLine($"          <mesh><uri>model://{modelEsc}/meshes/{SecurityElement.Escape(link.CollisionMeshFile)}</uri></mesh>");
            sb.AppendLine("        </geometry>");
            sb.AppendLine("      </collision>");

            sb.AppendLine("    </link>");
        }

        private static void AppendJoint(StringBuilder sb, UrdfJoint j)
        {
            string nameEsc = SecurityElement.Escape(j.Name);
            string typeStr = JointTypeString(j.Type);
            string parentEsc = SecurityElement.Escape(j.ParentLink);
            sb.AppendLine($"    <joint name=\"{nameEsc}\" type=\"{typeStr}\">");
            sb.AppendLine($"      <parent>{parentEsc}</parent>");
            sb.AppendLine($"      <child>{SecurityElement.Escape(j.ChildLink)}</child>");

            var pos = j.Origin.Position;
            var (roll, pitch, yaw) = Matrix3.FromQuaternion(j.Origin.Rotation).ToRpy();
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "      <pose relative_to=\"{0}\">{1} {2} {3} {4} {5} {6}</pose>",
                parentEsc, pos.X, pos.Y, pos.Z, roll, pitch, yaw));

            if (j.Type == UrdfJointType.Revolute || j.Type == UrdfJointType.Continuous
                || j.Type == UrdfJointType.Prismatic)
            {
                sb.AppendLine("      <axis>");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "        <xyz>{0} {1} {2}</xyz>", j.Axis.X, j.Axis.Y, j.Axis.Z));
                if (j.Type == UrdfJointType.Revolute || j.Type == UrdfJointType.Prismatic)
                {
                    sb.AppendLine("        <limit>");
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "          <lower>{0}</lower><upper>{1}</upper><effort>{2}</effort><velocity>{3}</velocity>",
                        j.LimitLower ?? 0.0, j.LimitUpper ?? 0.0, j.LimitEffort, j.LimitVelocity));
                    sb.AppendLine("        </limit>");
                }
                sb.AppendLine("      </axis>");
            }

            sb.AppendLine("    </joint>");
        }

        // SDF lacks continuous/planar/floating: continuous→revolute (no limit),
        // planar/floating→fixed (documented limitation).
        private static string JointTypeString(UrdfJointType t) => t switch
        {
            UrdfJointType.Fixed      => "fixed",
            UrdfJointType.Revolute   => "revolute",
            UrdfJointType.Continuous => "revolute",
            UrdfJointType.Prismatic  => "prismatic",
            UrdfJointType.Planar     => "fixed",
            UrdfJointType.Floating   => "fixed",
            _ => throw new InvalidOperationException($"Unhandled UrdfJointType: {t}"),
        };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter "FullyQualifiedName~TestSdfModelWriter"`
Expected: PASS (both new cases and the original `SdfModelInput` cases).

- [ ] **Step 5: Commit**

```bash
git add SW2GZ/Gz/SdfModelWriter.cs Test/Writers/TestSdfModelWriter.cs
git commit -m "Emit real geometry in SdfModelWriter from RobotModel"
```

---

### Task 2: `SdfWorldWriter.WriteWithModel` — world with an `<include>` of the model

**Files:**
- Modify: `SW2GZ/Gz/SdfWorldWriter.cs`
- Test: `Test/Writers/TestSdfWorldWriter.cs`

- [ ] **Step 1: Write the failing tests**

Add to `Test/Writers/TestSdfWorldWriter.cs`:

```csharp
[Fact]
public void WriteWithModel_IncludesModelByUri()
{
    var sdf = SdfWorldWriter.WriteWithModel(new SdfWorldInput("my_world"), "my_asset");
    Assert.Contains("<world name=\"my_world\">", sdf);
    Assert.Contains("<include>", sdf);
    Assert.Contains("<uri>model://my_asset</uri>", sdf);
    Assert.Contains("<name>my_asset</name>", sdf);
}

[Fact]
public void WriteWithModel_KeepsGroundSunPhysics()
{
    var sdf = SdfWorldWriter.WriteWithModel(new SdfWorldInput("w"), "m");
    Assert.Contains("ground_plane", sdf);
    Assert.Contains("<light", sdf);
    Assert.Contains("<physics", sdf);
}

[Fact]
public void WriteWithModel_NullModelName_Throws()
{
    Assert.Throws<System.ArgumentException>(
        () => SdfWorldWriter.WriteWithModel(new SdfWorldInput("w"), "  "));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter "FullyQualifiedName~TestSdfWorldWriter"`
Expected: FAIL — `WriteWithModel` not defined.

- [ ] **Step 3: Add `WriteWithModel` to `SdfWorldWriter`**

In `SW2GZ/Gz/SdfWorldWriter.cs`, refactor so the world body is built once and the include variant inserts an `<include>` before `</world>`. Add a `using System.Security;` is already present. Add:

```csharp
public static string WriteWithModel(SdfWorldInput input, string modelName)
{
    if (input == null) throw new ArgumentNullException(nameof(input));
    if (string.IsNullOrWhiteSpace(input.WorldName))
        throw new ArgumentException("WorldName must not be null or whitespace.", nameof(input));
    if (string.IsNullOrWhiteSpace(modelName))
        throw new ArgumentException("modelName must not be null or whitespace.", nameof(modelName));

    string baseWorld = Write(input);                  // ground + sun + physics, no model
    string nameEsc = SecurityElement.Escape(modelName);
    string include =
        "    <include>\n" +
        $"      <uri>model://{nameEsc}</uri>\n" +
        $"      <name>{nameEsc}</name>\n" +
        "    </include>\n";
    // Splice the include immediately before the closing </world>.
    return baseWorld.Replace("  </world>", include + "  </world>");
}
```

(The existing `Write(SdfWorldInput)` overload emits `"  </world>"` on its own line — the `Replace` targets that exact token.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter "FullyQualifiedName~TestSdfWorldWriter"`
Expected: PASS (new cases + all existing world tests).

- [ ] **Step 5: Commit**

```bash
git add SW2GZ/Gz/SdfWorldWriter.cs Test/Writers/TestSdfWorldWriter.cs
git commit -m "Add SdfWorldWriter.WriteWithModel for composed worlds"
```

---

### Task 3: `LaunchPyWriter.GzAsset` / `GzWorld` launches

**Files:**
- Modify: `SW2GZ/Ros2/LaunchPyWriter.cs`
- Test: `Test/Writers/TestLaunchPyWriter.cs`

- [ ] **Step 1: Write the failing tests**

Add to `Test/Writers/TestLaunchPyWriter.cs`:

```csharp
[Fact]
public void GzAsset_SetsResourcePathAndSpawnsModelFile()
{
    string py = LaunchPyWriter.GzAsset("my_asset");
    Assert.Contains("GZ_SIM_RESOURCE_PATH", py);
    Assert.Contains("'models'", py);                 // resource path = <share>/models
    Assert.Contains("'empty.sdf'", py);              // empty world
    Assert.Contains("ros_gz_sim", py);
    Assert.Contains("'create'", py);                 // spawn the model
    Assert.Contains("'model.sdf'", py);              // spawn from the model.sdf file
    Assert.DoesNotContain("gz_ros2_control", py);
    Assert.DoesNotContain("controller_manager", py);
}

[Fact]
public void GzWorld_SetsResourcePathAndLoadsWorld()
{
    string py = LaunchPyWriter.GzWorld("my_world_pkg", "my_world_pkg");
    Assert.Contains("GZ_SIM_RESOURCE_PATH", py);
    Assert.Contains("'models'", py);
    Assert.Contains("my_world_pkg.sdf", py);         // loads the composed world
    Assert.Contains("ros_gz_sim", py);
    Assert.DoesNotContain("'create'", py);           // no spawn — model is in the world
    Assert.DoesNotContain("gz_ros2_control", py);
}

[Fact]
public void GzAsset_NullPkg_Throws()
{
    Assert.Throws<System.ArgumentException>(() => LaunchPyWriter.GzAsset("  "));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter "FullyQualifiedName~TestLaunchPyWriter"`
Expected: FAIL — `GzAsset`/`GzWorld` not defined.

- [ ] **Step 3: Add `GzAsset` and `GzWorld` to `LaunchPyWriter`**

In `SW2GZ/Ros2/LaunchPyWriter.cs`, add these two methods (and in Task 5 the now-dead `GzSimModelOnly` is deleted):

```csharp
// GZ Asset: start Gz Harmonic with the empty world, then spawn the gz model
// (models/<pkg>/model.sdf) into it. No ros2_control, no bridge.
public static string GzAsset(string packageName)
{
    Guard(packageName);
    return $@"# Auto-generated by SW2GZ (gz asset).
import os
from ament_index_python.packages import get_package_share_directory
from launch import LaunchDescription
from launch.actions import IncludeLaunchDescription, SetEnvironmentVariable
from launch.launch_description_sources import PythonLaunchDescriptionSource
from launch_ros.actions import Node

def generate_launch_description():
    pkg_share = get_package_share_directory('{packageName}')
    models_dir = os.path.join(pkg_share, 'models')
    world_path = os.path.join(pkg_share, 'worlds', 'empty.sdf')
    model_sdf = os.path.join(models_dir, '{packageName}', 'model.sdf')

    set_resource_path = SetEnvironmentVariable(
        name='GZ_SIM_RESOURCE_PATH', value=models_dir)

    gz_sim = IncludeLaunchDescription(
        PythonLaunchDescriptionSource(os.path.join(
            get_package_share_directory('ros_gz_sim'), 'launch', 'gz_sim.launch.py')),
        launch_arguments=[('gz_args', '-r ' + world_path)])

    spawn = Node(package='ros_gz_sim', executable='create',
                 arguments=['-name', '{packageName}', '-file', model_sdf],
                 output='screen')

    return LaunchDescription([set_resource_path, gz_sim, spawn])
";
}

// GZ World: the model is already <include>d in the world, so just load it.
public static string GzWorld(string packageName, string worldName)
{
    Guard(packageName);
    if (string.IsNullOrWhiteSpace(worldName))
        throw new ArgumentException("worldName must not be null or whitespace.", nameof(worldName));
    return $@"# Auto-generated by SW2GZ (gz world).
import os
from ament_index_python.packages import get_package_share_directory
from launch import LaunchDescription
from launch.actions import IncludeLaunchDescription, SetEnvironmentVariable
from launch.launch_description_sources import PythonLaunchDescriptionSource

def generate_launch_description():
    pkg_share = get_package_share_directory('{packageName}')
    models_dir = os.path.join(pkg_share, 'models')
    world_path = os.path.join(pkg_share, 'worlds', '{worldName}.sdf')

    set_resource_path = SetEnvironmentVariable(
        name='GZ_SIM_RESOURCE_PATH', value=models_dir)

    gz_sim = IncludeLaunchDescription(
        PythonLaunchDescriptionSource(os.path.join(
            get_package_share_directory('ros_gz_sim'), 'launch', 'gz_sim.launch.py')),
        launch_arguments=[('gz_args', '-r ' + world_path)])

    return LaunchDescription([set_resource_path, gz_sim])
";
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter "FullyQualifiedName~TestLaunchPyWriter"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SW2GZ/Ros2/LaunchPyWriter.cs Test/Writers/TestLaunchPyWriter.cs
git commit -m "Add GzAsset/GzWorld launch writers"
```

---

### Task 4: Mode-aware `AmentCMakeInput` and `PackageXmlInput`

Make the package metadata writers emit the right install dirs and lean deps for gz modes, keeping all existing tests green via defaults.

**Files:**
- Modify: `SW2GZ/Ros2/AmentCMakeWriter.cs`
- Modify: `SW2GZ/Ros2/PackageXmlV3Writer.cs`
- Test: `Test/Writers/TestAmentCMakeWriter.cs`, `Test/Writers/TestPackageXmlV3Writer.cs`

- [ ] **Step 1: Write the failing tests**

Add to `Test/Writers/TestAmentCMakeWriter.cs`:

```csharp
[Fact]
public void Write_GzMode_InstallsModelsNotUrdfOrConfig()
{
    var cmake = AmentCMakeWriter.Write(new AmentCMakeInput(
        "asset_pkg", hasMeshes: false, hasModels: true, hasUrdf: false, hasConfig: false));
    Assert.Contains("models", cmake);
    Assert.Contains("launch", cmake);
    Assert.Contains("worlds", cmake);
    Assert.DoesNotContain("urdf", cmake);
    Assert.DoesNotContain("config", cmake);
}
```

Add to `Test/Writers/TestPackageXmlV3Writer.cs`:

```csharp
[Fact]
public void Write_GzMode_EmitsLeanDeps()
{
    var xml = PackageXmlV3Writer.Write(new PackageXmlInput(
        "asset_pkg", "0.0.1", "d", "m", "e@x", "MIT") { GzMode = true });
    Assert.Contains("<exec_depend>ros_gz_sim</exec_depend>", xml);
    Assert.DoesNotContain("ros2_control", xml);
    Assert.DoesNotContain("gz_ros2_control", xml);
    Assert.DoesNotContain("ros_gz_bridge", xml);
}
```

NOTE: `PackageXmlInput` is a positional record; `GzMode` must be added as an init-only property (see Step 3) so the `{ GzMode = true }` object-initializer compiles.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter "FullyQualifiedName~TestAmentCMakeWriter|FullyQualifiedName~TestPackageXmlV3Writer"`
Expected: FAIL — `hasModels`/`hasUrdf`/`hasConfig` and `GzMode` don't exist.

- [ ] **Step 3: Extend the writers**

In `SW2GZ/Ros2/AmentCMakeWriter.cs`, replace the record + dir logic:

```csharp
public sealed record AmentCMakeInput(
    string PackageName, bool hasMeshes,
    bool hasModels = false, bool hasUrdf = true, bool hasConfig = true);

public static class AmentCMakeWriter
{
    public static string Write(AmentCMakeInput input)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        if (string.IsNullOrWhiteSpace(input.PackageName))
            throw new ArgumentException("PackageName must not be null or whitespace.", nameof(input));

        var dirs = new List<string> { "launch", "worlds" };
        if (input.hasUrdf) dirs.Insert(0, "urdf");
        if (input.hasConfig) dirs.Add("config");
        if (input.hasMeshes) dirs.Add("meshes");
        if (input.hasModels) dirs.Add("models");

        var sb = new StringBuilder();
        sb.AppendLine("cmake_minimum_required(VERSION 3.8)");
        sb.AppendLine($"project({input.PackageName})");
        sb.AppendLine();
        sb.AppendLine("find_package(ament_cmake REQUIRED)");
        sb.AppendLine();
        sb.AppendLine($"install(DIRECTORY {string.Join(" ", dirs)}");
        sb.AppendLine("  DESTINATION share/${PROJECT_NAME}");
        sb.AppendLine(")");
        sb.AppendLine();
        sb.AppendLine("ament_package()");
        return sb.ToString();
    }
}
```

(Existing tests pass: defaults `hasUrdf=true,hasConfig=true` ⇒ urdf/launch/config/worlds present; `hasMeshes:false` ⇒ no `meshes`.)

In `SW2GZ/Ros2/PackageXmlV3Writer.cs`, add `GzMode` and branch the deps:

```csharp
public sealed record PackageXmlInput(
    string Name, string Version, string Description,
    string Maintainer, string Email, string License)
{
    public bool GzMode { get; init; } = false;
}
```

Then in `Write`, build the dependency elements conditionally. Replace the single `new XElement("package", …)` deps list so that when `input.GzMode` is true only `ros_gz_sim` (plus `ament_cmake` buildtool) is emitted:

```csharp
var deps = new List<XElement> { new XElement("buildtool_depend", "ament_cmake") };
if (input.GzMode)
{
    deps.Add(new XElement("exec_depend", "ros_gz_sim"));
}
else
{
    deps.Add(new XElement("exec_depend", "robot_state_publisher"));
    deps.Add(new XElement("exec_depend", "joint_state_publisher_gui"));
    deps.Add(new XElement("exec_depend", "xacro"));
    deps.Add(new XElement("exec_depend", "rviz2"));
    deps.Add(new XElement("exec_depend", "ros_gz_sim"));
    deps.Add(new XElement("exec_depend", "ros_gz_bridge"));
    deps.Add(new XElement("exec_depend", "gz_ros2_control"));
    deps.Add(new XElement("exec_depend", "ros2_control"));
    deps.Add(new XElement("exec_depend", "ros2_controllers"));
}

var doc = new XDocument(
    new XDeclaration("1.0", "utf-8", null),
    new XElement("package",
        new XAttribute("format", "3"),
        new XElement("name", input.Name),
        new XElement("version", input.Version ?? "0.0.1"),
        new XElement("description", input.Description ?? "Auto-generated by SW2GZ"),
        new XElement("maintainer",
            new XAttribute("email", input.Email ?? "TODO@example.com"),
            input.Maintainer ?? "TODO"),
        new XElement("license", input.License ?? "Apache-2.0"),
        deps,
        new XElement("export",
            new XElement("build_type", "ament_cmake"),
            new XElement("architecture_independent"))));
return doc.Declaration + System.Environment.NewLine + doc.ToString();
```

(Add `using System.Collections.Generic;` if not present.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter "FullyQualifiedName~TestAmentCMakeWriter|FullyQualifiedName~TestPackageXmlV3Writer"`
Expected: PASS (new + all existing).

- [ ] **Step 5: Commit**

```bash
git add SW2GZ/Ros2/AmentCMakeWriter.cs SW2GZ/Ros2/PackageXmlV3Writer.cs Test/Writers/TestAmentCMakeWriter.cs Test/Writers/TestPackageXmlV3Writer.cs
git commit -m "Make CMake/package.xml writers mode-aware for gz modes"
```

---

### Task 5: `Sw2gzPipeline.Run` — branch the write stage by `ExportMode`

Replace the `bool modelOnly` param with `ExportMode mode`; emit the gz model directory + mode-specific world + launch for the two gz modes. Delete the now-dead `XacroWriter.WriteModelOnly` and `LaunchPyWriter.GzSimModelOnly`.

**Files:**
- Modify: `SW2GZ/URDFExport/Sw2gzPipeline.cs`
- Modify: `SW2GZ/Ros2/XacroWriter.cs` (delete `WriteModelOnly`)
- Modify: `SW2GZ/Ros2/LaunchPyWriter.cs` (delete `GzSimModelOnly`)
- Test: `Test/Integration/Sw2gzPipelineTests.cs`

- [ ] **Step 1: Update the existing model-only test and add a world test**

In `Test/Integration/Sw2gzPipelineTests.cs`, find the test whose body calls
`.Run(tmp, "model_pkg", "A", "a@b", "MIT", Array.Empty<…SensorDef>(), modelOnly: true)`
and replace that whole test method with the two below (keep the surrounding `mass`/`walker`/`tess` mock setup — copy it from the existing method):

```csharp
[Fact]
public void Run_SdfModel_EmitsGzModelDirEmptyWorldSpawnLaunch()
{
    var mass = new Mock<IMassProperties>();
    mass.Setup(m => m.Get(It.IsAny<string>()))
        .Returns(new MassProps(1.0, Vector3.Zero, Matrix3.Identity));
    var walker = new Mock<IAssemblyWalker>();
    walker.Setup(w => w.WalkActive()).Returns(new[]
    {
        new LinkSpec("base_link", new[] { "/p/base.SLDPRT" }),
        new LinkSpec("arm1",      new[] { "/p/arm1.SLDPRT" }),
    });
    walker.Setup(w => w.WalkMates()).Returns(new[]
    {
        new MateSpec("shoulder", MateKind.Revolute, Pose.Identity, Vector3.UnitZ,
            -1.0, 1.0, 0, 0, UrdfCmdInterface.Position, "base_link", "arm1"),
    });
    var tess = new Mock<IMeshTessellator>();
    tess.Setup(t => t.Tessellate(It.IsAny<string>(), It.IsAny<TessellationLod>())).Returns(TinyMesh());

    var tmp = Path.Combine(Path.GetTempPath(), "sw2gz_asset_" + Guid.NewGuid());
    try
    {
        var report = new Sw2gzPipeline(mass.Object, walker.Object, tess.Object)
            .Run(tmp, "model_pkg", "A", "a@b", "MIT",
                 Array.Empty<SW2GZ.Build.Model.SensorDef>(), SW2GZ.Ros2.ExportMode.SdfModel);
        Assert.False(report.HasErrors,
            string.Join("; ", System.Linq.Enumerable.Select(report.Errors, e => e.Code + " " + e.Message)));

        string root = Path.Combine(tmp, "model_pkg_ws", "src", "model_pkg");
        Assert.True(File.Exists(Path.Combine(root, "models", "model_pkg", "model.config")));
        Assert.True(File.Exists(Path.Combine(root, "models", "model_pkg", "model.sdf")));
        Assert.True(File.Exists(Path.Combine(root, "models", "model_pkg", "meshes", "base_link.dae")));
        Assert.True(File.Exists(Path.Combine(root, "worlds", "empty.sdf")));
        Assert.True(File.Exists(Path.Combine(root, "launch", "model_pkg.launch.py")));

        // gz model carries real geometry + joint.
        string sdf = File.ReadAllText(Path.Combine(root, "models", "model_pkg", "model.sdf"));
        Assert.Contains("<model name=\"model_pkg\">", sdf);
        Assert.Contains("model://model_pkg/meshes/base_link.dae", sdf);
        Assert.Contains("<joint name=\"shoulder\" type=\"revolute\">", sdf);

        // Absent: URDF/control scaffolding.
        Assert.False(Directory.Exists(Path.Combine(root, "urdf")));
        Assert.False(File.Exists(Path.Combine(root, "config", "controllers.yaml")));
        Assert.False(File.Exists(Path.Combine(root, "config", "ros_gz_bridge.yaml")));

        string launch = File.ReadAllText(Path.Combine(root, "launch", "model_pkg.launch.py"));
        Assert.Contains("'create'", launch);
        Assert.DoesNotContain("gz_ros2_control", launch);
    }
    finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
}

[Fact]
public void Run_SdfWorld_EmitsWorldThatIncludesModel()
{
    var mass = new Mock<IMassProperties>();
    mass.Setup(m => m.Get(It.IsAny<string>()))
        .Returns(new MassProps(1.0, Vector3.Zero, Matrix3.Identity));
    var walker = new Mock<IAssemblyWalker>();
    walker.Setup(w => w.WalkActive()).Returns(new[] { new LinkSpec("base_link", new[] { "/p/base.SLDPRT" }) });
    walker.Setup(w => w.WalkMates()).Returns(Array.Empty<MateSpec>());
    var tess = new Mock<IMeshTessellator>();
    tess.Setup(t => t.Tessellate(It.IsAny<string>(), It.IsAny<TessellationLod>())).Returns(TinyMesh());

    var tmp = Path.Combine(Path.GetTempPath(), "sw2gz_world_" + Guid.NewGuid());
    try
    {
        var report = new Sw2gzPipeline(mass.Object, walker.Object, tess.Object)
            .Run(tmp, "world_pkg", "A", "a@b", "MIT",
                 Array.Empty<SW2GZ.Build.Model.SensorDef>(), SW2GZ.Ros2.ExportMode.SdfWorld);
        Assert.False(report.HasErrors);

        string root = Path.Combine(tmp, "world_pkg_ws", "src", "world_pkg");
        Assert.True(File.Exists(Path.Combine(root, "models", "world_pkg", "model.sdf")));
        Assert.True(File.Exists(Path.Combine(root, "worlds", "world_pkg.sdf")));
        Assert.True(File.Exists(Path.Combine(root, "launch", "world_pkg.launch.py")));

        string world = File.ReadAllText(Path.Combine(root, "worlds", "world_pkg.sdf"));
        Assert.Contains("<include>", world);
        Assert.Contains("<uri>model://world_pkg</uri>", world);

        string launch = File.ReadAllText(Path.Combine(root, "launch", "world_pkg.launch.py"));
        Assert.Contains("world_pkg.sdf", launch);
        Assert.DoesNotContain("'create'", launch);
    }
    finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
}
```

(Confirm the `MassProps` ctor + `MateSpec` ctor arg order against the existing test file you copied from — reuse exactly what that file already uses; the snippet above mirrors the existing `modelOnly` test's setup.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj --filter "FullyQualifiedName~Sw2gzPipelineTests"`
Expected: FAIL — `Run(..., ExportMode)` overload doesn't exist (compile error).

- [ ] **Step 3: Rewrite the write stage in `Sw2gzPipeline.Run`**

In `SW2GZ/URDFExport/Sw2gzPipeline.cs`:

(a) Change the signature (line ~71-73) and the 5-arg delegator (line ~62-64):

```csharp
public SW2GZ.Validate.ValidationReport Run(string outputDir, string packageName,
                            string author, string email, string license) =>
    Run(outputDir, packageName, author, email, license,
        System.Array.Empty<SensorDef>(), Ros2.ExportMode.RobotPackage);

public SW2GZ.Validate.ValidationReport Run(string outputDir, string packageName,
                            string author, string email, string license,
                            IReadOnlyList<SensorDef> sensors,
                            Ros2.ExportMode mode = Ros2.ExportMode.RobotPackage)
{
```

(b) Replace the directory-creation + mesh-write + file-emit block (the `try { … }` body from `Directory.CreateDirectory(root)` down to the `OutputValidator` call) with mode-aware logic:

```csharp
string workspaceDir = Path.Combine(outputDir, $"{pkg}_ws");
bool createdWorkspace = !Directory.Exists(workspaceDir);
string srcDir = Path.Combine(workspaceDir, "src");
string root = Path.Combine(srcDir, pkg);
bool gz = mode != Ros2.ExportMode.RobotPackage;
try
{
    Directory.CreateDirectory(root);

    string meshesDir;
    if (gz)
    {
        Directory.CreateDirectory(Path.Combine(root, "worlds"));
        Directory.CreateDirectory(Path.Combine(root, "launch"));
        string modelDir = Path.Combine(root, "models", pkg);
        meshesDir = Path.Combine(modelDir, "meshes");
        Directory.CreateDirectory(meshesDir);
    }
    else
    {
        foreach (string subdir in new[] { "urdf", "urdf/inc", "worlds", "launch", "config", "meshes" })
            Directory.CreateDirectory(Path.Combine(root, subdir));
        meshesDir = Path.Combine(root, "meshes");
    }

    foreach (UrdfLink link in links)
    {
        DaeWriter.Write(link.VisualMesh,    Path.Combine(meshesDir, link.VisualMeshFile));
        StlWriter.Write(link.CollisionMesh, Path.Combine(meshesDir, link.CollisionMeshFile));
    }

    // package.xml + CMakeLists (mode-aware)
    File.WriteAllText(Path.Combine(root, "package.xml"),
        PackageXmlV3Writer.Write(new PackageXmlInput(pkg, "0.1.0",
            "Auto-generated by SW2GZ", author, email, license) { GzMode = gz }));

    File.WriteAllText(Path.Combine(root, "CMakeLists.txt"),
        AmentCMakeWriter.Write(gz
            ? new AmentCMakeInput(pkg, hasMeshes: false, hasModels: true, hasUrdf: false, hasConfig: false)
            : new AmentCMakeInput(pkg, hasMeshes: true)));

    if (gz)
    {
        string modelDir = Path.Combine(root, "models", pkg);
        new ModelConfigWriter(new ModelConfigWriter.Input
        {
            Name = pkg, Author = author, Email = email,
        }).Write(modelDir);

        SdfModelWriter.Write(model, modelDir); // model.sdf with real geometry

        if (mode == Ros2.ExportMode.SdfModel)
        {
            File.WriteAllText(Path.Combine(root, "worlds", "empty.sdf"),
                SdfWorldWriter.Write(new SdfWorldInput("empty"), model.Sensors));
            File.WriteAllText(Path.Combine(root, "launch", $"{pkg}.launch.py"),
                LaunchPyWriter.GzAsset(pkg));
        }
        else // SdfWorld
        {
            File.WriteAllText(Path.Combine(root, "worlds", $"{pkg}.sdf"),
                SdfWorldWriter.WriteWithModel(new SdfWorldInput(pkg), pkg));
            File.WriteAllText(Path.Combine(root, "launch", $"{pkg}.launch.py"),
                LaunchPyWriter.GzWorld(pkg, pkg));
        }
    }
    else
    {
        string bodyXml = UrdfSerializer.SerializeBody(model);

        File.WriteAllText(Path.Combine(root, "urdf", $"{pkg}.urdf.xacro"),
            XacroWriter.Write(pkg, bodyXml));
        File.WriteAllText(Path.Combine(root, "urdf", "inc", "materials.xacro"),
            UrdfSerializer.SerializeMaterialsXacro(model.Materials));

        var jointNames = new List<string>();
        foreach (UrdfJoint j in joints) jointNames.Add(j.Name);

        File.WriteAllText(Path.Combine(root, "urdf", "inc", "ros2_control.xacro"),
            Ros2ControlWriter.Write(pkg, jointNames));
        File.WriteAllText(Path.Combine(root, "urdf", "inc", "gz.xacro"),
            GzPluginTags.WriteGzRos2ControlXacro(pkg));

        File.WriteAllText(Path.Combine(root, "worlds", "empty.sdf"),
            SdfWorldWriter.Write(new SdfWorldInput("empty"), model.Sensors));

        string launchDir = Path.Combine(root, "launch");
        File.WriteAllText(Path.Combine(launchDir, "display.launch.py"), LaunchPyWriter.Display(pkg));
        File.WriteAllText(Path.Combine(launchDir, "gz_sim.launch.py"), LaunchPyWriter.GzSim(pkg));
        File.WriteAllText(Path.Combine(launchDir, "ros2_control.launch.py"), LaunchPyWriter.Ros2Control(pkg));

        File.WriteAllText(Path.Combine(root, "config", "controllers.yaml"),
            ControllersYaml.Write(new ControllersInput(pkg, jointNames)));
        File.WriteAllText(Path.Combine(root, "config", "ros_gz_bridge.yaml"),
            RosGzBridgeYaml.Write(pkg, model.Sensors));
    }

    SW2GZ.Validate.ValidationReport postWrite =
        new SW2GZ.Validate.OutputValidator().Run(root, pkg);
    return new SW2GZ.Validate.ValidationReport(
        preWrite.Warnings.Concat(jointIssues).Concat(postWrite.Issues).ToList());
}
catch
{
    if (createdWorkspace && Directory.Exists(workspaceDir))
    {
        try { Directory.Delete(workspaceDir, recursive: true); }
        catch { }
    }
    throw;
}
```

Update the doc-comment above the method to describe the `mode` param instead of `modelOnly`.

- [ ] **Step 4: Delete the now-dead model-only helpers**

In `SW2GZ/Ros2/XacroWriter.cs`, delete the entire `public static string WriteModelOnly(...)` method.
In `SW2GZ/Ros2/LaunchPyWriter.cs`, delete the entire `public static string GzSimModelOnly(...)` method.
(Confirm no remaining references: `grep -rn "WriteModelOnly\|GzSimModelOnly" SW2GZ Test` should return nothing after Task 6 edits; at this point only the pipeline referenced them and it no longer does.)

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj`
Expected: PASS — the two new pipeline tests plus the full existing suite (RobotPackage golden/integration tests prove byte-parity is intact).

- [ ] **Step 6: Commit**

```bash
git add SW2GZ/URDFExport/Sw2gzPipeline.cs SW2GZ/Ros2/XacroWriter.cs SW2GZ/Ros2/LaunchPyWriter.cs Test/Integration/Sw2gzPipelineTests.cs
git commit -m "Branch Sw2gzPipeline write stage by ExportMode"
```

---

### Task 6: Wire net48 callers to the mode-driven pipeline; remove dead SDF path

Route `ExportHelper` and the ribbon/wizard callers through `pipeline.Run(..., mode)`, and delete the obsolete name-only `SdfModelInput` + `SdfModelWriter(SdfModelInput)` instance API.

> **Behavior change to flag in the commit/PR:** the ribbon "export model" path (`Sw2gzModelExporter`, `Sw2gzExportPmp`), previously a URDF model-only package via `modelOnly: true`, now produces a **GZ Asset** (`ExportMode.SdfModel`) gz model package. This is the intended consolidation onto the three modes.

**Files:**
- Modify: `SW2GZ/URDFExport/ExportHelper.cs`
- Modify: `SW2GZ/URDFExport/Sw2gzModelExporter.cs`
- Modify: `SW2GZ/URDFExport/Sw2gzExportPmp.cs`
- Modify: `SW2GZ/UI/Services/Sw/Sw2gzPipelineExportRunner.cs`
- Modify: `SW2GZ/Gz/SdfModelWriter.cs` (remove legacy instance API)
- Delete: `SW2GZ/Gz/SdfModelInput.cs`
- Modify: `SW2GZ/SW2GZ.csproj` (remove `SdfModelInput.cs` compile entry)
- Modify: `Test/SW2GZ.Writers.Test.csproj` (remove `SdfModelInput.cs` link entry)
- Modify: `Test/Writers/TestSdfModelWriter.cs` (drop the two `SdfModelInput`-based cases)

- [ ] **Step 1: Reroute `ExportHelper.ExportRobot`**

In `SW2GZ/URDFExport/ExportHelper.cs`, replace the `#if SW_INTEROP` RobotPackage-only pipeline branch (lines ~171-199) so it runs for **all** modes, and delete the legacy `switch (Profile.Mode)` block (lines ~209-238). The new branch:

```csharp
#if SW_INTEROP
                if (Profile != null)
                {
                    var pipeline = new Sw2gzPipeline(
                        new SolidWorksMassProperties((SldWorks)iSwApp, (AssemblyDoc)ActiveSWModel),
                        new SolidWorksAssemblyWalker((AssemblyDoc)ActiveSWModel),
                        new SolidWorksMeshTessellator((SldWorks)iSwApp, (AssemblyDoc)ActiveSWModel));

                    string pkgName = PackageName ?? ActiveSWModel?.GetTitle() ?? "robot";
                    var report = pipeline.Run(SavePath, pkgName,
                                              Profile_Author, Profile_Email, Profile_License,
                                              System.Array.Empty<SensorDef>(), Profile.Mode);

                    if (report.HasErrors)
                    {
                        var msg = string.Join("\n", report.Errors.Select(e => $"{e.Code}: {e.Message} ({e.Location})"));
                        System.Windows.Forms.MessageBox.Show("SW2GZ export validation failed:\n\n" + msg,
                            "SW2GZ Export", System.Windows.Forms.MessageBoxButtons.OK,
                            System.Windows.Forms.MessageBoxIcon.Warning);
                    }

                    logger.Info("SW2GZ pipeline export complete. Output: " + SavePath);
                    return;
                }
#endif
```

Then delete the now-unreachable legacy body: the `if (URDFRobot == null) …`, `string outDir = …`, `List<string> jointNames = …`, and the entire `switch (Profile.Mode) { … }` (cases RobotPackage/SdfModel/SdfWorld), down to `logger.Info("SW2GZ export complete. Output: " + outDir);`. Also delete the now-unused private helper `BuildSdfModelInput()` (lines ~285-313) and `BuildUrdfBodyXml()` if it has no other callers (`grep -n "BuildUrdfBodyXml" SW2GZ/URDFExport/ExportHelper.cs` — if only its own definition remains, delete it). Remove now-unused `using SW2GZ.Gz;` only if nothing else in the file needs it (check first).

Add `using SW2GZ.Build.Model;` if `SensorDef` is not already in scope.

- [ ] **Step 2: Reroute the ribbon + wizard callers**

`SW2GZ/URDFExport/Sw2gzModelExporter.cs` — change the `Run` call:

```csharp
return new Sw2gzPipeline(mass, walker, tess, appearances).Run(
    config.OutputFolder, config.PackageName, config.Author, config.Email, config.License,
    System.Array.Empty<SensorDef>(), SW2GZ.Ros2.ExportMode.SdfModel);
```

`SW2GZ/URDFExport/Sw2gzExportPmp.cs` (~line 1124) — same change:

```csharp
SW2GZ.Validate.ValidationReport report =
    new Sw2gzPipeline(mass, walker, tess, appearances).Run(
        config.OutputFolder, config.PackageName, config.Author, config.Email, config.License,
        System.Array.Empty<SensorDef>(), SW2GZ.Ros2.ExportMode.SdfModel);
```

`SW2GZ/UI/Services/Sw/Sw2gzPipelineExportRunner.cs` (~line 71) — thread the `mode` arg through:

```csharp
SW2GZ.Validate.ValidationReport report = pipeline.Run(
    outputDir, meta.PackageName, meta.Author, meta.Email, meta.License,
    model.Sensors, mode);
```

- [ ] **Step 3: Remove the legacy `SdfModelInput` API**

In `SW2GZ/Gz/SdfModelWriter.cs`, delete the `private readonly SdfModelInput _input;` field, the `SdfModelWriter(SdfModelInput …)` ctor, and the legacy instance `Write(string outputDir)` method. The class becomes a static helper holding only `Serialize`/`Write(RobotModel,…)` + private helpers. Change `public class SdfModelWriter` → `public static class SdfModelWriter`. Remove the now-unused `using System.Xml.Linq;`.

Delete `SW2GZ/Gz/SdfModelInput.cs`.

Remove from `SW2GZ/SW2GZ.csproj` the line:
`<Compile Include="Gz\SdfModelInput.cs" />`

Remove from `Test/SW2GZ.Writers.Test.csproj` the line:
`<Compile Include="..\SW2GZ\Gz\SdfModelInput.cs"          Link="Sources\Gz\SdfModelInput.cs" />`

In `Test/Writers/TestSdfModelWriter.cs`, delete the two original cases that build `SdfModelInput` (`WritesModelSdfWithMatchingSdfVersionAndModelName`, `EmitsJointsWithParentChild`) and remove the now-unused `using System.Collections.Generic;` if nothing else needs it. Keep the Task 1 `RobotModel`-based cases.

- [ ] **Step 4: Verify**

Run: `dotnet test Test/SW2GZ.Writers.Test.csproj`
Expected: PASS — full suite green; no references to `SdfModelInput`.

Run: `grep -rn "SdfModelInput\|modelOnly\|WriteModelOnly\|GzSimModelOnly\|BuildSdfModelInput" SW2GZ Test`
Expected: no matches (all removed).

net48 SW build (best-effort — requires SolidWorks interop DLLs): `dotnet build SW2GZ/SW2GZ.csproj` (or the MSBuild command from `BUILD.md`). If DLLs are unavailable on this machine, review the four net48 files above to confirm each `Run(...)` call matches the new signature and no symbol references a deleted type. Note in the commit message that the net48 build must be confirmed on a SolidWorks workstation.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Route all export modes through Sw2gzPipeline; remove dead SDF path"
```

---

## Self-Review

- **Spec coverage:** SdfModelWriter rewrite → Task 1; SdfWorldWriter include → Task 2; GzAsset/GzWorld launches → Task 3; mode-aware CMake/package.xml → Task 4; pipeline mode branch + signature → Task 5; ExportHelper reroute + delete legacy POCO/path → Task 6. Per-mode output table (urdf vs models, control on/off, world empty vs include, spawn vs load, lean deps) is realized across Tasks 4–6. All spec items mapped.
- **Type consistency:** `SdfModelWriter.Serialize(RobotModel)`/`Write(RobotModel,string)`, `SdfWorldWriter.WriteWithModel(SdfWorldInput,string)`, `LaunchPyWriter.GzAsset(string)`/`GzWorld(string,string)`, `AmentCMakeInput(string,bool,bool,bool,bool)`, `PackageXmlInput{ GzMode }`, `Sw2gzPipeline.Run(...,IReadOnlyList<SensorDef>,ExportMode)` — names match across all tasks.
- **Placeholder scan:** every code step shows full code; test assertion strings are exact. The two "confirm ctor signature" notes (Task 1 helpers, Task 5 mock setup) instruct copying from existing adjacent code, not leaving blanks.
- **Build-order risk:** Tasks 1–5 keep `dotnet test` green and keep net48 compiling (legacy `SdfModelInput` API retained until Task 6). Task 6 restores/finalizes net48. Flagged explicitly.
