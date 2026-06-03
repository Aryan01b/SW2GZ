/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

StackRibbonGate — pure rule for whether the ribbon "Stacks" section buttons
(Actuation / Sensors / Gazebo / Bridge) are enabled. They tune a ROBOT PACKAGE
built from a saved model, so they require:
  * a saved model — Create Model has run, i.e. config.Links is non-empty, and
  * RobotPackage mode — the stack tuning is meaningless for the SdfModel (asset)
    and SdfWorld (world) export targets, so the buttons are greyed there.
The active-document-is-assembly check stays in the COM layer (SwAddin).
*/
using SW2GZ.URDFExport;

namespace SW2GZ.Ros2
{
    public static class StackRibbonGate
    {
        public static bool IsEnabled(Sw2gzExportConfig config)
        {
            if (config == null) return false;
            if (config.Mode != ExportMode.RobotPackage) return false;
            return config.Links != null && config.Links.Count > 0;
        }
    }
}
