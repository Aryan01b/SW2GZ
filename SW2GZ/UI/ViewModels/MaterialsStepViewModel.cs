/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8+ — Step 7. One row per link; toggle UseMaterial to attach an RGBA override
material to that link (default neutral gray). BuildMaterials() returns one
MaterialDef per enabled row. Always advanceable (materials optional). RGBA is
clamped 0..1 at the channel setters, so builds are always in range.
*/
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SW2GZ.Build.Model;

namespace SW2GZ.UI.ViewModels
{
    public sealed class MaterialsStepViewModel : StepViewModelBase
    {
        public MaterialsStepViewModel(IReadOnlyList<string> linkNames)
            : base("Materials", "Colors & names")
        {
            Links = new ObservableCollection<LinkMaterialViewModel>();
            if (linkNames != null)
                foreach (string name in linkNames)
                    Links.Add(new LinkMaterialViewModel(name));
        }

        public ObservableCollection<LinkMaterialViewModel> Links { get; }

        /// Number of links with an override material enabled.
        public int MaterialCount => Links.Count(l => l.UseMaterial);

        public override bool CanAdvance() => true;

        /// One MaterialDef per link whose UseMaterial is set.
        public IReadOnlyList<MaterialDef> BuildMaterials() =>
            Links.Where(l => l.UseMaterial).Select(l => l.BuildMaterial()).ToList();

        /// link name → chosen material name, for enabled rows (Review display).
        public IReadOnlyDictionary<string, string> MaterialNamesByLink() =>
            Links.Where(l => l.UseMaterial)
                 .ToDictionary(l => l.LinkName, l => l.MaterialName);
    }
}
