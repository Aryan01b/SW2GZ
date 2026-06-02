/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — wizard shell code-behind. REQUIRES VISUAL STUDIO BUILD (markup compiler).
Construct with a WizardViewModel; the window simply hosts it as DataContext.
*/
using System.Windows;
using SW2GZ.UI.ViewModels;

namespace SW2GZ.UI.Wizard
{
    public partial class WizardWindow : Window
    {
        public WizardWindow()
        {
            InitializeComponent();
        }

        public WizardWindow(WizardViewModel viewModel) : this()
        {
            DataContext = viewModel;
        }
    }
}
