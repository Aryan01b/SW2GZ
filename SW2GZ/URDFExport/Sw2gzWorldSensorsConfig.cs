/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Persisted "Sensors" config for World mode. The world does NOT place individual
sensors on its models; it just enables the world-level system/GUI plugins that
spawned models (robots created at runtime) need so their own sensors and
keyboard teleop work. Each flag toggles one Gz Harmonic <plugin>.

Edited in the "Sensors" PMP, stored on Sw2gzDoc.World.SensorPlugins, mapped to
the pure SdfWorldPlugins flags at export by Sw2gzWorldExporter.

UserCommands + SceneBroadcaster default ON — they are the baseline plugins a
world needs to spawn/delete models at runtime and stream scene state to the GUI
(the world writer emitted them unconditionally before this config existed). The
OnDeserializing hook reseeds defaults so a legacy doc saved before this field
never loads with them off.
*/
using System.Runtime.Serialization;

namespace SW2GZ.URDFExport
{
    [DataContract]
    public sealed class Sw2gzWorldSensorsConfig
    {
        // Sensor-family system plugins — enable a spawned robot's matching
        // sensors to actually run in this world.
        [DataMember] public bool Sensors      { get; set; } = false;  // gz-sim-sensors-system (camera/lidar/depth/rgbd)
        [DataMember] public bool Imu          { get; set; } = false;  // gz-sim-imu-system
        [DataMember] public bool Contact      { get; set; } = false;  // gz-sim-contact-system
        [DataMember] public bool ForceTorque  { get; set; } = false;  // gz-sim-forcetorque-system
        [DataMember] public bool Navsat       { get; set; } = false;  // gz-sim-navsat-system

        // Baseline runtime plugins (default on) — needed to spawn models and
        // broadcast scene state. The world writer emitted these unconditionally
        // before this config, so default-on keeps existing worlds identical.
        [DataMember] public bool UserCommands     { get; set; } = true;   // gz-sim-user-commands-system
        [DataMember] public bool SceneBroadcaster { get; set; } = true;   // gz-sim-scene-broadcaster-system

        // Keyboard teleop — KeyPublisher (GUI) publishes keystrokes on
        // /keyboard/keypress; TriggeredPublisher maps arrow keys to a Twist on
        // /cmd_vel so a spawned robot can be driven from the keyboard.
        [DataMember] public bool KeyPublisher       { get; set; } = false;
        [DataMember] public bool TriggeredPublisher { get; set; } = false;

        public Sw2gzWorldSensorsConfig Clone() => (Sw2gzWorldSensorsConfig)MemberwiseClone();

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            // Reseed baseline-on defaults for docs saved before this field.
            UserCommands = true;
            SceneBroadcaster = true;
        }
    }
}
