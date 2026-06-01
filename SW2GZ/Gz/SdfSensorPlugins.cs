/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P6-data — emits the world-level <plugin> tags required by the sensor
families present in a sensor list. Each family is emitted at most once
(dedup by SensorKind family).

Family mapping (matches docs/reference/gz-harmonic.md §3):
  Imu                                 → gz-sim-imu-system
  GpuLidar / Camera / DepthCamera     → gz-sim-sensors-system
  Contact                             → gz-sim-contact-system
  ForceTorque                         → gz-sim-forcetorque-system
  Navsat                              → gz-sim-navsat-system

Output is just the <plugin> lines (no enclosing <world>). Each line is
prefixed with a 4-space indent so it slots cleanly into SdfWorldWriter's
two-space-times-two block. Empty sensor list returns empty string —
SdfWorldWriter callers can append unconditionally.
*/
using System.Collections.Generic;
using System.Text;
using SW2GZ.Build.Model;

namespace SW2GZ.Gz
{
    public static class SdfSensorPlugins
    {
        public static string WritePluginBlock(IReadOnlyList<SensorDef> sensors)
        {
            if (sensors == null || sensors.Count == 0)
                return string.Empty;

            bool needImu = false, needSensors = false, needContact = false,
                 needForceTorque = false, needNavsat = false;

            foreach (SensorDef s in sensors)
            {
                switch (s.Kind)
                {
                    case SensorKind.Imu: needImu = true; break;
                    case SensorKind.GpuLidar:
                    case SensorKind.Camera:
                    case SensorKind.DepthCamera: needSensors = true; break;
                    case SensorKind.Contact: needContact = true; break;
                    case SensorKind.ForceTorque: needForceTorque = true; break;
                    case SensorKind.Navsat: needNavsat = true; break;
                }
            }

            var sb = new StringBuilder();
            if (needImu)
                sb.AppendLine("    <plugin filename=\"gz-sim-imu-system\" name=\"gz::sim::systems::Imu\"/>");
            if (needSensors)
                sb.AppendLine("    <plugin filename=\"gz-sim-sensors-system\" name=\"gz::sim::systems::Sensors\"><render_engine>ogre2</render_engine></plugin>");
            if (needContact)
                sb.AppendLine("    <plugin filename=\"gz-sim-contact-system\" name=\"gz::sim::systems::Contact\"/>");
            if (needForceTorque)
                sb.AppendLine("    <plugin filename=\"gz-sim-forcetorque-system\" name=\"gz::sim::systems::ForceTorque\"/>");
            if (needNavsat)
                sb.AppendLine("    <plugin filename=\"gz-sim-navsat-system\" name=\"gz::sim::systems::NavSat\"/>");
            return sb.ToString();
        }
    }
}
