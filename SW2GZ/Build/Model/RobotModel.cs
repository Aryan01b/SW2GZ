/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P1 — RobotModel keystone: the single immutable domain aggregate built
once by RobotModelBuilder and consumed by every serializer. Supersedes
the legacy UrdfRobot record (which is kept around for back-compat with
older callers).

Shape (per docs/superpowers/specs/2026-06-01-robust-exporter-architecture.md §3.1):
  RobotModel
   ├─ Meta         (Package, Author, Email, License, CoordinateConvention)
   ├─ Links[]      (ModelLink: UrdfLink + optional MaterialRef + optional GazeboLinkProps)
   ├─ Joints[]     (UrdfJoint)
   ├─ Materials[]  (named, rgba — empty in v2.1)
   ├─ Sensors[]    (placeholder — empty in v2.1, populated in P6)
   └─ Control      (placeholder ControlSpec — minimal in v2.1, populated in P2)
*/
using System.Collections.Generic;
using SW2GZ.Build.Urdf;

namespace SW2GZ.Build.Model
{
    public sealed record RobotModel(
        RobotMeta Meta,
        IReadOnlyList<ModelLink> Links,
        IReadOnlyList<UrdfJoint> Joints,
        IReadOnlyList<MaterialDef> Materials,
        IReadOnlyList<SensorDef> Sensors,
        ControlSpec Control);
}
