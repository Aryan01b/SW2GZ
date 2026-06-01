namespace SW2GZ.SwSurface.Abstractions
{
    /// <summary>
    /// Provides scale factors from the active SolidWorks document's unit system
    /// to SI (meters, kilograms) for downstream URDF/SDF emission.
    /// </summary>
    public interface IUnitsContext
    {
        /// <summary>Meters per source-length-unit.</summary>
        double LengthScale { get; }

        /// <summary>Kilograms per source-mass-unit.</summary>
        double MassScale { get; }
    }
}
