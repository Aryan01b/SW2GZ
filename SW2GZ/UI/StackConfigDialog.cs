/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

StackConfigDialog — one parameterized modal that configures a single "stack"
(Actuation / Sensors / Gazebo / Bridge) of a StackProfile. The ribbon's Stacks
section opens this with a StackTarget telling it which slice to show.

Clone-then-commit pattern: the ctor takes the caller's live StackProfile and
immediately deep-copies it into `_working`. Every control edits `_working`, never
the caller's object. Only when the user clicks OK do we publish `_working` to
`Result`; on Cancel `Result` stays null, so the caller's profile is untouched —
editing and cancelling never mutates the source.

Controls are built programmatically in the ctor (no .Designer/.resx), mirroring
ExportDialog. Like ExportDialog this file is guarded by SW_INTEROP because it
pulls in System.Windows.Forms; it only compiles into the SW-bound add-in build.
*/
#if SW_INTEROP
using System;
using System.Windows.Forms;
using SW2GZ.Ros2;

namespace SW2GZ.UI
{
    // Which slice of the StackProfile this dialog instance edits. Public so the
    // (next-task) ribbon handlers can pick a target per button.
    public enum StackTarget { Actuation, Sensors, Gazebo, Bridge }

    public sealed class StackConfigDialog : Form
    {
        // The edited copy. Never the caller's instance — see clone-then-commit above.
        private readonly StackProfile _working;
        private readonly StackTarget _target;

        // Actuation radios, kept as a field so OK can read the selected index.
        private RadioButton _rbNone, _rbGzPlugin, _rbRos2Control;
        // Sensors / Gazebo single checkboxes.
        private CheckBox _sensorsEnabled, _gzSim;
        // Bridge per-topic checkboxes.
        private CheckBox _clock, _tf, _jointStates, _cmdVel, _odom;

        // Set to the edited clone only on OK. Null while the user is still editing
        // or after Cancel, which is the signal "no change committed".
        public StackProfile Result { get; private set; }

        public StackConfigDialog(StackTarget target, StackProfile current)
        {
            _target = target;
            // Deep clone up front: from here on, the caller's profile is read-only to us.
            _working = new StackProfile(current);

            Text = "Configure: " + target;
            Width = 380; Height = 280;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false; MinimizeBox = false;

            // Build only the controls relevant to this target.
            switch (target)
            {
                case StackTarget.Actuation: BuildActuation(); break;
                case StackTarget.Sensors:   BuildSensors();   break;
                case StackTarget.Gazebo:    BuildGazebo();    break;
                case StackTarget.Bridge:    BuildBridge();    break;
            }

            BuildButtons();
        }

        // Actuation: 3 mutually-exclusive radios in the order None / Gz plugin /
        // ros2_control. A GroupBox makes the radios a single exclusive group.
        // Initial selection comes from the pure StackConfigMap so the radio order
        // can't drift from the enum without the unit test catching it.
        private void BuildActuation()
        {
            var box = new GroupBox { Left = 12, Top = 12, Width = 344, Height = 130, Text = "Actuation backend" };
            _rbNone        = new RadioButton { Left = 16, Top = 28, Width = 300, Text = "None" };
            _rbGzPlugin    = new RadioButton { Left = 16, Top = 56, Width = 300, Text = "Gz plugin" };
            _rbRos2Control = new RadioButton { Left = 16, Top = 84, Width = 300, Text = "ros2_control" };
            box.Controls.Add(_rbNone);
            box.Controls.Add(_rbGzPlugin);
            box.Controls.Add(_rbRos2Control);
            Controls.Add(box);

            switch (StackConfigMap.RadioIndexForBackend(_working.Actuation))
            {
                case 1:  _rbGzPlugin.Checked = true;    break;
                case 2:  _rbRos2Control.Checked = true; break;
                default: _rbNone.Checked = true;        break;
            }
        }

        // Sensors: single enable toggle plus a forward-looking note about the
        // detail step that lands in a later phase.
        private void BuildSensors()
        {
            _sensorsEnabled = new CheckBox
            {
                Left = 16, Top = 16, Width = 330, Text = "Enable sensors", Checked = _working.SensorsEnabled,
            };
            Controls.Add(_sensorsEnabled);
            Controls.Add(new Label
            {
                Left = 16, Top = 46, Width = 330, Height = 32,
                Text = "Per-sensor placement & rates configured in a later step.",
            });
        }

        // Gazebo: master sim toggle plus a forward-looking note about world options.
        private void BuildGazebo()
        {
            _gzSim = new CheckBox
            {
                Left = 16, Top = 16, Width = 330, Text = "Build for Gazebo simulation", Checked = _working.GzSim,
            };
            Controls.Add(_gzSim);
            Controls.Add(new Label
            {
                Left = 16, Top = 46, Width = 330, Height = 32,
                Text = "World physics/ground/sun options configured in a later step.",
            });
        }

        // Bridge: one checkbox per ros_gz_bridge topic, each seeded from the
        // corresponding BridgePlan flag on the working clone.
        private void BuildBridge()
        {
            _clock       = new CheckBox { Left = 16, Top = 12, Width = 330, Text = "clock",        Checked = _working.Bridge.Clock };
            _tf          = new CheckBox { Left = 16, Top = 38, Width = 330, Text = "tf",           Checked = _working.Bridge.Tf };
            _jointStates = new CheckBox { Left = 16, Top = 64, Width = 330, Text = "joint_states", Checked = _working.Bridge.JointStates };
            _cmdVel      = new CheckBox { Left = 16, Top = 90, Width = 330, Text = "cmd_vel",      Checked = _working.Bridge.CmdVel };
            _odom        = new CheckBox { Left = 16, Top = 116, Width = 330, Text = "odom",        Checked = _working.Bridge.Odom };
            Controls.Add(_clock);
            Controls.Add(_tf);
            Controls.Add(_jointStates);
            Controls.Add(_cmdVel);
            Controls.Add(_odom);
        }

        // OK / Cancel, wired as AcceptButton/CancelButton. OK reads the controls
        // back into the working clone, then publishes it to Result. Cancel leaves
        // Result null, so the caller's original profile is never mutated.
        private void BuildButtons()
        {
            int y = 200;
            var ok     = new Button { Text = "OK",     Left = 188, Top = y, Width = 80, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 276, Top = y, Width = 80, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) =>
            {
                CommitToWorking();
                Result = _working;
            };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        // Pull the live control state into _working for this dialog's target only.
        private void CommitToWorking()
        {
            switch (_target)
            {
                case StackTarget.Actuation:
                    int idx = _rbGzPlugin.Checked ? 1 : _rbRos2Control.Checked ? 2 : 0;
                    _working.Actuation = StackConfigMap.BackendForRadioIndex(idx);
                    break;
                case StackTarget.Sensors:
                    _working.SensorsEnabled = _sensorsEnabled.Checked;
                    break;
                case StackTarget.Gazebo:
                    _working.GzSim = _gzSim.Checked;
                    break;
                case StackTarget.Bridge:
                    _working.Bridge.Clock       = _clock.Checked;
                    _working.Bridge.Tf          = _tf.Checked;
                    _working.Bridge.JointStates = _jointStates.Checked;
                    _working.Bridge.CmdVel      = _cmdVel.Checked;
                    _working.Bridge.Odom        = _odom.Checked;
                    break;
            }
        }
    }
}
#endif
