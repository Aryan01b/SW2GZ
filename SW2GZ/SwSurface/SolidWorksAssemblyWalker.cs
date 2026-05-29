/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Task 28: wires actual IAssemblyDoc.GetComponents traversal.
The parameterless ctor preserves the original skeleton behaviour —
WalkActive() throws NotImplementedException when no SW handle is present.

SW_INTEROP is defined when building SW2GZ.csproj (which has the COM
references). It is NOT defined when building the xunit test project
(net8.0, no COM refs), allowing the same source file to compile in both.
*/
using System.Collections.Generic;
using SW2GZ.SwSurface.Abstractions;

#if SW_INTEROP
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
#endif

namespace SW2GZ.SwSurface
{
    public sealed class SolidWorksAssemblyWalker : IAssemblyWalker
    {
#if SW_INTEROP
        private readonly AssemblyDoc _doc;
#endif

        // Skeleton ctor — preserves Moq test: WalkActive() throws NotImplementedException.
        public SolidWorksAssemblyWalker() { }

#if SW_INTEROP
        // Real ctor for production use.
        public SolidWorksAssemblyWalker(AssemblyDoc doc)
        {
            _doc = doc;
        }
#endif

        public IReadOnlyList<LinkSpec> WalkActive()
        {
#if SW_INTEROP
            if (_doc == null)
#endif
                throw new System.NotImplementedException(
                    "SolidWorksAssemblyWalker.WalkActive() not yet wired to SldWorks API — see Task 28.");

#if SW_INTEROP
            object[] topLevel = (object[])_doc.GetComponents(false);
            var result = new List<LinkSpec>();

            if (topLevel == null) return result.AsReadOnly();

            foreach (object obj in topLevel)
            {
                Component2 topComp = (Component2)obj;

                string rawName = topComp.Name2;
                string sanitized = SanitizeComponentName(rawName);

                var partPaths = new List<string>();
                CollectLeafPaths(topComp, partPaths);

                result.Add(new LinkSpec(sanitized, partPaths.AsReadOnly()));
            }

            return result.AsReadOnly();
#endif
        }

#if SW_INTEROP
        // Lowercase, replace non-[a-z0-9_] with underscore, prefix _ if leading digit.
        private static string SanitizeComponentName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "unnamed_link";
            string s = name.ToLowerInvariant();
            s = Regex.Replace(s, "[^a-z0-9_]", "_");
            if (s.Length == 0) return "unnamed_link";
            if (char.IsDigit(s[0])) s = "_" + s;
            return s;
        }

        // Recursively collect leaf-component GetPathName() values.
        // A leaf is a component whose children are null/empty AND whose model is a part doc.
        private static void CollectLeafPaths(Component2 comp, List<string> paths)
        {
            object[] children = (object[])comp.GetChildren();

            bool hasChildren = children != null && children.Length > 0;
            if (!hasChildren)
            {
                // It's a leaf — check that it is a part doc before adding.
                IModelDoc2 model = (IModelDoc2)comp.GetModelDoc2();
                if (model != null &&
                    model.GetType() == (int)swDocumentTypes_e.swDocPART)
                {
                    paths.Add(comp.GetPathName());
                }
                return;
            }

            foreach (object obj in children)
            {
                Component2 child = (Component2)obj;
                CollectLeafPaths(child, paths);
            }
        }
#endif
    }
}
