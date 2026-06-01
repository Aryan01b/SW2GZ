/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P6-data — emits the SDF <sensor>...</sensor> block for a single SensorDef.
Shape per sensor kind matches gz-harmonic reference doc §1-3:
  - imu          → <sensor type="imu"> with angular_velocity + linear_acceleration noise
  - gpu_lidar    → <sensor type="gpu_lidar"> with single horizontal scan
  - camera       → <sensor type="camera"> with R8G8B8 image
  - depth_camera → <sensor type="depth_camera"> with R_FLOAT32 image
  - force_torque → <sensor type="force_torque"> (no pose/gz_frame_id — attaches to joint)
  - contact      → <sensor type="contact"> referencing a <collision> name
  - navsat       → <sensor type="navsat"> with horizontal/vertical position noise

All floats go through InvariantCulture so the test locale never injects a
comma. All dynamic strings escape via SecurityElement.Escape (defense-in-
depth — RosNameSanitizer already restricts names to safe identifiers).
*/
using System;
using System.Globalization;
using System.Numerics;
using System.Security;
using System.Text;
using SW2GZ.Build.Model;

namespace SW2GZ.Gz
{
    public static class SdfSensorBlocks
    {
        public static string Write(SensorDef sensor, int indentSpaces = 6)
        {
            if (sensor == null) throw new ArgumentNullException(nameof(sensor));
            if (indentSpaces < 0) throw new ArgumentOutOfRangeException(nameof(indentSpaces));

            string pad = new string(' ', indentSpaces);
            string pad2 = new string(' ', indentSpaces + 2);
            string pad4 = new string(' ', indentSpaces + 4);
            string pad6 = new string(' ', indentSpaces + 6);

            return sensor switch
            {
                ImuSensor imu => WriteImu(imu, pad, pad2, pad4, pad6),
                GpuLidarSensor lidar => WriteGpuLidar(lidar, pad, pad2, pad4, pad6),
                CameraSensor cam => WriteCameraLike(cam.Name, "camera", "R8G8B8",
                    cam.Pose, cam.Topic, cam.GzFrameId, cam.UpdateRate,
                    cam.Width, cam.Height, cam.HorizontalFovRad, cam.NearClip, cam.FarClip,
                    pad, pad2, pad4, pad6),
                DepthCameraSensor d => WriteCameraLike(d.Name, "depth_camera", "R_FLOAT32",
                    d.Pose, d.Topic, d.GzFrameId, d.UpdateRate,
                    d.Width, d.Height, d.HorizontalFovRad, d.NearClip, d.FarClip,
                    pad, pad2, pad4, pad6),
                ForceTorqueSensor ft => WriteForceTorque(ft, pad, pad2, pad4),
                ContactSensor c => WriteContact(c, pad, pad2, pad4),
                NavsatSensor n => WriteNavsat(n, pad, pad2, pad4, pad6),
                _ => throw new InvalidOperationException($"Unhandled SensorDef subtype: {sensor.GetType().Name}"),
            };
        }

        private static string WriteImu(ImuSensor s, string pad, string pad2, string pad4, string pad6)
        {
            var sb = new StringBuilder();
            string name = SecurityElement.Escape(s.Name);
            string topic = SecurityElement.Escape(s.Topic);
            string frame = SecurityElement.Escape(s.GzFrameId);
            string poseStr = FormatPose(s.Pose);
            string rate = Fmt(s.UpdateRate);
            string stddev = Fmt(s.GaussianNoiseStdDev);

            sb.AppendLine($"{pad}<sensor name=\"{name}\" type=\"imu\">");
            sb.AppendLine($"{pad2}<pose>{poseStr}</pose>");
            sb.AppendLine($"{pad2}<topic>{topic}</topic>");
            sb.AppendLine($"{pad2}<gz_frame_id>{frame}</gz_frame_id>");
            sb.AppendLine($"{pad2}<update_rate>{rate}</update_rate>");
            sb.AppendLine($"{pad2}<imu>");
            sb.AppendLine($"{pad4}<angular_velocity>");
            foreach (string axis in new[] { "x", "y", "z" })
                sb.AppendLine($"{pad6}<{axis}><noise type=\"gaussian\"><stddev>{stddev}</stddev></noise></{axis}>");
            sb.AppendLine($"{pad4}</angular_velocity>");
            sb.AppendLine($"{pad4}<linear_acceleration>");
            foreach (string axis in new[] { "x", "y", "z" })
                sb.AppendLine($"{pad6}<{axis}><noise type=\"gaussian\"><stddev>{stddev}</stddev></noise></{axis}>");
            sb.AppendLine($"{pad4}</linear_acceleration>");
            sb.AppendLine($"{pad2}</imu>");
            sb.AppendLine($"{pad}</sensor>");
            return sb.ToString();
        }

