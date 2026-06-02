/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — one row in the Links step tree. Holds the link's name + SW-derived
mass + visual mesh, and the geometry-assignment state. AssignGeometry pulls
the current viewport selection from IViewportSelectionService; ClearGeometry
resets it.
*/
using System;
using System.Collections.Generic;
using SW2GZ.UI.Mvvm;
using SW2GZ.UI.Services;

namespace SW2GZ.UI.ViewModels
{
    public sealed class LinkViewModel : ObservableObject
    {
        private readonly IViewportSelectionService _selection;
        private string _name;
        private double? _massKg;
        private string _visualMeshFile;
        private bool _hasGeometry;
        private int _selectedBodyCount;

        public LinkViewModel(LinkDto dto, IViewportSelectionService selection)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            _selection = selection ?? new NullViewportSelectionService();
            _name = dto.Name;
            _massKg = dto.MassKg;
            _visualMeshFile = dto.VisualMeshFile;

            AssignGeometryCommand = new RelayCommand(AssignGeometry);
            ClearGeometryCommand = new RelayCommand(ClearGeometry, () => _hasGeometry);
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public double? MassKg
        {
            get => _massKg;
            set => SetProperty(ref _massKg, value);
        }

        public string VisualMeshFile
        {
            get => _visualMeshFile;
            set => SetProperty(ref _visualMeshFile, value);
        }

        public bool HasGeometry
        {
            get => _hasGeometry;
            private set
            {
                if (SetProperty(ref _hasGeometry, value))
                {
                    ClearGeometryCommand.RaiseCanExecuteChanged();
                    GeometryChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public int SelectedBodyCount
        {
            get => _selectedBodyCount;
            private set => SetProperty(ref _selectedBodyCount, value);
        }

        /// Body names captured from the viewport at the moment of assignment.
        public IReadOnlyList<string> AssignedBodyNames { get; private set; } = Array.Empty<string>();

        public RelayCommand AssignGeometryCommand { get; }
        public RelayCommand ClearGeometryCommand { get; }

        /// Raised when HasGeometry flips, so the parent step can re-check its
        /// all-links-assigned gate.
        public event EventHandler GeometryChanged;

        /// Seeds the geometry state from an external source (e.g. the native
        /// PropertyManagerPage's GeometryAssignment, converted to a plain tuple).
        /// Pure-C#: no COM, no viewport read — just sets the flags so the Links
        /// step reflects what was assigned in the 3D panel.
        public void ApplyGeometry(bool hasGeometry, int bodyCount)
        {
            SelectedBodyCount = hasGeometry ? bodyCount : 0;
            HasGeometry = hasGeometry;
        }

        private void AssignGeometry()
        {
            IReadOnlyList<string> names = _selection.GetSelectedBodyNames();
            if (names == null || names.Count == 0)
                return; // nothing selected — leave state unchanged

            AssignedBodyNames = names;
            SelectedBodyCount = names.Count;
            HasGeometry = true;
        }

        private void ClearGeometry()
        {
            AssignedBodyNames = Array.Empty<string>();
            SelectedBodyCount = 0;
            HasGeometry = false;
        }
    }
}
