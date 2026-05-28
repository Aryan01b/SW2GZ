using System.Numerics;
using SW2GZ.Math;

namespace SW2GZ.Build
{
    public sealed record MassProps(double Mass, Vector3 ComLocal, Matrix3 InertiaAtComLocal);
}