        private static string WriteGpuLidar(GpuLidarSensor s, string pad, string pad2, string pad4, string pad6)
        {
            var sb = new StringBuilder();
            string name = SecurityElement.Escape(s.Name);
            string topic = SecurityElement.Escape(s.Topic);
            string frame = SecurityElement.Escape(s.GzFrameId);
            string poseStr = FormatPose(s.Pose);

            sb.AppendLine($"{pad}<sensor name=\"{name}\" type=\"gpu_lidar\">");
            sb.AppendLine($"{pad2}<pose>{poseStr}</pose>");
            sb.AppendLine($"{pad2}<topic>{topic}</topic>");
            sb.AppendLine($"{pad2}<gz_frame_id>{frame}</gz_frame_id>");
            sb.AppendLine($"{pad2}<update_rate>{Fmt(s.UpdateRate)}</update_rate>");
            sb.AppendLine($"{pad2}<ray>");
            sb.AppendLine($"{pad4}<scan>");
            sb.AppendLine($"{pad6}<horizontal>");
            sb.AppendLine($"{pad6}  <samples>{s.HorizontalSamples.ToString(CultureInfo.InvariantCulture)}</samples>");
            sb.AppendLine($"{pad6}  <resolution>1</resolution>");
            sb.AppendLine($"{pad6}  <min_angle>{Fmt(s.HorizontalMinAngle)}</min_angle>");
            sb.AppendLine($"{pad6}  <max_angle>{Fmt(s.HorizontalMaxAngle)}</max_angle>");
            sb.AppendLine($"{pad6}</horizontal>");
            sb.AppendLine($"{pad4}</scan>");
            sb.AppendLine($"{pad4}<range>");
            sb.AppendLine($"{pad6}<min>{Fmt(s.RangeMin)}</min>");
            sb.AppendLine($"{pad6}<max>{Fmt(s.RangeMax)}</max>");
            sb.AppendLine($"{pad6}<resolution>0.01</resolution>");
            sb.AppendLine($"{pad4}</range>");
            sb.AppendLine($"{pad2}</ray>");
            sb.AppendLine($"{pad2}<always_on>1</always_on>");
            sb.AppendLine($"{pad2}<visualize>0</visualize>");
            sb.AppendLine($"{pad}</sensor>");
            return sb.ToString();
        }

        private static string WriteCameraLike(
            string rawName, string sdfType, string imageFormat,
            SW2GZ.Math.Pose pose, string rawTopic, string rawFrame, double updateRate,
            int width, int height, double hfov, double near, double far,
            string pad, string pad2, string pad4, string pad6)
        {
            var sb = new StringBuilder();
            string name = SecurityElement.Escape(rawName);
            string topic = SecurityElement.Escape(rawTopic);
            string frame = SecurityElement.Escape(rawFrame);
            string poseStr = FormatPose(pose);

            sb.AppendLine($"{pad}<sensor name=\"{name}\" type=\"{sdfType}\">");
            sb.AppendLine($"{pad2}<pose>{poseStr}</pose>");
            sb.AppendLine($"{pad2}<topic>{topic}</topic>");
            sb.AppendLine($"{pad2}<gz_frame_id>{frame}</gz_frame_id>");
            sb.AppendLine($"{pad2}<update_rate>{Fmt(updateRate)}</update_rate>");
            sb.AppendLine($"{pad2}<camera>");
            sb.AppendLine($"{pad4}<horizontal_fov>{Fmt(hfov)}</horizontal_fov>");
            sb.AppendLine($"{pad4}<image>");
            sb.AppendLine($"{pad6}<width>{width.ToString(CultureInfo.InvariantCulture)}</width>");
            sb.AppendLine($"{pad6}<height>{height.ToString(CultureInfo.InvariantCulture)}</height>");
            sb.AppendLine($"{pad6}<format>{imageFormat}</format>");
            sb.AppendLine($"{pad4}</image>");
            sb.AppendLine($"{pad4}<clip>");
            sb.AppendLine($"{pad6}<near>{Fmt(near)}</near>");
            sb.AppendLine($"{pad6}<far>{Fmt(far)}</far>");
            sb.AppendLine($"{pad4}</clip>");
            sb.AppendLine($"{pad2}</camera>");
            sb.AppendLine($"{pad2}<always_on>1</always_on>");
            sb.AppendLine($"{pad}</sensor>");
            return sb.ToString();
        }

