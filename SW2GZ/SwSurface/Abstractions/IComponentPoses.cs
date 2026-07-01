/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Raw SolidWorks Component2.Transform2 pose (assembly-frame rotation +
translation) for a given component, read via the verified COLUMN-major
convention (see memory sw-mathtransform-column-major — confirmed against
Component2.GetBox ground truth). Lets exporters compute the exact relative
parent/child joint transform instead of approximating one from mesh bounding
boxes.
*/
using System.Numerics;
using SW2GZ.Math;

namespace SW2GZ.SwSurface.Abstractions
{
    public interface IComponentPoses
    {
        (Matrix3 Rotation, Vector3 Translation) GetPose(string componentPathName);
    }
}
