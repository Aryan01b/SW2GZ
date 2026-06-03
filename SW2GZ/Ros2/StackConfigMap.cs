/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

StackConfigMap — pure mapping between the Actuation radio-button index in the
StackConfigDialog and the ActuationBackend enum. Kept out of the WinForms file so
the radio ordering is unit-tested and can't silently drift from the enum.
*/
namespace SW2GZ.Ros2
{
    public static class StackConfigMap
    {
        // Radio order shown in the Actuation dialog: 0 None, 1 Gz plugin, 2 ros2_control.
        public static ActuationBackend BackendForRadioIndex(int idx)
        {
            switch (idx)
            {
                case 1:  return ActuationBackend.GzPlugin;
                case 2:  return ActuationBackend.Ros2Control;
                default: return ActuationBackend.None;
            }
        }

        public static int RadioIndexForBackend(ActuationBackend b)
        {
            switch (b)
            {
                case ActuationBackend.GzPlugin:    return 1;
                case ActuationBackend.Ros2Control: return 2;
                default:                            return 0;
            }
        }
    }
}
