using System.Drawing;
using System.Numerics;

namespace SW2GZ.Build
{
    public sealed record MeshData(Vector3[] Vertices, int[] Triangles, Color? MaterialColor);
}
