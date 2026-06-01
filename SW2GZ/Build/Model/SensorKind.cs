/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P6-data — discriminator for the SensorDef record hierarchy. One value per
SDF sensor type emitted by SW2GZ. Adding a new kind requires (a) a new
concrete record under Build/Model/Sensors, (b) a Write branch in
SdfSensorBlocks, (c) a family entry in SdfSensorPlugins, and (d) a
ROS↔Gz type-pair entry in RosGzBridgeYaml.
*/
namespace SW2GZ.Build.Model
{
    public enum SensorKind { Imu, GpuLidar, Camera, DepthCamera, ForceTorque, Contact, Navsat }
}
