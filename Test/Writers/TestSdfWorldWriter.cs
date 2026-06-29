/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Numerics;
using SW2GZ.Build.Model;
using SW2GZ.Gz;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Writers.Tests
{
    public class TestSdfWorldWriter
    {
        [Theory]
        [InlineData("gz-sim-physics-system")]
        [InlineData("gz-sim-user-commands-system")]
        [InlineData("gz-sim-scene-broadcaster-system")]
        public void Write_UsesUnversionedHarmonicPluginFilenames_Bug4(string expectedFilename)
        {
            var sdf = SdfWorldWriter.Write(new SdfWorldInput("empty"));
            Assert.Contains(expectedFilename, sdf);
        }

        [Theory]
        [InlineData("gz-sim-sensors-system")]
        [InlineData("gz-sim-imu-system")]
        public void Write_WithSensors_EmitsSensorFamilyPlugins_Bug4(string expectedFilename)
        {
            // After Fix 1, sensor-family plugins are only emitted when sensors are
            // present. SdfSensorPlugins is the single source of truth.
            var imu = new ImuSensor("imu", "base_link", Pose.Identity, "/imu", "base_link", 100, 0);
            var cam = new CameraSensor("cam", "base_link", Pose.Identity, "/cam", "base_link", 30, 640, 480, 1.047, 0.1, 100.0);
            var sensors = new SensorDef[] { imu, cam };
            var sdf = SdfWorldWriter.Write(new SdfWorldInput("empty"), sensors);
            Assert.Contains(expectedFilename, sdf);
        }

        [Fact]
        public void Write_DoesNotUseGardenVersionedPlugins_Bug4()
        {
            var sdf = SdfWorldWriter.Write(new SdfWorldInput("empty"));
            Assert.DoesNotContain("gz-sim8-", sdf);
            Assert.DoesNotContain("gz-sim7-", sdf);
        }

        [Fact]
        public void Write_UsesHarmonicNamespace()
        {
            var sdf = SdfWorldWriter.Write(new SdfWorldInput("empty"));
            // Harmonic uses gz::sim, NOT ignition::gazebo
            Assert.Contains("gz::sim::systems::Physics", sdf);
            Assert.DoesNotContain("ignition::gazebo", sdf);
        }

        [Fact]
        public void Write_EmitsSdfVersion110()
        {
            var sdf = SdfWorldWriter.Write(new SdfWorldInput("empty"));
            Assert.Contains("<sdf version=\"1.10\">", sdf);
        }

        [Fact]
        public void Write_EmitsWorldNameFromInput()
        {
            var sdf = SdfWorldWriter.Write(new SdfWorldInput("my_test_world"));
            Assert.Contains("<world name=\"my_test_world\">", sdf);
        }

        [Fact]
        public void Write_EmitsPhysicsBlock()
        {
            var sdf = SdfWorldWriter.Write(new SdfWorldInput("empty"));
            Assert.Contains("<physics", sdf);
        }

        [Fact]
        public void Write_EmitsSunAndGroundPlane()
        {
            var sdf = SdfWorldWriter.Write(new SdfWorldInput("empty"));
            Assert.Contains("<light", sdf);          // sun
            Assert.Contains("ground_plane", sdf);    // ground
        }

        [Fact]
        public void Write_StartsWithXmlProlog()
        {
            var sdf = SdfWorldWriter.Write(new SdfWorldInput("empty"));
            Assert.StartsWith("<?xml version=\"1.0\"?>", sdf.TrimStart());
        }

        [Fact]
        public void Write_NullInput_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => SdfWorldWriter.Write(null));
        }

        [Fact]
        public void Write_NullWorldName_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => SdfWorldWriter.Write(new SdfWorldInput(null)));
        }

        [Fact]
        public void Write_WhitespaceWorldName_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => SdfWorldWriter.Write(new SdfWorldInput("  ")));
        }

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

        // ── WriteScene (world-mode: inlined static asset models) ──────────────
        private static SdfSceneInput Scene(params SdfSceneModel[] models) =>
            new SdfSceneInput("env", models);

        [Fact]
        public void WriteScene_EmitsInlinedStaticModelPerComponent()
        {
            var sdf = SdfWorldWriter.WriteScene(Scene(
                new SdfSceneModel("floor", "floor.dae"),
                new SdfSceneModel("rack", "rack.dae")));
            Assert.Contains("<model name=\"floor\">", sdf);
            Assert.Contains("<model name=\"rack\">", sdf);
            Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(sdf, "<static>true</static>").Count);
        }

        [Fact]
        public void WriteScene_VisualAndCollisionShareTheSameMesh()
        {
            var sdf = SdfWorldWriter.WriteScene(Scene(new SdfSceneModel("floor", "floor.dae")));
            Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(sdf, "meshes/floor.dae").Count);
            Assert.Contains("<visual name=\"visual\">", sdf);
            Assert.Contains("<collision name=\"collision\">", sdf);
        }

        [Fact]
        public void WriteScene_NoGround_OmitsGroundPlane()
        {
            var sdf = SdfWorldWriter.WriteScene(
                new SdfSceneInput("env", new[] { new SdfSceneModel("floor", "floor.dae") },
                    IncludeGroundPlane: false));
            Assert.DoesNotContain("ground_plane", sdf);
        }

        [Fact]
        public void WriteScene_NoGroundPicked_IncludesDefaultGroundPlane()
        {
            var sdf = SdfWorldWriter.WriteScene(
                new SdfSceneInput("env", System.Array.Empty<SdfSceneModel>(),
                    IncludeGroundPlane: true));
            Assert.Contains("ground_plane", sdf);
        }

        [Fact]
        public void WriteScene_PhysicsEngineAndStepFromInput()
        {
            var sdf = SdfWorldWriter.WriteScene(
                new SdfSceneInput("env", System.Array.Empty<SdfSceneModel>(),
                    PhysicsEngine: "bullet", MaxStepSize: 0.002, RealTimeFactor: 2.0));
            Assert.Contains("type=\"bullet\"", sdf);
            Assert.Contains("<max_step_size>0.002</max_step_size>", sdf);
            Assert.Contains("<real_time_factor>2</real_time_factor>", sdf);
        }

        [Fact]
        public void WriteScene_NonIdentityRotation_EmitsModelPose()
        {
            var sdf = SdfWorldWriter.WriteScene(
                new SdfSceneInput("env", new[] { new SdfSceneModel("floor", "floor.dae") },
                    Roll: 1.5708, Pitch: 0, Yaw: 1.5708));
            Assert.Contains("<pose>0 0 0 1.5708 0 1.5708</pose>", sdf);
        }

        [Fact]
        public void WriteScene_IdentityRotation_OmitsModelPose()
        {
            var sdf = SdfWorldWriter.WriteScene(Scene(new SdfSceneModel("floor", "floor.dae")));
            Assert.DoesNotContain("<pose>0 0 0", sdf);  // no model pose (sun's own pose is unrelated)
        }

        [Fact]
        public void WriteScene_EmitsHarmonicPluginsAndVersion()
        {
            var sdf = SdfWorldWriter.WriteScene(Scene(new SdfSceneModel("floor", "floor.dae")));
            Assert.Contains("<sdf version=\"1.10\">", sdf);
            Assert.Contains("gz-sim-scene-broadcaster-system", sdf);
            Assert.Contains("<world name=\"env\">", sdf);
        }

        [Fact]
        public void WriteScene_WithRgba_EmitsPerModelMaterial()
        {
            var sdf = SdfWorldWriter.WriteScene(Scene(
                new SdfSceneModel("floor", "floor.dae", new[] { 0.2, 0.4, 0.6, 1.0 })));
            Assert.Contains("<material>", sdf);
            Assert.Contains("<diffuse>0.2 0.4 0.6 1</diffuse>", sdf);
            Assert.Contains("<scene>", sdf);   // ambient so back-facing facets aren't black
        }

        [Fact]
        public void WriteScene_NoRgba_OmitsMaterial()
        {
            var sdf = SdfWorldWriter.WriteScene(Scene(new SdfSceneModel("floor", "floor.dae")));
            Assert.DoesNotContain("<material>", sdf);
        }

        [Fact]
        public void WriteScene_NullWorldName_Throws()
        {
            Assert.Throws<System.ArgumentException>(
                () => SdfWorldWriter.WriteScene(new SdfSceneInput(null, System.Array.Empty<SdfSceneModel>())));
        }

        // ── GUI block (Phase 1: framed camera + control panels) ───────────────
        [Fact]
        public void WriteScene_NoCamera_OmitsGuiBlock()
        {
            var sdf = SdfWorldWriter.WriteScene(Scene(new SdfSceneModel("floor", "floor.dae")));
            Assert.DoesNotContain("<gui", sdf);
            Assert.DoesNotContain("camera_pose", sdf);
        }

        [Fact]
        public void WriteScene_WithCamera_EmitsGuiAndCameraPose()
        {
            var sdf = SdfWorldWriter.WriteScene(new SdfSceneInput(
                "env", new[] { new SdfSceneModel("floor", "floor.dae") },
                Camera: new SdfCamera(3, -3, 2, 0, 0.5, 2.356)));
            Assert.Contains("<gui", sdf);
            Assert.Contains("MinimalScene", sdf);
            Assert.Contains("<camera_pose>3 -3 2 0 0.5 2.356</camera_pose>", sdf);
        }

        [Fact]
        public void GuiBlock_Default_EmitsStandardHarmonicPanels()
        {
            var gui = SdfGuiBlock.Default(new SdfCamera(1, 2, 3, 0, 0, 0));
            Assert.Contains("MinimalScene", gui);
            Assert.Contains("GzSceneManager", gui);
            Assert.Contains("WorldControl", gui);
            Assert.Contains("WorldStats", gui);
            Assert.Contains("EntityTree", gui);
            Assert.Contains("<engine>ogre2</engine>", gui);
        }

        [Fact]
        public void GuiBlock_NullCamera_OmitsCameraPose()
        {
            var gui = SdfGuiBlock.Default(null);
            Assert.DoesNotContain("camera_pose", gui);
            Assert.Contains("MinimalScene", gui);   // panels still emitted
        }

        // ── Scene settings (World Settings dialog) ────────────────────────────
        private static SdfSceneInput SceneWith(SdfSceneSettings s) =>
            new SdfSceneInput("env", new[] { new SdfSceneModel("floor", "floor.dae") }, Settings: s);

        [Fact]
        public void WriteScene_NullSettings_OmitsGravityAndExtras()
        {
            var sdf = SdfWorldWriter.WriteScene(Scene(new SdfSceneModel("floor", "floor.dae")));
            Assert.DoesNotContain("<gravity>", sdf);
            Assert.DoesNotContain("<grid>", sdf);
            Assert.DoesNotContain("<shadows>", sdf);
            Assert.Contains("<background>0.8 0.85 0.9 1</background>", sdf);  // legacy default
        }

        [Fact]
        public void WriteScene_WithSettings_EmitsGravityGridShadowsBackground()
        {
            var sdf = SdfWorldWriter.WriteScene(SceneWith(new SdfSceneSettings(
                ShowGrid: false, CastShadows: false, BgR: 0.1, BgG: 0.2, BgB: 0.3, GravityZ: -3.7)));
            Assert.Contains("<gravity>0 0 -3.7</gravity>", sdf);
            Assert.Contains("<grid>false</grid>", sdf);
            Assert.Contains("<shadows>false</shadows>", sdf);
            Assert.Contains("<background>0.1 0.2 0.3 1</background>", sdf);
        }

        [Fact]
        public void WriteScene_SkyAndFog_EmittedOnlyWhenEnabled()
        {
            var on = SdfWorldWriter.WriteScene(SceneWith(new SdfSceneSettings(Sky: true, Fog: true, FogDensity: 0.05)));
            Assert.Contains("<sky>", on);
            Assert.Contains("<fog><type>linear</type><density>0.05</density></fog>", on);

            var off = SdfWorldWriter.WriteScene(SceneWith(new SdfSceneSettings(Sky: false, Fog: false)));
            Assert.DoesNotContain("<sky>", off);
            Assert.DoesNotContain("<fog>", off);
        }

        [Fact]
        public void WriteScene_Wind_EmittedOnlyWhenNonZero()
        {
            var on = SdfWorldWriter.WriteScene(SceneWith(new SdfSceneSettings(WindX: 1.5)));
            Assert.Contains("<wind><linear_velocity>1.5 0 0</linear_velocity></wind>", on);
            var off = SdfWorldWriter.WriteScene(SceneWith(new SdfSceneSettings()));
            Assert.DoesNotContain("<wind>", off);
        }

        [Fact]
        public void WriteScene_Geo_EmittedOnlyWhenEnabled()
        {
            var on = SdfWorldWriter.WriteScene(SceneWith(new SdfSceneSettings(
                UseGeo: true, Latitude: 37.4, Longitude: -122.1, Elevation: 10, HeadingDeg: 90)));
            Assert.Contains("<spherical_coordinates>", on);
            Assert.Contains("<latitude_deg>37.4</latitude_deg>", on);
            Assert.Contains("<heading_deg>90</heading_deg>", on);

            var off = SdfWorldWriter.WriteScene(SceneWith(new SdfSceneSettings(UseGeo: false)));
            Assert.DoesNotContain("<spherical_coordinates>", off);
        }

        [Fact]
        public void Sun_FromAzimuthElevation_PointsDownAndScalesIntensity()
        {
            // Elevation 90° → sun straight overhead → direction points straight down.
            var sun = SdfPhysicsBlock.Sun(0, 90, 1.0, true);
            Assert.Contains("<direction>", sun);
            Assert.Contains("-1</direction>", sun);            // dz = -sin(90°) = -1
            Assert.Contains("<cast_shadows>true</cast_shadows>", sun);
            Assert.Contains("<diffuse>0.8 0.8 0.8 1</diffuse>", sun);   // 0.8 * intensity 1.0

            var dim = SdfPhysicsBlock.Sun(0, 90, 0.5, false);
            Assert.Contains("<diffuse>0.4 0.4 0.4 1</diffuse>", dim);   // 0.8 * 0.5
            Assert.Contains("<cast_shadows>false</cast_shadows>", dim);
        }
    }
}
