/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — Targets step: distro→Gz pairing, v2.1 support gating, and the
PropertyChanged notifications that drive the read-only Gz field.
*/
using System.Collections.Generic;
using System.ComponentModel;
using SW2GZ.UI.ViewModels;
using Xunit;

namespace SW2GZ.Test.UI
{
    public class TargetsStepViewModelTests
    {
        [Fact]
        public void JazzyPairsHarmonicAndIsSupported()
        {
            var vm = new TargetsStepViewModel { SelectedDistro = Ros2Distro.Jazzy };
            Assert.Equal("Harmonic", vm.GzVersion);
            Assert.True(vm.IsSupported);
            Assert.True(vm.CanAdvance());
            Assert.Equal(string.Empty, vm.SupportNote);
            Assert.Equal("Jazzy + Harmonic", vm.TargetSummary);
        }

        [Fact]
        public void HumblePairsFortressAndIsNotSupported()
        {
            var vm = new TargetsStepViewModel { SelectedDistro = Ros2Distro.Humble };
            Assert.Equal("Fortress", vm.GzVersion);
            Assert.False(vm.IsSupported);
            Assert.False(vm.CanAdvance());
            Assert.False(string.IsNullOrEmpty(vm.SupportNote));
        }

        [Theory]
        [InlineData(Ros2Distro.Humble, "Fortress")]
        [InlineData(Ros2Distro.Jazzy, "Harmonic")]
        [InlineData(Ros2Distro.Kilted, "Ionic")]
        [InlineData(Ros2Distro.Rolling, "Ionic")]
        public void GzPairingMapIsCorrect(Ros2Distro distro, string expectedGz)
        {
            var vm = new TargetsStepViewModel { SelectedDistro = distro };
            Assert.Equal(expectedGz, vm.GzVersion);
        }

        [Fact]
        public void ChangingDistroRaisesPropertyChangedForGzVersion()
        {
            var vm = new TargetsStepViewModel { SelectedDistro = Ros2Distro.Jazzy };
            var changed = new List<string>();
            ((INotifyPropertyChanged)vm).PropertyChanged += (s, e) => changed.Add(e.PropertyName);

            vm.SelectedDistro = Ros2Distro.Humble;

            Assert.Contains(nameof(TargetsStepViewModel.GzVersion), changed);
            Assert.Contains(nameof(TargetsStepViewModel.IsSupported), changed);
            Assert.Contains(nameof(TargetsStepViewModel.SupportNote), changed);
        }
    }
}
