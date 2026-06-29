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
using System.Runtime.Serialization;
using SW2GZ.Gz;

namespace SW2GZ.URDFExport
{
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
            UseGeo = false; Latitude = 0.0; Longitude = 0.0; Elevation = 0.0; HeadingDeg = 0.0;
        }

        public Sw2gzWorldSceneConfig Clone() => (Sw2gzWorldSceneConfig)MemberwiseClone();

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
