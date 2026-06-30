/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — persisted wizard state ("checkpoint") for the native SW2GZ export
PropertyManagerPage. The wizard reads this on open and writes it on each Next,
serialized via DataContract to a SolidWorks Attribute feature in the assembly
document tree (see Sw2gzConfigSerialization). Reopening the assembly and
clicking the SW2GZ button resumes from here.

Pure / COM-free so it round-trips in the net8 test project. Fields grow as the
later wizard steps (Geometry/Joints/Review) are implemented.
*/
using System.Collections.Generic;
using System.Runtime.Serialization;
using SW2GZ.Build.Model;
using SW2GZ.Ros2;

namespace SW2GZ.URDFExport
{
    [DataContract(Name = "Sw2gzExportConfig", Namespace = "")]
    public sealed class Sw2gzExportConfig
    {
        // Step 1 — what to generate. Drives the output file/folder layout.
        [DataMember] public ExportMode Mode { get; set; } = ExportMode.RobotPackage;

        // Coordinate convention. Describes which SW axis the user's assembly
        // treats as "up" (gravity-opposed) and which axis the robot "faces".
        // Defaults match the stock SW template: Y up, Z out of the screen.
        // SwToRosRotation.Build(SwUpAxis, SwForwardAxis) yields the rotation
        // applied on the world_to_<root> joint in the URDF (and the include
        // pose / spawn -R -P -Y in SDF modes).
        [DataMember] public SW2GZ.Build.Model.AxisDirection SwUpAxis { get; set; }
            = SW2GZ.Build.Model.AxisDirection.PlusY;
        [DataMember] public SW2GZ.Build.Model.AxisDirection SwForwardAxis { get; set; }
            = SW2GZ.Build.Model.AxisDirection.PlusZ;

        // Whether to emit a synthetic <link name="world"/> + world_to_<root>
        // fixed joint that anchors the robot to a known ROS frame. False by
        // default (REP-105 convention: base_link IS the root; an external
        // static_transform_publisher handles world placement if needed). Set
        // true for fixed-base manipulators where you want the world frame in
        // the URDF itself (Gz still anchors via Gazebo &lt;static&gt; or a
        // separate fixed joint when off).
        [DataMember] public bool EmitWorldLink { get; set; } = false;

        // Step 2 — output destination, package identity, and package metadata.
        [DataMember] public string OutputFolder { get; set; } = string.Empty;
        [DataMember] public string PackageName { get; set; } = string.Empty;
        [DataMember] public string Author { get; set; } = string.Empty;
        [DataMember] public string Email { get; set; } = string.Empty;
        [DataMember] public string License { get; set; } = string.Empty;

        // Resume position — 0-based wizard step index reached at last save.
        [DataMember] public int LastStep { get; set; }

        // Step 3 — link definitions (name + assigned component ids + base flag).
        [DataMember] public List<LinkDef> Links { get; set; } = new List<LinkDef>();

        // Step 4 — joint definitions, one per non-root link edge. Seeded from the
        // link tree (JointSeeder) and editable in the Joints step.
        [DataMember] public List<JointDef> Joints { get; set; } = new List<JointDef>();

        // World mode — picked components + physics from the Create-World wizard.
        // Flat schema mirroring Sw2gzWorldConfig (the v2.1.0 in-memory model);
        // WorldGround empty ⇒ no ground component picked ⇒ default ground_plane.
        // WorldAssets are the other static environment components, each inlined
        // as a <model><static> at its assembly pose.
        [DataMember] public string WorldGround { get; set; } = string.Empty;
        [DataMember] public List<string> WorldAssets { get; set; } = new List<string>();
        [DataMember] public string WorldPhysicsEngine { get; set; } = "ode";
        [DataMember] public double WorldMaxStepSize { get; set; } = 0.001;
        [DataMember] public double WorldRealTimeFactor { get; set; } = 1.0;
        // Coulomb friction coefficient (mu = mu2) emitted on every world model's
        // <collision> so a robot spawned into the world grips the floor instead
        // of sliding. 1.0 matches the Gz/ODE default but makes it explicit and
        // tunable; the exporter always passes it, so exported worlds carry a
        // friction surface.
        [DataMember] public double WorldFriction { get; set; } = 1.0;
        // Initial GUI camera framing: "iso" (default) | "top" | "front". The
        // exporter computes the actual camera pose from the scene bounds; this
        // only picks the viewing direction.
        [DataMember] public string WorldInitialView { get; set; } = "iso";
        // Scene/environment settings from the "World Settings" dialog (lighting,
        // sky, fog, grid, gravity, wind, geo). One nested object so there's a
        // single place to default / clone / serialize.
        [DataMember] public Sw2gzWorldSceneConfig WorldScene { get; set; } = new Sw2gzWorldSceneConfig();
        // World-level sensor/teleop support plugins ("Sensors" panel) — toggles
        // which Gz system/GUI plugins the world enables for spawned models.
        [DataMember] public Sw2gzWorldSensorsConfig WorldSensorPlugins { get; set; } = new Sw2gzWorldSensorsConfig();

