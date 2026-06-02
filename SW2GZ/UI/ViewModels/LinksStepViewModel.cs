/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — Step 4. Link tree + selected-link geometry assignment. Populated from
a caller-supplied LinkDto list (the VM never touches COM); each DTO becomes a
LinkViewModel sharing the injected IViewportSelectionService. CanAdvance is
true once every link has geometry assigned — Finish is gated on the same.
*/
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SW2GZ.UI.Services;

namespace SW2GZ.UI.ViewModels
{
    public sealed class LinksStepViewModel : StepViewModelBase
    {
        private LinkViewModel _selectedLink;

        public LinksStepViewModel(
            IReadOnlyList<LinkDto> links,
            IViewportSelectionService selection)
            : base("Links & meshes", "Pick geometry")
        {
            selection ??= new NullViewportSelectionService();
            Links = new ObservableCollection<LinkViewModel>();

            if (links != null)
            {
                foreach (LinkDto dto in links)
                {
                    var vm = new LinkViewModel(dto, selection);
                    vm.GeometryChanged += (s, e) =>
                    {
                        OnPropertyChanged(nameof(AssignedGeometryCount));
                        OnPropertyChanged(nameof(AllLinksHaveGeometry));
                        OnAdvanceabilityChanged();
                    };
                    Links.Add(vm);
                }
            }

            _selectedLink = Links.FirstOrDefault();
        }

        public ObservableCollection<LinkViewModel> Links { get; }

        public LinkViewModel SelectedLink
        {
            get => _selectedLink;
            set => SetProperty(ref _selectedLink, value);
        }

        public int LinkCount => Links.Count;

        public int AssignedGeometryCount => Links.Count(l => l.HasGeometry);

        public bool AllLinksHaveGeometry =>
            Links.Count > 0 && Links.All(l => l.HasGeometry);

        /// Seeds/refreshes each link's geometry state from an external source —
        /// e.g. the native geometry PropertyManagerPage's GeometryAssignment,
        /// converted to a simple (LinkName, HasGeometry, BodyCount) tuple list so
        /// this stays pure-C# (no COM). Links are matched by name; entries with no
        /// matching LinkViewModel are ignored.
        public void ApplyGeometry(
            IReadOnlyList<(string LinkName, bool HasGeometry, int BodyCount)> state)
        {
            if (state == null) return;
            foreach ((string linkName, bool hasGeometry, int bodyCount) in state)
            {
                LinkViewModel link = Links.FirstOrDefault(
                    l => string.Equals(l.Name, linkName, System.StringComparison.Ordinal));
                link?.ApplyGeometry(hasGeometry, bodyCount);
            }
            OnPropertyChanged(nameof(AssignedGeometryCount));
            OnPropertyChanged(nameof(AllLinksHaveGeometry));
            OnAdvanceabilityChanged();
        }

        // Gate Next/Finish on every link having geometry. (Empty link list is
        // treated as not-yet-ready so the wizard can't advance an empty model.)
        public override bool CanAdvance() => AllLinksHaveGeometry;
    }
}
