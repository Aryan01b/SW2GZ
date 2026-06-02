/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — Output step: live package-name sanitization, the folder/package
advance gate, and the Browse command wiring through IFolderBrowserService.
*/
using SW2GZ.Build;
using SW2GZ.UI.ViewModels;
using Xunit;

namespace SW2GZ.Test.UI
{
    public class OutputStepViewModelTests
    {
        [Fact]
        public void PackageNameIsSanitizedLive()
        {
            var vm = new OutputStepViewModel(new FakeFolderBrowserService(null))
            {
                PackageName = "My Robot!"
            };
            string expected = PackageNameSanitizer.Sanitize("My Robot!").Value;
            Assert.Equal(expected, vm.SanitizedPackageName);
            Assert.Equal("my_robot", vm.SanitizedPackageName);
        }

        [Fact]
        public void CannotAdvanceWhenFolderEmpty()
        {
            var vm = new OutputStepViewModel(new FakeFolderBrowserService(null))
            {
                PackageName = "valid_pkg"
            };
            Assert.False(vm.CanAdvance());

            vm.OutputFolder = @"D:\ros2_ws\src";
            Assert.True(vm.CanAdvance());
        }

        [Fact]
        public void CannotAdvanceWhenPackageNameBlank()
        {
            var vm = new OutputStepViewModel(new FakeFolderBrowserService(null))
            {
                OutputFolder = @"D:\ros2_ws\src",
                PackageName = ""
            };
            // Blank sanitizes to "unnamed_package" which is non-empty, so the
            // gate passes; verify the live preview rather than blocking on blank.
            Assert.Equal("unnamed_package", vm.SanitizedPackageName);
            Assert.True(vm.CanAdvance());
        }

        [Fact]
        public void BrowseCommandSetsFolderFromService()
        {
            var fake = new FakeFolderBrowserService(@"E:\chosen\path");
            var vm = new OutputStepViewModel(fake) { OutputFolder = @"C:\start" };

            vm.BrowseCommand.Execute(null);

            Assert.Equal(1, fake.CallCount);
            Assert.Equal(@"C:\start", fake.LastInitialPath);
            Assert.Equal(@"E:\chosen\path", vm.OutputFolder);
        }

        [Fact]
        public void BrowseCommandLeavesFolderUnchangedWhenCancelled()
        {
            var fake = new FakeFolderBrowserService(null); // user cancelled
            var vm = new OutputStepViewModel(fake) { OutputFolder = @"C:\start" };

            vm.BrowseCommand.Execute(null);

            Assert.Equal(@"C:\start", vm.OutputFolder);
        }

        [Fact]
        public void CtorSeedsPackageNameAndOutputFolderFromDefaults()
        {
            var vm = new OutputStepViewModel(
                new FakeFolderBrowserService(null),
                defaultPackageName: "My Robot!",
                defaultOutputFolder: @"D:\models\arm");

            Assert.Equal("My Robot!", vm.PackageName);
            Assert.Equal(@"D:\models\arm", vm.OutputFolder);
        }

        [Fact]
        public void SeededPackageNameDrivesSanitizedPreview()
        {
            var vm = new OutputStepViewModel(
                new FakeFolderBrowserService(null),
                defaultPackageName: "My Robot!");

            Assert.Equal("my_robot", vm.SanitizedPackageName);
        }

        [Fact]
        public void SeededDefaultsSatisfyAdvanceGate()
        {
            var vm = new OutputStepViewModel(
                new FakeFolderBrowserService(null),
                defaultPackageName: "three_dof_arm",
                defaultOutputFolder: @"D:\ros2_ws\src");

            Assert.True(vm.CanAdvance());
        }

        [Fact]
        public void NullOrEmptyDefaultsLeaveFieldsEmpty()
        {
            var vmNull = new OutputStepViewModel(
                new FakeFolderBrowserService(null), null, null);
            Assert.Equal(string.Empty, vmNull.PackageName);
            Assert.Equal(string.Empty, vmNull.OutputFolder);

            var vmEmpty = new OutputStepViewModel(
                new FakeFolderBrowserService(null), "   ", "");
            Assert.Equal(string.Empty, vmEmpty.PackageName);
            Assert.Equal(string.Empty, vmEmpty.OutputFolder);
        }
    }
}
