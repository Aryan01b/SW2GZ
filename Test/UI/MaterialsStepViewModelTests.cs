/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8+ — Materials step VM tests: per-link rows, UseMaterial gating of the built
MaterialDef list, RGBA clamping, and the hex round-trip.
*/
using System.Collections.Generic;
using SW2GZ.Build.Model;
using SW2GZ.UI.ViewModels;
using Xunit;

namespace SW2GZ.Test.UI
{
    public class MaterialsStepViewModelTests
    {
        private static MaterialsStepViewModel Build() =>
            new MaterialsStepViewModel(new List<string> { "base_link", "link1", "link2" });

        [Fact]
        public void RowPerLinkWithGrayDefault()
        {
            var vm = Build();
            Assert.Equal(3, vm.Links.Count);
            Assert.Equal(0.8, vm.Links[0].R);
            Assert.Equal(1.0, vm.Links[0].A);
            Assert.True(vm.CanAdvance());
        }

        [Fact]
        public void OnlyEnabledRowsBuildMaterials()
        {
            var vm = Build();
            Assert.Empty(vm.BuildMaterials());

            vm.Links[0].UseMaterial = true;
            vm.Links[2].UseMaterial = true;
            IReadOnlyList<MaterialDef> mats = vm.BuildMaterials();
            Assert.Equal(2, mats.Count);
            Assert.Equal(2, vm.MaterialCount);
            Assert.Equal("base_link_material", mats[0].Name);
        }

        [Fact]
        public void RgbaIsClampedToUnitRange()
        {
            var vm = Build();
            vm.Links[0].R = 1.7;
            vm.Links[0].G = -0.4;
            Assert.Equal(1.0, vm.Links[0].R);
            Assert.Equal(0.0, vm.Links[0].G);

            vm.Links[0].UseMaterial = true;
            MaterialDef m = vm.BuildMaterials()[0];
            Assert.InRange(m.R, 0.0, 1.0);
            Assert.InRange(m.G, 0.0, 1.0);
        }

        [Fact]
        public void HexColorRoundTrips()
        {
            var vm = Build();
            vm.Links[0].HexColor = "#FF8000";
            Assert.Equal(1.0, vm.Links[0].R, 2);
            Assert.Equal(0.5, vm.Links[0].G, 2);
            Assert.Equal(0.0, vm.Links[0].B, 2);

            // Setting channels updates hex back.
            vm.Links[1].R = 1.0; vm.Links[1].G = 0.0; vm.Links[1].B = 0.0; vm.Links[1].A = 1.0;
            Assert.Equal("#FF0000FF", vm.Links[1].HexColor);
        }

        [Fact]
        public void MaterialNamesByLinkOnlyEnabledRows()
        {
            var vm = Build();
            vm.Links[1].UseMaterial = true;
            vm.Links[1].MaterialName = "red";
            IReadOnlyDictionary<string, string> map = vm.MaterialNamesByLink();
            Assert.Single(map);
            Assert.Equal("red", map["link1"]);
        }
    }
}
