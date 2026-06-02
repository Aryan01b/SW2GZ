/*
Representative fake robot so every wizard page has something to show in the
standalone preview harness. No SolidWorks involved — plain DTOs.

  Links : base_link, link1, link2, tool0 (masses + a visual mesh filename)
  Joints: joint1 base_link->link1 (Revolute)
          joint2 link1->link2     (Revolute)
          joint3 link2->tool0     (Continuous)

previewModel is intentionally null: it only gates the Review step's "run
export" action, which the NullExportRunner would no-op anyway.
*/
using System.Collections.Generic;
using System.Numerics;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using SW2GZ.UI.ViewModels;

namespace WizardPreview
{
    internal static class SampleData
    {
        public static readonly IReadOnlyList<LinkDto> Links = new List<LinkDto>
        {
            new LinkDto("base_link", 4.20, "base_link.stl"),
            new LinkDto("link1",     2.75, "link1.stl"),
            new LinkDto("link2",     1.90, "link2.stl"),
            new LinkDto("tool0",     0.45, "tool0.stl"),
        };

        public static readonly IReadOnlyList<JointDto> Joints = new List<JointDto>
        {
            new JointDto(
                Name: "joint1", Type: UrdfJointType.Revolute,
                ParentLink: "base_link", ChildLink: "link1",
                Origin: new Pose(new Vector3(0f, 0f, 0.10f), Quaternion.Identity),
                Axis: new Vector3(0f, 0f, 1f),
                LimitLower: -3.14, LimitUpper: 3.14,
                LimitEffort: 150.0, LimitVelocity: 3.0,
                Interface: UrdfCmdInterface.Position),

            new JointDto(
                Name: "joint2", Type: UrdfJointType.Revolute,
                ParentLink: "link1", ChildLink: "link2",
                Origin: new Pose(new Vector3(0f, 0f, 0.25f), Quaternion.Identity),
                Axis: new Vector3(0f, 1f, 0f),
                LimitLower: -1.57, LimitUpper: 1.57,
                LimitEffort: 90.0, LimitVelocity: 2.5,
                Interface: UrdfCmdInterface.Position),

            new JointDto(
                Name: "joint3", Type: UrdfJointType.Continuous,
                ParentLink: "link2", ChildLink: "tool0",
                Origin: new Pose(new Vector3(0f, 0f, 0.20f), Quaternion.Identity),
                Axis: new Vector3(1f, 0f, 0f),
                LimitLower: null, LimitUpper: null,
                LimitEffort: 30.0, LimitVelocity: 6.0,
                Interface: UrdfCmdInterface.Velocity),
        };

        public static readonly RobotModel PreviewModel = null;
    }
}
