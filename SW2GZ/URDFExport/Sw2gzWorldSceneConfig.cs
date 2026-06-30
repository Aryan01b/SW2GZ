/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Persisted world-scene/environment settings — the model behind the "World
Settings" ribbon dialog. Stored as one nested object on Sw2gzExportConfig so
there is a single place to default / clone / serialize (vs. ~20 flat fields).
Maps to the pure writer record SdfSceneSettings via ToSceneSettings().

DataContractSerializer builds instances with GetUninitializedObject (skips field
initializers), so the [OnDeserializing] hook reseeds defaults for any field a
legacy checkpoint omitted.
*/
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using SW2GZ.Gz;

namespace SW2GZ.URDFExport
{
    // W3 — one extra fill light beyond the sun. Pose in ROS (Z-up) world frame;
    // Range drives point/spot attenuation. Persisted on Sw2gzWorldSceneConfig.
    [DataContract]
    public sealed class Sw2gzLightConfig
    {
        [DataMember] public string Type { get; set; } = "point";   // point|spot|directional
        [DataMember] public double X { get; set; } = 0.0;
        [DataMember] public double Y { get; set; } = 0.0;
        [DataMember] public double Z { get; set; } = 2.0;
        [DataMember] public double R { get; set; } = 1.0;
        [DataMember] public double G { get; set; } = 1.0;
        [DataMember] public double B { get; set; } = 1.0;
        [DataMember] public double Intensity { get; set; } = 1.0;
        [DataMember] public double Range { get; set; } = 10.0;
        [DataMember] public bool CastShadows { get; set; } = false;

        public Sw2gzLightConfig Clone() => (Sw2gzLightConfig)MemberwiseClone();
    }

    [DataContract]
    public sealed class Sw2gzWorldSceneConfig
    {
        // View
        [DataMember] public string InitialView { get; set; } = "iso";   // iso | top | front
        [DataMember] public bool ShowGrid { get; set; } = true;
        // Lighting
        [DataMember] public double SunAzimuthDeg { get; set; } = 169.0;
        [DataMember] public double SunElevationDeg { get; set; } = 60.0;
        [DataMember] public double SunIntensity { get; set; } = 1.0;
        [DataMember] public bool CastShadows { get; set; } = true;
        // Sky & fog
        [DataMember] public bool Sky { get; set; } = false;
        [DataMember] public bool Fog { get; set; } = false;
        [DataMember] public double FogDensity { get; set; } = 0.02;
        [DataMember] public double BgR { get; set; } = 0.8;
        [DataMember] public double BgG { get; set; } = 0.85;
        [DataMember] public double BgB { get; set; } = 0.9;
        // Environment
        [DataMember] public double GravityZ { get; set; } = -9.8;
        [DataMember] public double WindX { get; set; } = 0.0;
        [DataMember] public double WindY { get; set; } = 0.0;
        [DataMember] public double WindZ { get; set; } = 0.0;
        // W2 — Coulomb friction (mu=mu2) emitted on every world collision so a
        // spawned robot grips the floor. Threaded to cfg.WorldFriction by the
        // Bridge; the world writer always emits it.
        [DataMember] public double Friction { get; set; } = 1.0;
        // W3 — extra fill lights beyond the sun (empty = sun only).
        [DataMember] public List<Sw2gzLightConfig> Lights { get; set; } = new List<Sw2gzLightConfig>();
        // Geo
        [DataMember] public bool UseGeo { get; set; } = false;
        [DataMember] public double Latitude { get; set; } = 0.0;
        [DataMember] public double Longitude { get; set; } = 0.0;
        [DataMember] public double Elevation { get; set; } = 0.0;
        [DataMember] public double HeadingDeg { get; set; } = 0.0;

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            InitialView = "iso";
            ShowGrid = true;
            SunAzimuthDeg = 169.0; SunElevationDeg = 60.0; SunIntensity = 1.0; CastShadows = true;
            Sky = false; Fog = false; FogDensity = 0.02;
            BgR = 0.8; BgG = 0.85; BgB = 0.9;
            GravityZ = -9.8; WindX = 0.0; WindY = 0.0; WindZ = 0.0;
            Friction = 1.0;
            Lights = new List<Sw2gzLightConfig>();
            UseGeo = false; Latitude = 0.0; Longitude = 0.0; Elevation = 0.0; HeadingDeg = 0.0;
        }

        // Deep clone — the Lights list must NOT be shared by reference, or a PMP
        // cancel/rollback would leak edits into the snapshot (MemberwiseClone is
        // shallow). All other members are value types, so a memberwise base copy
        // plus a fresh list of cloned lights is sufficient.
        public Sw2gzWorldSceneConfig Clone()
        {
            var c = (Sw2gzWorldSceneConfig)MemberwiseClone();
            c.Lights = Lights == null
                ? new List<Sw2gzLightConfig>()
                : Lights.Select(l => l.Clone()).ToList();
            return c;
        }

        // Map the persisted lights → pure writer records consumed by WriteScene.
        public List<SdfLight> ToExtraLights()
        {
            var list = new List<SdfLight>();
            if (Lights == null) return list;
            int n = 0;
            foreach (Sw2gzLightConfig l in Lights)
            {
                if (l == null) continue;
                n++;
                list.Add(new SdfLight(
                    Name: "light" + n, Type: l.Type,
                    X: l.X, Y: l.Y, Z: l.Z, R: l.R, G: l.G, B: l.B,
                    Intensity: l.Intensity, Range: l.Range, CastShadows: l.CastShadows));
            }
            return list;
        }

        // Map to the pure writer record consumed by SdfWorldWriter.WriteScene.
        public SdfSceneSettings ToSceneSettings() => new SdfSceneSettings(
            ShowGrid: ShowGrid,
            SunAzimuthDeg: SunAzimuthDeg, SunElevationDeg: SunElevationDeg,
            SunIntensity: SunIntensity, CastShadows: CastShadows,
            Sky: Sky, Fog: Fog, FogDensity: FogDensity,
            BgR: BgR, BgG: BgG, BgB: BgB,
            GravityZ: GravityZ, WindX: WindX, WindY: WindY, WindZ: WindZ,
            UseGeo: UseGeo, Latitude: Latitude, Longitude: Longitude,
            Elevation: Elevation, HeadingDeg: HeadingDeg);
    }
}
