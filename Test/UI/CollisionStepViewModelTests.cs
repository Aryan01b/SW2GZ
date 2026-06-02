/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8+ — Collision step VM tests: default strategy, selection persistence, and
the per-strategy description.
*/
using SW2GZ.Build;
using SW2GZ.UI.ViewModels;
using Xunit;

namespace SW2GZ.Test.UI
{
    public class CollisionStepViewModelTests
    {
        [Fact]
        public void DefaultsToConvexHull()
        {
            var vm = new CollisionStepViewModel();
            Assert.Equal(ColliderStrategy.ConvexHull, vm.SelectedStrategy);
            Assert.Contains("recommended", vm.StrategyDescription);
            Assert.True(vm.CanAdvance());
        }

        [Fact]
        public void SelectionSticks()
        {
            var vm = new CollisionStepViewModel();
            vm.SelectedStrategy = ColliderStrategy.Aabb;
            Assert.Equal(ColliderStrategy.Aabb, vm.SelectedStrategy);
            Assert.Contains("box", vm.StrategyDescription);
        }

        [Fact]
        public void OffersBothStrategies()
        {
            var vm = new CollisionStepViewModel();
            Assert.Contains(ColliderStrategy.ConvexHull, vm.StrategyOptions);
            Assert.Contains(ColliderStrategy.Aabb, vm.StrategyOptions);
        }
    }
}
