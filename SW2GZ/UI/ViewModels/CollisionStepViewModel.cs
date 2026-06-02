/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8+ — Step 6. Picks the collision-mesh strategy applied to every link. Two
choices map straight to Build.ColliderStrategy. Defaults to ConvexHull
(the recommended tight fit). Always advanceable.
*/
using System.Collections.Generic;
using SW2GZ.Build;

namespace SW2GZ.UI.ViewModels
{
    public sealed class CollisionStepViewModel : StepViewModelBase
    {
        private static readonly ColliderStrategy[] _strategyOptions =
            (ColliderStrategy[])System.Enum.GetValues(typeof(ColliderStrategy));

        private ColliderStrategy _selectedStrategy = ColliderStrategy.ConvexHull;

        public CollisionStepViewModel() : base("Collision", "Collider strategy") { }

        public IReadOnlyList<ColliderStrategy> StrategyOptions => _strategyOptions;

        public ColliderStrategy SelectedStrategy
        {
            get => _selectedStrategy;
            set
            {
                if (SetProperty(ref _selectedStrategy, value))
                    OnPropertyChanged(nameof(StrategyDescription));
            }
        }

        /// One-line rationale for the current pick, shown under the selector.
        public string StrategyDescription => Describe(_selectedStrategy);

        public override bool CanAdvance() => true;

        public static string Describe(ColliderStrategy strategy)
        {
            switch (strategy)
            {
                case ColliderStrategy.ConvexHull: return "tight, accurate, recommended";
                case ColliderStrategy.Aabb: return "fast box, loose fit";
                default: return strategy.ToString();
            }
        }
    }
}
