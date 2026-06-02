/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — Links step: geometry assignment pulls from IViewportSelectionService,
ClearGeometry resets it, and CanAdvance reflects the all-links-assigned gate.
*/
using System.Collections.Generic;
using SW2GZ.UI.ViewModels;
using Xunit;

namespace SW2GZ.Test.UI
{
    public class LinksStepViewModelTests
    {
        private static List<LinkDto> TwoLinks() => new List<LinkDto>
        {
            new LinkDto("base_link", 1.42, "base_link.dae"),
            new LinkDto("link1", 0.50, "link1.dae"),
        };

        [Fact]
        public void AssignGeometryFlipsHasGeometryAndCapturesBodies()
        {
            var selection = new FakeViewportSelectionService("body_a", "body_b");
            var step = new LinksStepViewModel(TwoLinks(), selection);
            LinkViewModel link = step.Links[0];

            Assert.False(link.HasGeometry);
            link.AssignGeometryCommand.Execute(null);

            Assert.True(link.HasGeometry);
            Assert.Equal(2, link.SelectedBodyCount);
            Assert.Equal(new[] { "body_a", "body_b" }, link.AssignedBodyNames);
        }

        [Fact]
        public void AssignGeometryWithEmptySelectionLeavesStateUnchanged()
        {
            var selection = new FakeViewportSelectionService(); // nothing selected
            var step = new LinksStepViewModel(TwoLinks(), selection);
            LinkViewModel link = step.Links[0];

            link.AssignGeometryCommand.Execute(null);

            Assert.False(link.HasGeometry);
            Assert.Equal(0, link.SelectedBodyCount);
        }

        [Fact]
        public void ClearGeometryResetsLink()
        {
            var selection = new FakeViewportSelectionService("body_a");
            var step = new LinksStepViewModel(TwoLinks(), selection);
            LinkViewModel link = step.Links[0];

            link.AssignGeometryCommand.Execute(null);
            Assert.True(link.HasGeometry);

            link.ClearGeometryCommand.Execute(null);
            Assert.False(link.HasGeometry);
            Assert.Equal(0, link.SelectedBodyCount);
            Assert.Empty(link.AssignedBodyNames);
        }

        [Fact]
        public void CanAdvanceOnlyWhenAllLinksAssigned()
        {
            var selection = new FakeViewportSelectionService("body_a");
            var step = new LinksStepViewModel(TwoLinks(), selection);

            Assert.False(step.CanAdvance());

            step.Links[0].AssignGeometryCommand.Execute(null);
            Assert.False(step.CanAdvance()); // one still unassigned
            Assert.Equal(1, step.AssignedGeometryCount);

            step.Links[1].AssignGeometryCommand.Execute(null);
            Assert.True(step.CanAdvance());
            Assert.Equal(2, step.AssignedGeometryCount);
            Assert.True(step.AllLinksHaveGeometry);
        }

        [Fact]
        public void EmptyLinkListCannotAdvance()
        {
            var step = new LinksStepViewModel(new List<LinkDto>(), new FakeViewportSelectionService());
            Assert.False(step.CanAdvance());
            Assert.Equal(0, step.LinkCount);
        }

        [Fact]
        public void FirstLinkSelectedByDefault()
        {
            var step = new LinksStepViewModel(TwoLinks(), new FakeViewportSelectionService());
            Assert.NotNull(step.SelectedLink);
            Assert.Equal("base_link", step.SelectedLink.Name);
        }
    }
}
