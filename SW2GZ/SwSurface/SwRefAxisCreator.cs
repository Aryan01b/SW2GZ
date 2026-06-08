/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

SwRefAxisCreator — creates a visible SolidWorks Reference Axis feature in
the assembly's FeatureManager tree from the cylindrical face(s) of a
user-picked mate. Mirrors the upstream solidworks_urdf_exporter convention
of one Reference Axis feature per revolute / prismatic joint, but generates
it FOR the user instead of asking them to author it by hand.

Called from Sw2gzCreateRobotPmp.ApplySelectedMate immediately after
AutoJointResolver.ResolveFromMateName succeeds with a cylinder-bearing
result. The created feature's name is written back into
JointDef.RefAxisName so downstream walkers / saved Sw2gzDoc payloads can
round-trip the visible-feature reference.

Algorithm mirrors AutoJointResolver.ResolveFromMateName's MateGroup walk
+ COM-hygiene pattern:
  1. Walk MateGroup sub-features → find the named mate.
  2. Identify parent-side and child-side MateEntity2 via TopLevelName-
     matching against the supplied component-id lists.
  3. Pull IFace2 references; verify ISurface.IsCylinder().
  4. Delete any pre-existing "sw2gz_<jointName>_axis" feature (idempotent
     re-clicks on the same joint).
  5. Clear selection, multi-select the cylindrical face(s)
     (IEntity.Select4(true, null) — true = append).
  6. Snapshot FeatureManager.GetFeatures, call IModelDoc2.InsertAxis2(true),
     diff to find the new Feature (mirrors the upstream solidworks_urdf_exporter
     ExportHelperExtension.InsertAxis trick — InsertAxis2 is void-ish and
     doesn't hand back the created feature directly).
  7. Rename the new feature to "sw2gz_<jointName>_axis".

Returns the new feature name on success, null on any miss (no cylindrical
face, SelectByID failure, InsertRefAxis failure). The caller wraps the
call in try/catch and treats failure as non-fatal — the JointDef-side
auto-extracted axis/origin numbers are already populated by
AutoJointResolver, so missing the visible feature just costs the user
the FeatureManager affordance.

Gated on SW_INTEROP — this entire class touches Mate2 / MateEntity2 /
IFace2 / ISurface / Feature / IModelDocExtension.
*/
#if SW_INTEROP
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SW2GZ.SwSurface
{
    public sealed class SwRefAxisCreator
    {
        private readonly ModelDoc2 _doc;

        public SwRefAxisCreator(ModelDoc2 doc)
        {
            _doc = doc;
        }

        // Create a SolidWorks Reference Axis feature from the cylindrical
        // face(s) of the named mate. Names the new feature
        // "sw2gz_<jointName>_axis". If a previous feature with that name
        // exists (from a prior Apply on the same joint), it is deleted first
        // so the click is idempotent.
        //
        // Returns the new feature's name on success, null on any failure
        // (mate not found, no cylindrical face on either side, SelectByID
        // failure, InsertRefAxis failure).
        public string CreateFromMate(
            string mateName,
            string jointName,
            IReadOnlyList<string> parentComponentIds,
            IReadOnlyList<string> childComponentIds)
        {
            if (_doc == null) return null;
            if (string.IsNullOrEmpty(mateName) || string.IsNullOrEmpty(jointName)) return null;
            if (parentComponentIds == null || childComponentIds == null) return null;

            var parents = new HashSet<string>(parentComponentIds);
            var children = new HashSet<string>(childComponentIds);
            if (parents.Count == 0 || children.Count == 0) return null;

            string desiredName = "sw2gz_" + jointName + "_axis";

            // Pull the parent/child cylindrical faces up front so we can
            // bail before mutating any SW state if neither side has one.
            (IFace2 parentFace, IFace2 childFace) = FindCylindricalFaces(
                mateName, parents, children);

            if (parentFace == null && childFace == null) return null;

            try
            {
                // Idempotent: nuke any stale axis feature with the same name
                // before re-creating. Quiet if not present.
                DeleteExistingRefAxis(desiredName);

                _doc.ClearSelection2(true);

                bool anySelected = false;
                if (parentFace != null)
                {
                    var ent = parentFace as IEntity;
                    if (ent != null && ent.Select4(anySelected, null)) anySelected = true;
                }
                if (childFace != null)
                {
                    var ent = childFace as IEntity;
                    if (ent != null && ent.Select4(anySelected, null)) anySelected = true;
                }
                if (!anySelected) return null;

                // IModelDoc2.InsertAxis2(true) operates on the current
                // selection — with two cylindrical faces selected SW infers
                // the axis through their common centerline; with one face,
                // through that single face's centerline. The call doesn't
                // return the created Feature, so we diff FeatureManager.
                object[] before = (object[])_doc.FeatureManager.GetFeatures(true) ?? new object[0];
                _doc.InsertAxis2(true);
                object[] after = (object[])_doc.FeatureManager.GetFeatures(true) ?? new object[0];
                if (after.Length <= before.Length) return null;

                Feature created = FindNewFeature(before, after);
                if (created == null) return null;

                try
                {
                    created.Name = desiredName;
                    return desiredName;
                }
                finally
                {
                    try { Marshal.ReleaseComObject(created); } catch { }
                }
            }
            finally
            {
                if (parentFace != null) try { Marshal.ReleaseComObject(parentFace); } catch { }
                if (childFace  != null) try { Marshal.ReleaseComObject(childFace);  } catch { }
                try { _doc.ClearSelection2(true); } catch { }
            }
        }

        // Walk MateGroup → find mate by Feature.Name → pull parent/child
        // MateEntity2 Reference. Returns IFace2 references when the entity's
        // Reference is a cylindrical face; null otherwise. Caller releases
        // the returned faces.
        private (IFace2 parentFace, IFace2 childFace) FindCylindricalFaces(
            string mateName,
            HashSet<string> parents,
            HashSet<string> children)
        {
            IFace2 pFace = null, cFace = null;

            Feature feat = (Feature)_doc.FirstFeature();
            try
            {
                while (feat != null)
                {
                    if (pFace == null && cFace == null && feat.GetTypeName2() == "MateGroup")
                    {
                        Feature sub = (Feature)feat.GetFirstSubFeature();
                        try
                        {
                            while (sub != null)
                            {
                                if (pFace == null && cFace == null && sub.Name == mateName)
                                {
                                    ExtractFaces(sub, parents, children, out pFace, out cFace);
                                }
                                Feature nextSub = (Feature)sub.GetNextSubFeature();
                                Marshal.ReleaseComObject(sub);
                                sub = nextSub;
                            }
                        }
                        finally { if (sub != null) Marshal.ReleaseComObject(sub); }
                    }
                    Feature next = (Feature)feat.GetNextFeature();
                    Marshal.ReleaseComObject(feat);
                    feat = next;
                }
            }
            finally { if (feat != null) Marshal.ReleaseComObject(feat); }

            return (pFace, cFace);
        }

        // Pull the parent-side and child-side cylindrical-face references
        // off a Mate2 sub-feature. Writes the cylindrical faces (or null
        // if absent / non-face / non-cylinder) into the out params.
        private static void ExtractFaces(
            Feature feat,
            HashSet<string> parents,
            HashSet<string> children,
            out IFace2 parentFace,
            out IFace2 childFace)
        {
            parentFace = null;
            childFace = null;

            object specific = feat.GetSpecificFeature2();
            var mate = specific as Mate2;
            if (mate == null)
            {
                if (specific != null) try { Marshal.ReleaseComObject(specific); } catch { }
                return;
            }

            try
            {
                int n = mate.GetMateEntityCount();
                for (int i = 0; i < n; i++)
                {
                    MateEntity2 ent = mate.MateEntity(i);
                    if (ent == null) continue;
                    bool keptFace = false;
                    try
                    {
                        Component2 comp = ent.ReferenceComponent;
                        if (comp == null) continue;
                        string name;
                        try { name = TopLevelName(comp); }
                        finally { Marshal.ReleaseComObject(comp); }

                        bool isParent = parents.Contains(name);
                        bool isChild  = !isParent && children.Contains(name);
                        if (!isParent && !isChild) continue;
                        if (isParent && parentFace != null) continue;
                        if (isChild  && childFace  != null) continue;

                        IFace2 face = TryGetCylindricalFace(ent);
                        if (face == null) continue;

                        if (isParent) parentFace = face;
                        else          childFace  = face;
                        keptFace = true;
                    }
                    finally
                    {
                        // Only release ent if we didn't hand its underlying
                        // face off — IFace2 is a separate COM object pulled
                        // via ent.Reference, so releasing ent is always safe.
                        try { Marshal.ReleaseComObject(ent); } catch { }
                        // Suppress unused-warning on keptFace; the flag is
                        // for readability of the branch above.
                        _ = keptFace;
                    }
                }
            }
            finally { try { Marshal.ReleaseComObject(mate); } catch { } }
        }

        // Returns the cylindrical IFace2 underlying a MateEntity2, or null
        // when the entity's reference is not a face or not a cylinder.
        // Caller owns the returned face (releases via ReleaseComObject).
        private static IFace2 TryGetCylindricalFace(MateEntity2 ent)
        {
            object refObj = null, surfObj = null;
            try
            {
                try { refObj = ent.Reference; } catch { refObj = null; }
                if (!(refObj is IFace2 face)) return null;

                surfObj = face.GetSurface();
                if (!(surfObj is ISurface surf) || !surf.IsCylinder()) return null;

                // Detach refObj from the finally release so the face survives.
                refObj = null;
                return face;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (surfObj != null) try { Marshal.ReleaseComObject(surfObj); } catch { }
                if (refObj  != null) try { Marshal.ReleaseComObject(refObj);  } catch { }
            }
        }

        // Walk the FeatureManager looking for a Reference Axis feature
        // matching desiredName. If found, select + delete it so the
        // re-creation below is idempotent.
        private void DeleteExistingRefAxis(string desiredName)
        {
            bool found = false;
            Feature feat = (Feature)_doc.FirstFeature();
            try
            {
                while (feat != null)
                {
                    if (!found && feat.Name == desiredName && feat.GetTypeName2() == "RefAxis")
                    {
                        found = true;
                    }
                    Feature next = (Feature)feat.GetNextFeature();
                    Marshal.ReleaseComObject(feat);
                    feat = next;
                }
            }
            finally { if (feat != null) Marshal.ReleaseComObject(feat); }

            if (!found) return;

            try
            {
                _doc.ClearSelection2(true);
                bool selected = _doc.Extension.SelectByID2(
                    desiredName, "REFAXIS", 0, 0, 0, false, 0, null, 0);
                if (selected)
                {
                    _doc.Extension.DeleteSelection2(0);
                }
            }
            catch { /* non-fatal — leave the stale feature and let the
                       new one collide-rename; better than crashing. */ }
            finally { try { _doc.ClearSelection2(true); } catch { } }
        }

        // Diff before/after FeatureManager snapshots to find the Feature
        // that InsertAxis2 just created. Walk after-snapshot in reverse
        // because new features are appended at the end of the tree —
        // mirrors ExportHelperExtension.InsertAxis's reverse-walk trick.
        // Releases the discarded after-snapshot entries it walks past.
        private static Feature FindNewFeature(object[] before, object[] after)
        {
            var beforeSet = new HashSet<object>(before);
            Feature found = null;
            for (int i = after.Length - 1; i >= 0; i--)
            {
                if (found == null && !beforeSet.Contains(after[i]))
                {
                    found = after[i] as Feature;
                }
            }
            return found;
        }

        // Walk up the component owner chain to its top-level instance name —
        // mirrors AutoJointResolver.TopLevelName so the ids we match against
        // (LinkDef.ComponentIds) line up.
        private static string TopLevelName(Component2 comp)
        {
            string name = comp.Name2;
            Component2 parent = (Component2)comp.GetParent();
            while (parent != null)
            {
                name = parent.Name2;
                Component2 next = (Component2)parent.GetParent();
                Marshal.ReleaseComObject(parent);
                parent = next;
            }
            return name;
        }
    }
}
#endif
