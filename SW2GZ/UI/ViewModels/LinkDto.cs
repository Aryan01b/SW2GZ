/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — lightweight transport for link data into the wizard. The COM layer
(or a test) builds these from LinkSpec/UrdfLink; the LinksStepViewModel turns
each into a LinkViewModel. Keeping a plain DTO here means the VM layer never
touches SolidWorks types and stays net-portable.
*/
namespace SW2GZ.UI.ViewModels
{
    public sealed record LinkDto(string Name, double? MassKg, string VisualMeshFile);
}
