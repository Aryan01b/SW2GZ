namespace SW2GZ.Build
{
    /// <summary>
    /// Selects the algorithm used by <see cref="ConvexHullCollider"/> to derive a
    /// collision mesh from a visual mesh.
    /// </summary>
    public enum ColliderStrategy
    {
        /// <summary>Real 3D convex hull (QuickHull). Default for the export pipeline.</summary>
        ConvexHull,
        /// <summary>Axis-aligned bounding box. Stopgap / explicit primitive fallback.</summary>
        Aabb,
    }
}
