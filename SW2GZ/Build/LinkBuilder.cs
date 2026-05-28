using System;
using SW2GZ.Build.Urdf;

namespace SW2GZ.Build
{
    public static class LinkBuilder
    {
        public static UrdfLink Build(string name, MassProps mass, MeshData visual, MeshData collision)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name required", nameof(name));
            if (mass == null) throw new ArgumentNullException(nameof(mass));
            if (visual == null) throw new ArgumentNullException(nameof(visual));
            if (collision == null) throw new ArgumentNullException(nameof(collision));

            return new UrdfLink(
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
}
