/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Optional second interface that an IAssemblyWalker MAY implement: returns
the world-frame placement (Component2.Transform2 in SW terms) of a part
inside the active assembly. Used by Sw2gzPipeline to build per-link
anchor poses, rebase mesh vertices into link-local frame, and compute
URDF joint origins as parent-frame poses.

Kept separate from IAssemblyWalker so existing mock-based tests
(Mock<IAssemblyWalker>) continue to compile without setup work; the
pipeline checks `walker is IComponentPoseSource` and falls back to
identity anchors when not implemented — yielding the legacy behaviour
byte-for-byte.
*/
using SW2GZ.Math;

namespace SW2GZ.SwSurface.Abstractions
{
    public interface IComponentPoseSource
    {
        /// Returns the part's pose (rotation + translation) in the assembly
        /// frame. Implementations that cannot resolve the path SHOULD return
        /// Pose.Identity rather than throw; the pipeline tolerates either.
        Pose GetComponentPose(string partPath);
    }
}
