/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Skeleton — Task 28 wires the actual SldWorks IMassProperty call.
For now Get() throws NotImplementedException unless the cache has been
seeded (used in tests to verify the caching contract independently of
the SW dependency).
*/
using System.Collections.Generic;
using SW2GZ.Build;
using SW2GZ.SwSurface.Abstractions;

namespace SW2GZ.SwSurface
{
    public sealed class SolidWorksMassProperties : IMassProperties
    {
        private readonly Dictionary<string, MassProps> _cache = new Dictionary<string, MassProps>();

        public MassProps Get(string componentPathName)
        {
            if (_cache.TryGetValue(componentPathName, out var cached)) return cached;
            throw new System.NotImplementedException(
                "SolidWorksMassProperties.Get() not yet wired to SldWorks API — see Task 28.");
        }

        // Cache-seeder for tests (and eventually for the T28 SW-invocation hot path).
        internal void Seed(string path, MassProps props) => _cache[path] = props;
    }
}
