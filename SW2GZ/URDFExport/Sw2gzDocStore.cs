/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Per-process in-memory map: <document path> → Sw2gzDoc. Survives PMP open/close
cycles so multiple ribbon panels (Links / Joints / Sensors / ...) share the
same live tree for a given assembly.

Keyed on ModelDoc2.GetPathName() — empty path (unsaved doc) falls back to the
doc title, accepting that two unsaved docs with the same title would collide
(rare in practice and explicitly out of scope for v2.1.0).

Persistence to a SolidWorks Attribute lands in the backend wiring plan.
*/
#if SW_INTEROP
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

namespace SW2GZ.URDFExport
{
    public static class Sw2gzDocStore
    {
        private static readonly Dictionary<string, Sw2gzDoc> _byKey =
            new Dictionary<string, Sw2gzDoc>(System.StringComparer.OrdinalIgnoreCase);

        public static Sw2gzDoc GetOrCreate(ModelDoc2 model)
        {
            string key = KeyFor(model);
            if (!_byKey.TryGetValue(key, out Sw2gzDoc doc))
            {
                doc = new Sw2gzDoc();
                _byKey[key] = doc;
            }
            return doc;
        }

        public static void Clear() => _byKey.Clear();

        /// Drop the cached doc for a single assembly, e.g. after the persisted
        /// attribute is deleted. The next GetOrCreate will return a fresh doc.
        public static void Reset(ModelDoc2 model)
        {
            _byKey.Remove(KeyFor(model));
        }

        private static string KeyFor(ModelDoc2 model)
        {
            if (model == null) return "<null>";
            string p = model.GetPathName();
            if (!string.IsNullOrEmpty(p)) return p;
            return "<unsaved>::" + model.GetTitle();
        }
    }
}
#endif
