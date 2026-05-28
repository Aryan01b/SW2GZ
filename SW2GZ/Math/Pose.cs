using System.Numerics;

namespace SW2GZ.Math
{
    public sealed record Pose(Vector3 Position, Quaternion Rotation)
    {
        public static Pose Identity => new Pose(Vector3.Zero, Quaternion.Identity);
    }
}