        private static string WriteForceTorque(ForceTorqueSensor s, string pad, string pad2, string pad4)
        {
            // No <pose> / <gz_frame_id> — the sensor attaches to a joint frame,
            // not the link frame. Gz docs §1.5 are explicit about this.
            var sb = new StringBuilder();
            string name = SecurityElement.Escape(s.Name);
            string topic = SecurityElement.Escape(s.Topic);

            sb.AppendLine($"{pad}<sensor name=\"{name}\" type=\"force_torque\">");
            sb.AppendLine($"{pad2}<topic>{topic}</topic>");
            sb.AppendLine($"{pad2}<update_rate>{Fmt(s.UpdateRate)}</update_rate>");
            sb.AppendLine($"{pad2}<force_torque>");
            sb.AppendLine($"{pad4}<frame>child</frame>");
            sb.AppendLine($"{pad4}<measure_direction>child_to_parent</measure_direction>");
            sb.AppendLine($"{pad2}</force_torque>");
            sb.AppendLine($"{pad}</sensor>");
            return sb.ToString();
        }

        private static string WriteContact(ContactSensor s, string pad, string pad2, string pad4)
        {
            var sb = new StringBuilder();
            string name = SecurityElement.Escape(s.Name);
            string topic = SecurityElement.Escape(s.Topic);
            string col = SecurityElement.Escape(s.CollisionName);

            sb.AppendLine($"{pad}<sensor name=\"{name}\" type=\"contact\">");
            sb.AppendLine($"{pad2}<topic>{topic}</topic>");
            sb.AppendLine($"{pad2}<update_rate>{Fmt(s.UpdateRate)}</update_rate>");
            sb.AppendLine($"{pad2}<contact>");
            sb.AppendLine($"{pad4}<collision>{col}</collision>");
            sb.AppendLine($"{pad2}</contact>");
            sb.AppendLine($"{pad}</sensor>");
            return sb.ToString();
        }

        private static string WriteNavsat(NavsatSensor s, string pad, string pad2, string pad4, string pad6)
        {
            var sb = new StringBuilder();
            string name = SecurityElement.Escape(s.Name);
            string topic = SecurityElement.Escape(s.Topic);
            string frame = SecurityElement.Escape(s.GzFrameId);
            string poseStr = FormatPose(s.Pose);
            string stddev = Fmt(s.GaussianNoiseStdDev);

            sb.AppendLine($"{pad}<sensor name=\"{name}\" type=\"navsat\">");
            sb.AppendLine($"{pad2}<pose>{poseStr}</pose>");
            sb.AppendLine($"{pad2}<topic>{topic}</topic>");
            sb.AppendLine($"{pad2}<gz_frame_id>{frame}</gz_frame_id>");
            sb.AppendLine($"{pad2}<update_rate>{Fmt(s.UpdateRate)}</update_rate>");
            sb.AppendLine($"{pad2}<navsat>");
            sb.AppendLine($"{pad4}<position_sensing>");
            sb.AppendLine($"{pad6}<horizontal><noise type=\"gaussian\"><stddev>{stddev}</stddev></noise></horizontal>");
            sb.AppendLine($"{pad6}<vertical><noise type=\"gaussian\"><stddev>{stddev}</stddev></noise></vertical>");
            sb.AppendLine($"{pad4}</position_sensing>");
            sb.AppendLine($"{pad2}</navsat>");
            sb.AppendLine($"{pad}</sensor>");
            return sb.ToString();
        }

        private static string Fmt(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static string FormatPose(SW2GZ.Math.Pose pose)
        {
            Vector3 p = pose.Position;
            (double r, double pt, double y) = QuatToRpy(pose.Rotation);
            return string.Format(CultureInfo.InvariantCulture,
                "{0} {1} {2} {3} {4} {5}",
                p.X, p.Y, p.Z, r, pt, y);
        }

        // Standard quaternion -> (roll, pitch, yaw) with gimbal-lock guard.
        internal static (double R, double P, double Y) QuatToRpy(Quaternion q)
        {
            double sinr_cosp = 2 * (q.W * q.X + q.Y * q.Z);
            double cosr_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
            double roll = System.Math.Atan2(sinr_cosp, cosr_cosp);

            double sinp = 2 * (q.W * q.Y - q.Z * q.X);
            double pitch = System.Math.Abs(sinp) >= 1
                ? System.Math.CopySign(System.Math.PI / 2, sinp)
                : System.Math.Asin(sinp);

            double siny_cosp = 2 * (q.W * q.Z + q.X * q.Y);
            double cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
            double yaw = System.Math.Atan2(siny_cosp, cosy_cosp);

            return (roll, pitch, yaw);
        }
    }
}
