using System.Numerics;
using SW2GZ.Math;

namespace SW2GZ.Build.Urdf
{
    public sealed record UrdfLink(
        string Name,
        double Mass,
        Vector3 ComLocal,
        Matrix3 InertiaAtComLocal,
        MeshData VisualMesh,
        MeshData CollisionMesh,
        string VisualMeshFile,
        string CollisionMeshFile);
}
