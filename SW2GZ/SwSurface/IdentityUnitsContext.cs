namespace SW2GZ.SwSurface
{
    using SW2GZ.SwSurface.Abstractions;

    /// <summary>
    /// Default scale: source data already in SI (meters / kilograms).
    /// Pipelines that do not need unit conversion can use this directly.
    /// </summary>
    public sealed class IdentityUnitsContext : IUnitsContext
    {
        public double LengthScale => 1.0;
        public double MassScale => 1.0;
    }
}
