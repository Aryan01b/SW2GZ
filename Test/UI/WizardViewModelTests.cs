/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — navigation + rail-state tests for WizardViewModel. These run on net8
via the source-linked VM layer, proving the MVVM code is net-portable and
the Back/Next gating is correct.
*/
using System.Collections.Generic;
using SW2GZ.UI.Services;
using SW2GZ.UI.ViewModels;
using Xunit;

namespace SW2GZ.Test.UI
{
    public class WizardViewModelTests
    {
        // A wizard whose every step can advance: one fully-assigned link + a
        // valid output folder/package, so the gates never block navigation.
        private static WizardViewModel BuildAdvanceableWizard()
        {
            var selection = new FakeViewportSelectionService("body1");
            var links = new List<LinkDto> { new LinkDto("base_link", 1.42, "base_link.dae") };
            var wizard = new WizardViewModel(
                new NullFolderBrowserService(), selection, new NullThemeService(),
                new NullExportRunner(), links);

            // Make every step advanceable.
            wizard.OutputStep.OutputFolder = @"D:\ros2_ws\src";
            wizard.OutputStep.PackageName = "three_dof_arm";
            wizard.LinksStep.Links[0].AssignGeometryCommand.Execute(null);
            return wizard;
        }

        [Fact]
        public void StartsAtStepZero()
        {
            var wizard = new WizardViewModel();
            Assert.Equal(0, wizard.CurrentStepIndex);
            Assert.Same(wizard.ModeStep, wizard.CurrentStep);
            Assert.True(wizard.IsFirstStep);
            Assert.False(wizard.IsLastStep);
        }

        [Fact]
        public void HasFiveSteps()
        {
            var wizard = new WizardViewModel();
            Assert.Equal(5, wizard.StepCount);
        }

        [Fact]
        public void NextAdvancesWhenStepCanAdvance()
        {
            var wizard = BuildAdvanceableWizard();
            Assert.True(wizard.NextCommand.CanExecute(null));
            wizard.NextCommand.Execute(null);
            Assert.Equal(1, wizard.CurrentStepIndex);
        }

        [Fact]
        public void NextIsBlockedWhenCurrentStepCannotAdvance()
        {
            // Targets step defaults to Jazzy (supported), so move to Output which
            // is empty (folder + package blank) => cannot advance.
            var wizard = new WizardViewModel();
            wizard.NextCommand.Execute(null); // 0 -> 1 (Mode always advances)
            wizard.NextCommand.Execute(null); // 1 -> 2 (Jazzy supported)
            Assert.Equal(2, wizard.CurrentStepIndex);

            Assert.False(wizard.OutputStep.CanAdvance());
            Assert.False(wizard.NextCommand.CanExecute(null));
            wizard.NextCommand.Execute(null); // no-op
            Assert.Equal(2, wizard.CurrentStepIndex);
        }

        [Fact]
        public void TargetsStepBlocksNextForUnsupportedDistro()
        {
            var wizard = new WizardViewModel();
            wizard.NextCommand.Execute(null); // 0 -> 1
            wizard.TargetsStep.SelectedDistro = Ros2Distro.Humble;
            Assert.False(wizard.NextCommand.CanExecute(null));

            wizard.TargetsStep.SelectedDistro = Ros2Distro.Jazzy;
            Assert.True(wizard.NextCommand.CanExecute(null));
        }

        [Fact]
        public void BackNeverGoesBelowZero()
        {
            var wizard = new WizardViewModel();
            Assert.False(wizard.BackCommand.CanExecute(null));
            wizard.BackCommand.Execute(null);
            Assert.Equal(0, wizard.CurrentStepIndex);
        }

        [Fact]
        public void BackReturnsToPreviousStep()
        {
            var wizard = BuildAdvanceableWizard();
            wizard.NextCommand.Execute(null);
            Assert.Equal(1, wizard.CurrentStepIndex);
            wizard.BackCommand.Execute(null);
            Assert.Equal(0, wizard.CurrentStepIndex);
        }

        [Fact]
        public void ProgressAndCounterReflectCurrentStep()
        {
            var wizard = BuildAdvanceableWizard();
            Assert.Equal(1.0 / 5.0, wizard.Progress, 5);
            Assert.Equal("Step 1 of 5", wizard.StepCounter);

            wizard.NextCommand.Execute(null);
            Assert.Equal(2.0 / 5.0, wizard.Progress, 5);
            Assert.Equal("Step 2 of 5", wizard.StepCounter);
        }

        [Fact]
        public void ActiveAndCompleteFlagsUpdateOnNavigation()
        {
            var wizard = BuildAdvanceableWizard();
            Assert.True(wizard.ModeStep.IsActive);
            Assert.False(wizard.ModeStep.IsComplete);

            wizard.NextCommand.Execute(null); // now on Targets

            Assert.False(wizard.ModeStep.IsActive);
            Assert.True(wizard.ModeStep.IsComplete);
            Assert.True(wizard.TargetsStep.IsActive);
            Assert.False(wizard.TargetsStep.IsComplete);
        }

        [Fact]
        public void IsLastStepTrueOnReview()
        {
            var wizard = BuildAdvanceableWizard();
            wizard.NextCommand.Execute(null); // 1
            wizard.NextCommand.Execute(null); // 2
            wizard.NextCommand.Execute(null); // 3
            wizard.NextCommand.Execute(null); // 4 (Review)
            Assert.Equal(4, wizard.CurrentStepIndex);
            Assert.True(wizard.IsLastStep);
            Assert.False(wizard.NextCommand.CanExecute(null)); // last step: no next
        }
    }
}
