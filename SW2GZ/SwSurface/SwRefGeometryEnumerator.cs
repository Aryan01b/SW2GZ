/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

D4 — Walks SolidWorks' FeatureManager (FirstFeature / GetNextFeature) and
collects Reference Coordinate System ("CoordSys") and Reference Axis
("RefAxis") feature names. The Create-Robot PMP populates its joint-side
Reference-CS and Reference-Axis comboboxes from these lists.

The walker operates on any ModelDoc2 — typically a child Component2's part
model (component.GetModelDoc2()) when populating the per-joint pickers, or
the assembly itself when the user picked a joint whose child has no
distinguishable per-part reference geometry.
*/
#if SW_INTEROP
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

namespace SW2GZ.SwSurface
{
    public static class SwRefGeometryEnumerator
    {
        /// Reference Coordinate System feature names on the given model.
        /// Empty list when none / null model.
        public static List<string> CoordinateSystems(ModelDoc2 model) =>
            CollectByTypeName(model, "CoordSys");

        /// Reference Axis feature names on the given model. Empty list when
        /// none / null model.
        public static List<string> ReferenceAxes(ModelDoc2 model) =>
            CollectByTypeName(model, "RefAxis");

        private static List<string> CollectByTypeName(ModelDoc2 model, string typeName)
        {
            var names = new List<string>();
            if (model == null) return names;
            try
            {
                Feature f = (Feature)model.FirstFeature();
                while (f != null)
                {
                    string t = f.GetTypeName2();
                    if (string.Equals(t, typeName, System.StringComparison.Ordinal))
                    {
                        string n = f.Name;
                        if (!string.IsNullOrEmpty(n)) names.Add(n);
                    }
                    f = (Feature)f.GetNextFeature();
                }
            }
            catch { /* swallowed — empty list signals "nothing usable here" */ }
            return names;
        }
    }
}
#endif
