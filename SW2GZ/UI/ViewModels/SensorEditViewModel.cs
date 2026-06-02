/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8+ — one sensor in the Sensors step. Carries the common SensorDef fields
(name, kind, attached link, pose as xyz+rpy in the target ROS/Gz frame,
topic, gz_frame_id, update rate) plus every type-specific parameter. Only the
fields relevant to the current Kind are shown (the ShowXxxFields flags drive
view visibility). BuildSensor() switches on Kind to construct the matching
concrete SensorDef subtype, converting the edited rpy into the Pose quaternion
via PoseMath.

The roadmap's 3D axis preview is deferred (needs a WPF/SW 3D viewport); in its
place AxisReadout gives a text forward/up summary so orientation is checkable.

ForceTorque attaches to a joint (ChildJointName) and Contact to a collision
(CollisionName); both default sensibly off the attached link so the common
case needs no extra input. Navsat reuses the IMU's Gaussian-noise field.
*/
using SW2GZ.Build.Model;
using SW2GZ.Math;
using SW2GZ.UI.Mvvm;

namespace SW2GZ.UI.ViewModels
{
    public sealed class SensorEditViewModel : ObservableObject
    {
        private static readonly SensorKind[] _kindOptions =
            (SensorKind[])System.Enum.GetValues(typeof(SensorKind));

        private string _name;
        private SensorKind _kind;
        private string _attachedLink = string.Empty;
        private double _poseX, _poseY, _poseZ, _poseRoll, _posePitch, _poseYaw;
        private string _topic;
        private string _gzFrameId = string.Empty;
        private double _updateRate = 30.0;

        // IMU / Navsat
        private double _gaussianNoiseStdDev = 0.0;
        // GpuLidar
        private int _horizontalSamples = 640;
        private double _horizontalMinAngle = -System.Math.PI;
        private double _horizontalMaxAngle = System.Math.PI;
        private double _rangeMin = 0.08;
        private double _rangeMax = 10.0;
        // Camera / DepthCamera
        private int _width = 640;
        private int _height = 480;
        private double _horizontalFovRad = 1.047; // ~60°
        private double _nearClip = 0.1;
        private double _farClip = 100.0;
        // ForceTorque / Contact
        private string _childJointName = string.Empty;
        private string _collisionName = string.Empty;

        public SensorEditViewModel(string name = "sensor", SensorKind kind = SensorKind.Imu)
        {
            _name = name ?? "sensor";
            _kind = kind;
            _topic = DefaultTopic(_kind, _name);
        }

        public System.Collections.Generic.IReadOnlyList<SensorKind> SensorKindOptions => _kindOptions;

        public string Name
        {
            get => _name;
            set
            {
                if (SetProperty(ref _name, value ?? string.Empty))
                    NameChanged?.Invoke(this, System.EventArgs.Empty);
            }
        }

        public SensorKind Kind
        {
            get => _kind;
            set
            {
                if (SetProperty(ref _kind, value))
                {
                    OnPropertyChanged(nameof(ShowImuFields));
                    OnPropertyChanged(nameof(ShowLidarFields));
                    OnPropertyChanged(nameof(ShowCameraFields));
                    OnPropertyChanged(nameof(ShowForceTorqueFields));
                    OnPropertyChanged(nameof(ShowContactFields));
                    OnPropertyChanged(nameof(ShowNavsatFields));
                    KindChanged?.Invoke(this, System.EventArgs.Empty);
                }
            }
        }

        public string AttachedLink
        {
            get => _attachedLink;
            set
            {
                if (SetProperty(ref _attachedLink, value ?? string.Empty))
                    AttachmentChanged?.Invoke(this, System.EventArgs.Empty);
            }
        }

        public double PoseX { get => _poseX; set { if (SetProperty(ref _poseX, value)) OnPropertyChanged(nameof(AxisReadout)); } }
        public double PoseY { get => _poseY; set { if (SetProperty(ref _poseY, value)) OnPropertyChanged(nameof(AxisReadout)); } }
        public double PoseZ { get => _poseZ; set { if (SetProperty(ref _poseZ, value)) OnPropertyChanged(nameof(AxisReadout)); } }
        public double PoseRoll { get => _poseRoll; set { if (SetProperty(ref _poseRoll, value)) OnPropertyChanged(nameof(AxisReadout)); } }
        public double PosePitch { get => _posePitch; set { if (SetProperty(ref _posePitch, value)) OnPropertyChanged(nameof(AxisReadout)); } }
        public double PoseYaw { get => _poseYaw; set { if (SetProperty(ref _poseYaw, value)) OnPropertyChanged(nameof(AxisReadout)); } }

        public string Topic
        {
            get => _topic;
            set => SetProperty(ref _topic, value ?? string.Empty);
        }

        public string GzFrameId
        {
            get => _gzFrameId;
            set => SetProperty(ref _gzFrameId, value ?? string.Empty);
        }

        public double UpdateRate
        {
            get => _updateRate;
            set
            {
                if (SetProperty(ref _updateRate, value))
                    ValidityChanged?.Invoke(this, System.EventArgs.Empty);
            }
        }