        // Asset mode — single part exported as a reusable Gz model.
        [DataMember] public string AssetBodyPart   { get; set; } = string.Empty;
        [DataMember] public double AssetFrictionMu { get; set; } = 0.8;
        [DataMember] public bool   AssetIsStatic   { get; set; } = true;

        // Stacks — à-la-carte ROS 2 / Gazebo stack selection for this assembly.
        // The full-stack default for legacy configs (saved before this field
        // existed) is guaranteed by the [OnDeserializing] hook below, NOT this
        // initializer: DataContractSerializer builds instances via
        // GetUninitializedObject and never runs initializers. The initializer
        // here only covers the `new Sw2gzExportConfig()` constructor path.
        [DataMember] public StackProfile Stacks { get; set; } = StackProfile.Default();

        // DataContractSerializer constructs instances via GetUninitializedObject,
        // which skips field/property initializers. Without this hook a config
        // saved before a field existed would deserialize it to null (→ NRE on
        // first read). Seed defaults here so legacy assemblies resume exactly as
        // before; any present element in the XML overwrites these during member
        // population. Stacks gets the full-stack default; Links/Joints get empty
        // lists for defense-in-depth symmetry (no real persisted checkpoint omits
        // them, but an unguarded .Count/enumeration on null would still throw).
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Stacks = StackProfile.Default();
            Links = new List<LinkDef>();
            Joints = new List<JointDef>();
            // World-mode defaults for checkpoints saved before these fields existed.
            WorldGround = string.Empty;
            WorldAssets = new List<string>();
            WorldPhysicsEngine = "ode";
            WorldMaxStepSize = 0.001;
            WorldRealTimeFactor = 1.0;
            WorldFriction = 1.0;
            WorldInitialView = "iso";
            WorldScene = new Sw2gzWorldSceneConfig();
            WorldSensorPlugins = new Sw2gzWorldSensorsConfig();
            AssetBodyPart = string.Empty;
            AssetFrictionMu = 0.8;
            AssetIsStatic = true;
            // Field-not-present defaults: legacy checkpoints saved before the
            // coordinate-convention fields existed deserialize them to the
            // enum default (PlusX = 0), which is the wrong "up". Seed the
            // SW-default convention here so old checkpoints behave correctly.
            SwUpAxis = SW2GZ.Build.Model.AxisDirection.PlusY;
            SwForwardAxis = SW2GZ.Build.Model.AxisDirection.PlusZ;
            EmitWorldLink = false;
        }

        /// Returns a shallow clone of this config with `EmitWorldLink`
        /// overridden. Used by `Sw2gzModelPreviewer` to bake the SW→ROS
        /// rotation into the preview URDF without mutating the user's
        /// saved config — real exports still honour the user's setting.
        /// Lists are shared by reference; the pipeline never mutates
        /// Links / Joints / Stacks so the shallow share is safe.
        public Sw2gzExportConfig WithEmitWorldLink(bool emitWorldLink)
        {
            return new Sw2gzExportConfig
            {
                Mode          = this.Mode,
                SwUpAxis      = this.SwUpAxis,
                SwForwardAxis = this.SwForwardAxis,
                EmitWorldLink = emitWorldLink,
                OutputFolder  = this.OutputFolder,
                PackageName   = this.PackageName,
                Author        = this.Author,
                Email         = this.Email,
                License       = this.License,
                LastStep      = this.LastStep,
                Links         = this.Links,
                Joints        = this.Joints,
                Stacks        = this.Stacks,
                WorldGround        = this.WorldGround,
                WorldAssets        = this.WorldAssets,
                WorldPhysicsEngine = this.WorldPhysicsEngine,
                WorldMaxStepSize   = this.WorldMaxStepSize,
                WorldRealTimeFactor = this.WorldRealTimeFactor,
                WorldFriction      = this.WorldFriction,
                WorldInitialView   = this.WorldInitialView,
                WorldScene         = this.WorldScene,
                WorldSensorPlugins = this.WorldSensorPlugins,
                AssetBodyPart   = this.AssetBodyPart,
                AssetFrictionMu = this.AssetFrictionMu,
                AssetIsStatic   = this.AssetIsStatic,
            };
        }
    }
}
