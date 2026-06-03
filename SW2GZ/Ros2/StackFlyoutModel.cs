/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

StackFlyoutModel — the pure, COM-free brain of the ribbon "Stacks" flyout. It
maps a StackProfile to per-item check state (IsChecked) and applies a flyout
click to produce a NEW profile (Apply). Keeping this logic out of the SolidWorks
COM layer means the flyout's behaviour is fully unit-tested; SwAddin only does
the thin COM glue (render items, load/save the active assembly's profile).

Actuation is a radio: the three Actuation* items are mutually exclusive because
they all write the single StackProfile.Actuation enum (selecting one implicitly
deselects the others). GazeboSim and Sensors are independent toggles. Bridge is
auto-derived downstream and is intentionally NOT a flyout item in v1.
*/
namespace SW2GZ.Ros2
{
    // The selectable rows in the Stacks flyout, in display order.
    public enum StackFlyoutItem
    {
        GazeboSim,             // toggle: build for Gz simulation
        ActuationNone,         // radio: no actuation backend
        ActuationGzPlugin,     // radio: Gz native plugins
        ActuationRos2Control,  // radio: gz_ros2_control
        Sensors,               // toggle: emit Gz sensor blocks + bridge entries
    }

    public static class StackFlyoutModel
    {
        // Whether the given flyout row should render with a checkmark for this
        // profile. Toggles reflect their bool; actuation rows reflect equality
        // with the profile's single Actuation backend.
        public static bool IsChecked(StackProfile p, StackFlyoutItem item)
        {
            switch (item)
            {
                case StackFlyoutItem.GazeboSim:            return p.GzSim;
                case StackFlyoutItem.ActuationNone:        return p.Actuation == ActuationBackend.None;
                case StackFlyoutItem.ActuationGzPlugin:    return p.Actuation == ActuationBackend.GzPlugin;
                case StackFlyoutItem.ActuationRos2Control: return p.Actuation == ActuationBackend.Ros2Control;
                case StackFlyoutItem.Sensors:             return p.SensorsEnabled;
                default:                                  return false;
            }
        }

        // Apply a click on `item` to `p`, returning a NEW profile (input is never
        // mutated — callers persist the result). Toggle rows flip their bool;
        // actuation rows set the single backend (radio behaviour).
        public static StackProfile Apply(StackProfile p, StackFlyoutItem item)
        {
            // Copy every field so the returned profile is independent of the input.
            var next = new StackProfile
            {
                GzSim = p.GzSim,
                Actuation = p.Actuation,
                SensorsEnabled = p.SensorsEnabled,
            };

            switch (item)
            {
                case StackFlyoutItem.GazeboSim:            next.GzSim = !p.GzSim; break;
                case StackFlyoutItem.Sensors:             next.SensorsEnabled = !p.SensorsEnabled; break;
                case StackFlyoutItem.ActuationNone:        next.Actuation = ActuationBackend.None; break;
                case StackFlyoutItem.ActuationGzPlugin:    next.Actuation = ActuationBackend.GzPlugin; break;
                case StackFlyoutItem.ActuationRos2Control: next.Actuation = ActuationBackend.Ros2Control; break;
            }
            return next;
        }

        // Human-readable row label shown in the flyout.
        public static string Label(StackFlyoutItem item)
        {
            switch (item)
            {
                case StackFlyoutItem.GazeboSim:            return "Gazebo sim";
                case StackFlyoutItem.ActuationNone:        return "Actuation: none";
                case StackFlyoutItem.ActuationGzPlugin:    return "Actuation: Gz plugin";
                case StackFlyoutItem.ActuationRos2Control: return "Actuation: ros2_control";
                case StackFlyoutItem.Sensors:             return "Sensors";
                default:                                  return item.ToString();
            }
        }
    }
}
