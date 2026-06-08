using System.Numerics;
using SW2GZ.Math;

namespace SW2GZ.Build.Urdf
{
    // FrameOffset (default Vector3.Zero) is the translation from this link's
    // URDF frame to the underlying part's design origin, expressed in the link
    // frame. When the link's frame was moved to a mate-reference point by
    // JointOriginResolver, this offset compensates the visual/collision/
    // inertial origins so the mesh keeps its world position. Zero in legacy
    // mode → existing emitters stay byte-stable.
    public sealed record UrdfLink(
        string Name,
        double Mass,
        Vector3 ComLocal,
        Matrix3 InertiaAtComLocal,
        MeshData VisualMesh,
        MeshData CollisionMesh,
        string VisualMeshFile,
        string CollisionMeshFile,
        Vector3 FrameOffset = default);
}
