/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Collections.Generic;

namespace SW2GZ.Ros2
{
    public enum ExportMode { RobotPackage, SdfModel, SdfWorld }
    public enum GzVersion { Fortress, Harmonic, Ionic }
    public enum Ros2Distro { Humble, Jazzy, Kilted, Rolling }

    public class TargetProfile
    {
        public ExportMode Mode { get; set; } = ExportMode.RobotPackage;
        public GzVersion Gz { get; set; } = GzVersion.Harmonic;
        public Ros2Distro Ros2 { get; set; } = Ros2Distro.Jazzy;

        // Suffix in ros-<distro>-ros-<prefix>-* package names.
        // Fortress era: ros-humble-ros-ign-gazebo. Harmonic+: ros-jazzy-ros-gz-sim.
        public static readonly IReadOnlyDictionary<GzVersion, string> GzPackagePrefix =
            new Dictionary<GzVersion, string>
            {
                { GzVersion.Fortress, "ign" },
                { GzVersion.Harmonic, "gz" },
                { GzVersion.Ionic,    "gz" },
            };

        // Plugin shared library name (without lib prefix / .so suffix) referenced
        // in SDF <plugin filename="..."> for the main sim system.
        public static readonly IReadOnlyDictionary<GzVersion, string> SimPluginLib =
            new Dictionary<GzVersion, string>
            {
                { GzVersion.Fortress, "ignition-gazebo6" },
                { GzVersion.Harmonic, "gz-sim8" },
                { GzVersion.Ionic,    "gz-sim9" },
            };

        // ros2_control hardware plugin system library name.
        public static readonly IReadOnlyDictionary<GzVersion, string> Ros2ControlPlugin =
            new Dictionary<GzVersion, string>
            {
                { GzVersion.Fortress, "ign_ros2_control-system" },
                { GzVersion.Harmonic, "gz_ros2_control-system" },
                { GzVersion.Ionic,    "gz_ros2_control-system" },
            };

        public static readonly IReadOnlyDictionary<GzVersion, string> SdfVersion =
            new Dictionary<GzVersion, string>
            {
                { GzVersion.Fortress, "1.9" },
                { GzVersion.Harmonic, "1.10" },
                { GzVersion.Ionic,    "1.11" },
            };

        // Default Gz that ships with each ROS 2 distro per OSRF release pairing.
        public static readonly IReadOnlyDictionary<Ros2Distro, GzVersion> Pairing =
            new Dictionary<Ros2Distro, GzVersion>
            {
                { Ros2Distro.Humble,  GzVersion.Fortress },
                { Ros2Distro.Jazzy,   GzVersion.Harmonic },
                { Ros2Distro.Kilted,  GzVersion.Ionic },
                { Ros2Distro.Rolling, GzVersion.Harmonic },
            };

        // Convenience: ros-<distro>-ros-<prefix>-sim package name.
        public string RosGzSimPackageName()
        {
            return $"ros-{Ros2.ToString().ToLowerInvariant()}-ros-{GzPackagePrefix[Gz]}-sim";
        }
    }
}
