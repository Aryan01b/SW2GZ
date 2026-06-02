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
using SW2GZ.Build;
using SW2GZ.SwSurface.Abstractions;

#if SW_INTEROP
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Numerics;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
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

        // P2 — surfaces the SolidWorks mates of the active assembly as MateSpecs.
        //
        // HEURISTIC CLASSIFICATION (documented as such — needs runtime tuning on a
        // real assembly in SolidWorks). Deriving joints from mates alone is
        // genuinely hard, so this code is deliberately CONSERVATIVE: when the
        // mate arrangement is ambiguous it emits a Fixed joint (links stay
        // connected, no bogus motion) rather than fabricating an axis or limits.
        // This COM path cannot be unit-tested off a live SldWorks session — its
        // correctness is validated later via SolidWorks smoke testing.
        public IReadOnlyList<MateSpec> WalkMates()
        {
#if SW_INTEROP
            if (_doc == null)
#endif
                throw new System.NotImplementedException(
                    "SolidWorksAssemblyWalker.WalkMates() not yet wired to SldWorks API — see Task 28/P2.");

#if SW_INTEROP
            var mates = new List<MateSpec>();
            var modelDoc = (IModelDoc2)_doc;

            Feature feat = (Feature)modelDoc.FirstFeature();
            try
            {
                while (feat != null)
                {
                    // Mate features live as sub-features under the "MateGroup"
                    // feature in the FeatureManager tree. Recurse one level into
                    // it; top-level mates (rare) are also handled by inspecting
                    // every feature for a Mate2 specific-feature.
                    string typeName = feat.GetTypeName2();
                    if (typeName == "MateGroup")
                    {
                        Feature sub = (Feature)feat.GetFirstSubFeature();
                        try
                        {
                            while (sub != null)
                            {
                                TryAddMate(sub, mates);
                                Feature nextSub = (Feature)sub.GetNextSubFeature();
                                Marshal.ReleaseComObject(sub);
                                sub = nextSub;
                            }
                        }
                        finally
                        {
                            if (sub != null) Marshal.ReleaseComObject(sub);
                        }
                    }
                    else
                    {
                        TryAddMate(feat, mates);
                    }

                    Feature next = (Feature)feat.GetNextFeature();
                    Marshal.ReleaseComObject(feat);
                    feat = next;
                }
            }
            finally
            {
                if (feat != null) Marshal.ReleaseComObject(feat);
            }

            return mates.AsReadOnly();
#endif
        }

#if SW_INTEROP
        // Inspects a single feature; if it wraps a Mate2, classifies it into a
        // MateSpec and appends. No-op for non-mate features. All COM RCWs touched
        // here are released in finally.
        private static void TryAddMate(Feature feat, List<MateSpec> sink)
        {
            object specific = feat.GetSpecificFeature2();
            var mate = specific as Mate2;
            if (mate == null)
            {
                if (specific != null) Marshal.ReleaseComObject(specific);
                return;
            }

            try
            {
                // Resolve the two coupled components -> sanitized link names.
                // A mate may reference more components, but joints are pairwise;
                // we take the first two distinct components.
                string parentLink = null, childLink = null;
                int entCount = mate.GetMateEntityCount();
                for (int i = 0; i < entCount; i++)
                {
                    MateEntity2 ent = mate.MateEntity(i);
                    if (ent == null) continue;
                    try
                    {
                        Component2 comp = ent.ReferenceComponent;
                        if (comp == null) continue;
                        try
                        {
                            // Use the SAME top-level mapping as WalkActive: a leaf
                            // component's owning top-level component is the link.
                            string compName = SanitizeComponentName(TopLevelName(comp));
                            if (parentLink == null) parentLink = compName;
                            else if (childLink == null && compName != parentLink) childLink = compName;
                        }
                        finally { Marshal.ReleaseComObject(comp); }
                    }
                    finally { Marshal.ReleaseComObject(ent); }
                }

                string mateName = SanitizeComponentName(feat.Name);

                // Both endpoints required; otherwise we can't form a joint.
                if (parentLink == null || childLink == null)
                    return;

                // ── Classification heuristic (CONSERVATIVE) ───────────────────
                // SolidWorks gives us the mate TYPE but not the assembly DOF that
                // results from the full mate stack. We map a single mate type to
                // a joint kind as a best-effort first cut:
                //   CONCENTRIC  -> Revolute about (0,0,1) [cylindrical: rotation
                //                  free; sliding ignored for v2.1 — noted].
                //   everything else (COINCIDENT, DISTANCE, PARALLEL, ...) ->
                //                  Fixed, since on its own it does not free a DOF
                //                  we can reliably orient.
                // Axis extraction from mate geometry is NOT attempted here (the
                // entity transform API is unreliable without a resolved assembly
                // context); we default axis (0,0,1) and null limits, exactly the
                // "don't fabricate" rule. Real axis/limit derivation is a
                // SolidWorks-smoke-test follow-up.
                MateKind kind;
                Vector3 axis = new Vector3(0, 0, 1);
                switch ((swMateType_e)mate.Type)
                {
                    case swMateType_e.swMateCONCENTRIC:
                        // Cylindrical: treat as Revolute (Continuous — no angle
                        // limit derivable from a lone concentric mate).
                        kind = MateKind.Continuous;
                        break;
                    default:
                        kind = MateKind.Fixed;
                        break;
                }

                // Origin: we cannot reliably derive the joint frame transform from
                // the mate alone, so use identity. Connectivity (parent/child) is
                // what matters for v2.1; exact origin is a follow-up. (Pose.Identity)
                var spec = new MateSpec(
                    Name:          mateName,
                    Kind:          kind,
                    Origin:        Pose.Identity,
                    Axis:          axis,
                    LimitLower:    null,
                    LimitUpper:    null,
                    LimitEffort:   0.0,
                    LimitVelocity: 0.0,
                    Interface:     UrdfCmdInterface.Position,
                    ParentLink:    parentLink,
                    ChildLink:     childLink);

                sink.Add(spec);
            }
            finally
            {
                Marshal.ReleaseComObject(mate);
            }
        }

        // Walks up a component's owner chain to the top-level component whose name
        // WalkActive uses as the link name. GetParent() returns null at top level.
        private static string TopLevelName(Component2 comp)
        {
            Component2 current = comp;
            // Don't release `comp` itself (caller owns it); only release parents we fetch.
            string name = current.Name2;
            Component2 parent = (Component2)current.GetParent();
            while (parent != null)
            {
                name = parent.Name2;
                Component2 next = (Component2)parent.GetParent();
                Marshal.ReleaseComObject(parent);
                parent = next;
            }
            return name;
        }

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
