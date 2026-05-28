using SW2GZ.Build.Urdf;

namespace SW2GZ.Build
{
    public static class LinkBuilder
    {
        public static UrdfLink Build(string name, MassProps mass, MeshData visual, MeshData collision) =>
            new UrdfLink(
                Name: name,
                Mass: mass.Mass,
                ComLocal: mass.ComLocal,
                InertiaAtComLocal: mass.InertiaAtComLocal,
                VisualMesh: visual,
                CollisionMesh: collision,
                VisualMeshFile: $"{name}.dae",
                CollisionMeshFile: $"{name}_collision.stl");
    }
}