        // ── Type-specific params ────────────────────────────────────────────
        public double GaussianNoiseStdDev { get => _gaussianNoiseStdDev; set => SetProperty(ref _gaussianNoiseStdDev, value); }
        public int HorizontalSamples { get => _horizontalSamples; set => SetProperty(ref _horizontalSamples, value); }
        public double HorizontalMinAngle { get => _horizontalMinAngle; set => SetProperty(ref _horizontalMinAngle, value); }
        public double HorizontalMaxAngle { get => _horizontalMaxAngle; set => SetProperty(ref _horizontalMaxAngle, value); }
        public double RangeMin { get => _rangeMin; set => SetProperty(ref _rangeMin, value); }
        public double RangeMax { get => _rangeMax; set => SetProperty(ref _rangeMax, value); }
        public int Width { get => _width; set => SetProperty(ref _width, value); }
        public int Height { get => _height; set => SetProperty(ref _height, value); }
        public double HorizontalFovRad { get => _horizontalFovRad; set => SetProperty(ref _horizontalFovRad, value); }
        public double NearClip { get => _nearClip; set => SetProperty(ref _nearClip, value); }
        public double FarClip { get => _farClip; set => SetProperty(ref _farClip, value); }
        public string ChildJointName { get => _childJointName; set => SetProperty(ref _childJointName, value ?? string.Empty); }
        public string CollisionName { get => _collisionName; set => SetProperty(ref _collisionName, value ?? string.Empty); }

        // ── Per-Kind field visibility ───────────────────────────────────────
        public bool ShowImuFields => _kind == SensorKind.Imu;
        public bool ShowLidarFields => _kind == SensorKind.GpuLidar;
        public bool ShowCameraFields => _kind == SensorKind.Camera || _kind == SensorKind.DepthCamera;
        public bool ShowForceTorqueFields => _kind == SensorKind.ForceTorque;
        public bool ShowContactFields => _kind == SensorKind.Contact;
        public bool ShowNavsatFields => _kind == SensorKind.Navsat;

        /// Text forward/up axis summary derived from the rpy (stand-in for the
        /// deferred 3D preview).
        public string AxisReadout => PoseMath.DescribeAxes(_poseRoll, _posePitch, _poseYaw);

        public bool UpdateRateValid => _updateRate > 0.0;

        public event System.EventHandler NameChanged;
        public event System.EventHandler KindChanged;
        public event System.EventHandler AttachmentChanged;
        public event System.EventHandler ValidityChanged;

        /// Default a sensible topic + gz_frame_id off the current kind/name/link.
        /// Called by the step VM when a sensor is first added or the user has not
        /// hand-edited the field.
        public void ApplyDefaults()
        {
            Topic = DefaultTopic(_kind, _name);
            if (!string.IsNullOrEmpty(_attachedLink))
                GzFrameId = _attachedLink;
        }

        /// Construct the concrete SensorDef for the current Kind. Pose is built
        /// from xyz + rpy via PoseMath.
        public SensorDef BuildSensor()
        {
            Pose pose = PoseMath.FromXyzRpy(_poseX, _poseY, _poseZ, _poseRoll, _posePitch, _poseYaw);
            switch (_kind)
            {
                case SensorKind.Imu:
                    return new ImuSensor(_name, _attachedLink, pose, _topic, _gzFrameId, _updateRate, _gaussianNoiseStdDev);
                case SensorKind.GpuLidar:
                    return new GpuLidarSensor(_name, _attachedLink, pose, _topic, _gzFrameId, _updateRate,
                        _horizontalSamples, _horizontalMinAngle, _horizontalMaxAngle, _rangeMin, _rangeMax);
                case SensorKind.Camera:
                    return new CameraSensor(_name, _attachedLink, pose, _topic, _gzFrameId, _updateRate,
                        _width, _height, _horizontalFovRad, _nearClip, _farClip);
                case SensorKind.DepthCamera:
                    return new DepthCameraSensor(_name, _attachedLink, pose, _topic, _gzFrameId, _updateRate,
                        _width, _height, _horizontalFovRad, _nearClip, _farClip);
                case SensorKind.ForceTorque:
                    return new ForceTorqueSensor(_name, _attachedLink, pose, _topic, _gzFrameId, _updateRate,
                        string.IsNullOrEmpty(_childJointName) ? _attachedLink + "_joint" : _childJointName);
                case SensorKind.Contact:
                    return new ContactSensor(_name, _attachedLink, pose, _topic, _gzFrameId, _updateRate,
                        string.IsNullOrEmpty(_collisionName) ? _attachedLink + "_collision" : _collisionName);
                case SensorKind.Navsat:
                    return new NavsatSensor(_name, _attachedLink, pose, _topic, _gzFrameId, _updateRate, _gaussianNoiseStdDev);
                default:
                    return new ImuSensor(_name, _attachedLink, pose, _topic, _gzFrameId, _updateRate, _gaussianNoiseStdDev);
            }
        }

        private static string DefaultTopic(SensorKind kind, string name)
        {
            string leaf = string.IsNullOrEmpty(name) ? "sensor" : name;
            switch (kind)
            {
                case SensorKind.Imu: return "/imu/" + leaf;
                case SensorKind.GpuLidar: return "/scan/" + leaf;
                case SensorKind.Camera: return "/camera/" + leaf + "/image";
                case SensorKind.DepthCamera: return "/camera/" + leaf + "/depth";
                case SensorKind.ForceTorque: return "/ft/" + leaf;
                case SensorKind.Contact: return "/contact/" + leaf;
                case SensorKind.Navsat: return "/navsat/" + leaf;
                default: return "/" + leaf;
            }
        }
    }
}
