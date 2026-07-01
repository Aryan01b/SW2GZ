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
using SW2GZ.Utilities;
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

        // P9 — lists every mate in the assembly as a MateInfo (name + implied joint
        // type + best-effort axis + limit range) for the Joints step's mate list.
        // The user assigns one of these to a joint. COM-only.
        public IReadOnlyList<MateInfo> WalkAllMates()
        {
#if SW_INTEROP
            if (_doc == null)
#endif
                throw new System.NotImplementedException(
                    "SolidWorksAssemblyWalker.WalkAllMates() requires a live SldWorks session.");

#if SW_INTEROP
            var mates = new List<MateInfo>();
            var modelDoc = (IModelDoc2)_doc;

            Feature feat = (Feature)modelDoc.FirstFeature();
            try
            {
                while (feat != null)
                {
                    if (feat.GetTypeName2() == "MateGroup")
                    {
                        Feature sub = (Feature)feat.GetFirstSubFeature();
                        try
                        {
                            while (sub != null)
                            {
                                TryAddMateInfo(sub, mates);
                                Feature nextSub = (Feature)sub.GetNextSubFeature();
                                Marshal.ReleaseComObject(sub);
                                sub = nextSub;
                            }
                        }
                        finally { if (sub != null) Marshal.ReleaseComObject(sub); }
                    }
                    else
                    {
                        TryAddMateInfo(feat, mates);
                    }

                    Feature next = (Feature)feat.GetNextFeature();
                    Marshal.ReleaseComObject(feat);
                    feat = next;
                }
            }
            finally { if (feat != null) Marshal.ReleaseComObject(feat); }

            return mates.AsReadOnly();
#endif
        }

#if SW_INTEROP
        private static void TryAddMateInfo(Feature feat, List<MateInfo> sink)
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
                // Limit range, if this is a limit mate.
                double? lower = null, upper = null;
                bool hasLimit = false;
                try
                {
                    double max = mate.MaximumVariation, min = mate.MinimumVariation;
                    if (System.Math.Abs(max) > 1e-9 || System.Math.Abs(min) > 1e-9)
                    {
                        lower = min; upper = max; hasLimit = true;
                    }
                }
                catch { }

                MateKind kind;
                switch ((swMateType_e)mate.Type)
                {
                    case swMateType_e.swMateLOCK:       kind = MateKind.Fixed;      break;
                    case swMateType_e.swMateCONCENTRIC: kind = hasLimit ? MateKind.Revolute : MateKind.Continuous; break;
                    case swMateType_e.swMateANGLE:      kind = hasLimit ? MateKind.Revolute : MateKind.Fixed; break;
                    case swMateType_e.swMateDISTANCE:   kind = hasLimit ? MateKind.Prismatic : MateKind.Fixed; break;
                    case swMateType_e.swMateSLOT:       kind = MateKind.Prismatic;  break;
                    case swMateType_e.swMateCOINCIDENT: kind = BothEntitiesPlanarFaces(mate) ? MateKind.Planar : MateKind.Fixed; break;
                    default:                            kind = MateKind.Fixed;      break;
                }

                Vector3 axis = kind == MateKind.Fixed ? new Vector3(0, 0, 1) : MateAxisDirection(mate);
                Vector3? matePoint = MateReferencePoint(mate);

                // Two top-level component names this mate spans — same mapping the
                // joint list uses (ParentLink/ChildLink), so the PMP can filter the
                // mate list to just the joint's pair.
                string linkA = null, linkB = null;
                int n2 = mate.GetMateEntityCount();
                for (int i = 0; i < n2 && linkB == null; i++)
                {
                    MateEntity2 ent = mate.MateEntity(i);
                    if (ent == null) continue;
                    try
                    {
                        Component2 comp = ent.ReferenceComponent;
                        if (comp == null) continue;
                        try
                        {
                            string name = SanitizeComponentName(TopLevelName(comp));
                            if (linkA == null) linkA = name;
                            else if (name != linkA) linkB = name;
                        }
                        finally { Marshal.ReleaseComObject(comp); }
                    }
                    finally { Marshal.ReleaseComObject(ent); }
                }

                sink.Add(new MateInfo(feat.Name, kind, axis, lower, upper, linkA, linkB, matePoint));
            }
            finally { Marshal.ReleaseComObject(mate); }
        }

        // P9 — selects a mate's reference geometry in the viewport by mate feature
        // name, so the user can verify the mate they assigned to a joint. COM-only.
        public bool HighlightMate(string mateName)
        {
            var log = Logger.GetLogger();
            log.Info("[HL] HighlightMate(\"" + mateName + "\") entry");
            if (string.IsNullOrEmpty(mateName)) { log.Info("[HL] empty name -> bail"); return false; }
            var model = (IModelDoc2)_doc;
            try { model.ClearSelection2(false); log.Info("[HL] ClearSelection2(false) ok"); }
            catch (System.Exception ex) { log.Info("[HL] ClearSelection2 threw: " + ex.GetType().Name + ": " + ex.Message); }

            int mateGroupsSeen = 0, subsSeen = 0;
            bool nameMatched = false;
            bool selected = false;
            Feature feat = (Feature)model.FirstFeature();
            try
            {
                while (feat != null && !selected)
                {
                    string tn = null; try { tn = feat.GetTypeName2(); } catch { }
                    if (tn == "MateGroup")
                    {
                        mateGroupsSeen++;
                        Feature sub = (Feature)feat.GetFirstSubFeature();
                        try
                        {
                            while (sub != null && !selected)
                            {
                                subsSeen++;
                                string sn = null; try { sn = sub.Name; } catch { }
                                if (sn == mateName)
                                {
                                    nameMatched = true;
                                    log.Info("[HL] matched mate \"" + sn + "\" — selecting refs");
                                    selected = SelectMateReferences(model, sub);
                                    log.Info("[HL] SelectMateReferences returned " + selected);
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

            log.Info("[HL] walk done: MateGroups=" + mateGroupsSeen + " subs=" + subsSeen +
                     " nameMatched=" + nameMatched + " selected=" + selected);

            if (selected)
            {
                try { model.GraphicsRedraw2(); log.Info("[HL] GraphicsRedraw2 ok"); }
                catch (System.Exception ex) { log.Info("[HL] GraphicsRedraw2 threw: " + ex.GetType().Name + ": " + ex.Message); }
            }
            return selected;
        }

        // Selects each MateEntity reference (face / edge / axis / vertex) so SW
        // highlights them in the viewport — same visual as SW's native mate PMP.
        // No feat.Select2 (broke wizard Next), no Component2.Select4 (selected
        // whole components, broke Step 2 Links picker that reads selection state).
        private static bool SelectMateReferences(IModelDoc2 model, Feature feat)
        {
            var log = Logger.GetLogger();
            object specific = feat.GetSpecificFeature2();
            var mate = specific as Mate2;
            if (mate == null)
            {
                log.Info("[HL]   GetSpecificFeature2 not Mate2 (was " + (specific?.GetType().Name ?? "null") + ")");
                if (specific != null) Marshal.ReleaseComObject(specific);
                return false;
            }

            bool any = false;
            try
            {
                int n = mate.GetMateEntityCount();
                log.Info("[HL]   mate has " + n + " entities");
                for (int i = 0; i < n; i++)
                {
                    MateEntity2 ent = mate.MateEntity(i);
                    if (ent == null) { log.Info("[HL]   ent[" + i + "] null"); continue; }
                    try
                    {
                        object refGeom = null;
                        try { refGeom = ent.Reference; }
                        catch (System.Exception ex) { log.Info("[HL]   ent[" + i + "].Reference threw: " + ex.GetType().Name + ": " + ex.Message); }
                        string refTy = refGeom?.GetType().Name ?? "null";
                        if (refGeom is Entity sel)
                        {
                            try
                            {
                                bool es = sel.Select4(true, null);
                                log.Info("[HL]   ent[" + i + "] ref=" + refTy + " Select4 -> " + es);
                                if (es) any = true;
                            }
                            catch (System.Exception ex) { log.Info("[HL]   ent[" + i + "].Select4 threw: " + ex.GetType().Name + ": " + ex.Message); }
                        }
                        else
                        {
                            log.Info("[HL]   ent[" + i + "] ref=" + refTy + " (not Entity)");
                        }
                        if (refGeom != null) Marshal.ReleaseComObject(refGeom);
                    }
                    finally { Marshal.ReleaseComObject(ent); }
                }
            }
            finally { Marshal.ReleaseComObject(mate); }

            return any;
        }

        // Best-effort axis direction for a moving mate: read the first mate
        // entity's EntityParams (origin[0..2] + direction[3..5]) and rotate it into
        // the assembly frame by the reference component's transform. Falls back to
        // (0,0,1) if nothing usable is found. Geometry-API exactness is validated on
        // a workstation; snapping to the nearest principal axis absorbs small error.
        private static Vector3 MateAxisDirection(Mate2 mate)
        {
            int entCount = mate.GetMateEntityCount();
            for (int i = 0; i < entCount; i++)
            {
                MateEntity2 ent = mate.MateEntity(i);
                if (ent == null) continue;
                try
                {
                    if (ent.EntityParams is double[] ep && ep.Length >= 6)
                    {
                        var dir = new Vector3((float)ep[3], (float)ep[4], (float)ep[5]);
                        Component2 comp = ent.ReferenceComponent;
                        if (comp != null)
                        {
                            try { dir = RotateByComponent(comp, dir); }
                            finally { Marshal.ReleaseComObject(comp); }
                        }
                        if (dir.LengthSquared() > 1e-9f) return Vector3.Normalize(dir);
                    }
                }
                finally { Marshal.ReleaseComObject(ent); }
            }
            return new Vector3(0, 0, 1);
        }

        // True when the mate couples two planar faces (a face-coincident mate),
        // i.e. the references slide in-plane and rotate about the shared normal —
        // URDF planar-joint semantics. Uses Face2.GetSurface().IsPlane().
        private static bool BothEntitiesPlanarFaces(Mate2 mate)
        {
            int entCount = mate.GetMateEntityCount();
            int planarFaces = 0;
            for (int i = 0; i < entCount; i++)
            {
                MateEntity2 ent = mate.MateEntity(i);
                if (ent == null) continue;
                object refGeom = null, surf = null;
                try
                {
                    refGeom = ent.Reference;
                    if (refGeom is Face2 face)
                    {
                        surf = face.GetSurface();
                        if (surf is Surface s && s.IsPlane()) planarFaces++;
                    }
                }
                finally
                {
                    // Release sub-objects (mirrors SelectMateReferences' COM hygiene),
                    // then the entity itself.
                    if (surf != null) Marshal.ReleaseComObject(surf);
                    if (refGeom != null) Marshal.ReleaseComObject(refGeom);
                    Marshal.ReleaseComObject(ent);
                }
            }
            return planarFaces >= 2;
        }

        // Mate-reference geometric point in the ASSEMBLY frame.
        //
        // Picks the most informative geometric reference among a mate's entities
        // and returns a single point that "where the joint physically lives" —
        // e.g. for a concentric mate, the cylindrical axis origin; for a coincident
        // mate on a planar face, a point on that plane; for a coincident edge,
        // the edge midpoint. Returns null when no usable reference is found, so
        // JointOriginResolver falls back to its legacy anchor-only path
        // (partial-mate-coverage is preferred over wrong-mate-coverage).
        //
        // Per SW SDK gotchas absorbed here:
        //   - IFace2 + Surface.IsCylinder/IsPlane: parameter blocks are
        //     CylinderParams [ox oy oz ax ay az radius] and PlaneParams
        //     [nx ny nz px py pz]. Origins/points are PART-LOCAL — must be
        //     transformed via the entity's ReferenceComponent.Transform2.
        //   - IEdge: GetCurveParams2() returns a CurveParams record whose
        //     StartPoint / EndPoint are part-local doubles; midpoint = mean.
        //   - When the mate carries multiple entities, prefer the entity whose
        //     ReferenceComponent is the PARENT side (heuristically: entity 0
        //     when ambiguous — same convention TryAddMate uses for ordering).
        private static Vector3? MateReferencePoint(Mate2 mate)
        {
            int entCount = mate.GetMateEntityCount();
            if (entCount <= 0) return null;

            // Order: pick the entity that yields the most informative geometric
            // point.
            //   Pass 0: cylindrical face → axis origin (concentric mates).
            //   Pass 1: planar face     → point on the plane (face-coincident).
            //   Pass 2: any entity's EntityParams origin block (best-effort
            //           fallback covering edges/axes/vertices whose specific
            //           geometric extractors vary across SW SDK versions).
            // Returns the first successful extraction; null when every pass on
            // every entity fails (caller falls back to legacy anchor path).
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 0; i < entCount; i++)
                {
                    MateEntity2 ent = mate.MateEntity(i);
                    if (ent == null) continue;
                    object refGeom = null;
                    Component2 comp = null;
                    try
                    {
                        try { refGeom = ent.Reference; } catch { refGeom = null; }
                        try { comp = ent.ReferenceComponent; } catch { comp = null; }
                        Vector3? local = TryExtractPointForPass(ent, refGeom, pass);
                        if (!local.HasValue) continue;

                        // Transform PART-LOCAL → ASSEMBLY frame via the entity's
                        // ReferenceComponent.Transform2. Without the component
                        // transform the point would land at the assembly origin
                        // for any nested part — the bug we're fixing.
                        Vector3 worldPt = comp != null
                            ? TransformByComponent(comp, local.Value)
                            : local.Value;
                        return worldPt;
                    }
                    catch
                    {
                        // Defensive: any SW COM hiccup → skip this entity, try
                        // the next one. We'd rather emit a null mate point and
                        // fall back than crash the whole export.
                    }
                    finally
                    {
                        if (refGeom != null) try { Marshal.ReleaseComObject(refGeom); } catch { }
                        if (comp != null)    try { Marshal.ReleaseComObject(comp);    } catch { }
                        try { Marshal.ReleaseComObject(ent); } catch { }
                    }
                }
            }
            return null;
        }

        private static Vector3? TryExtractPointForPass(MateEntity2 ent, object refGeom, int pass)
        {
            if (pass == 0 && refGeom is Face2 face0)
            {
                object surfObj = null;
                try
                {
                    surfObj = face0.GetSurface();
                    if (surfObj is Surface s && s.IsCylinder())
                    {
                        // CylinderParams: [origin.x, origin.y, origin.z,
                        //                  axis.x,   axis.y,   axis.z, radius]
                        if (s.CylinderParams is double[] cp && cp.Length >= 6)
                            return new Vector3((float)cp[0], (float)cp[1], (float)cp[2]);
                    }
                    return null;
                }
                finally { if (surfObj != null) try { Marshal.ReleaseComObject(surfObj); } catch { } }
            }

            if (pass == 1 && refGeom is Face2 face1)
            {
                object surfObj = null;
                try
                {
                    surfObj = face1.GetSurface();
                    if (surfObj is Surface s && s.IsPlane())
                    {
                        // PlaneParams: [normal.x, normal.y, normal.z,
                        //               point.x,  point.y,  point.z]
                        if (s.PlaneParams is double[] pp && pp.Length >= 6)
                            return new Vector3((float)pp[3], (float)pp[4], (float)pp[5]);
                    }
                    return null;
                }
                finally { if (surfObj != null) try { Marshal.ReleaseComObject(surfObj); } catch { } }
            }

            if (pass == 2)
            {
                // Best-effort fallback: MateEntity2.EntityParams exposes a
                // [origin.x origin.y origin.z direction.x direction.y direction.z]
                // pair for face/edge/axis references. Use the origin block
                // when present so edges, reference axes and vertices still
                // yield SOMETHING geometric for the joint origin. Not as
                // precise as the typed Face2.GetSurface() path but better than
                // returning null and reverting to the part-anchor bug.
                try
                {
                    if (ent.EntityParams is double[] ep && ep.Length >= 3)
                        return new Vector3((float)ep[0], (float)ep[1], (float)ep[2]);
                }
                catch { /* defensive: SW occasionally throws on EntityParams. */ }
                return null;
            }

            return null;
        }

        // Applies a component's full Transform2 (3x3 rotation + translation) to a
        // PART-LOCAL point, returning the corresponding ASSEMBLY-frame point.
        // Different from RotateByComponent above (which is for direction
        // vectors and ignores translation).
        // ArrayData's 3x3 rotation block is COLUMN-major (verified against
        // Component2.GetBox ground truth — see memory
        // sw-mathtransform-column-major); the naive row-major read silently
        // inverts rotation for any non-identity component orientation.
        private static Vector3 TransformByComponent(Component2 comp, Vector3 v)
        {
            MathTransform xform = comp.Transform2;
            if (xform == null) return v;
            if (!(xform.ArrayData is double[] d) || d.Length < 12) return v;
            float x = (float)(d[0] * v.X + d[3] * v.Y + d[6] * v.Z + d[9]);
            float y = (float)(d[1] * v.X + d[4] * v.Y + d[7] * v.Z + d[10]);
            float z = (float)(d[2] * v.X + d[5] * v.Y + d[8] * v.Z + d[11]);
            return new Vector3(x, y, z);
        }

        // Applies a component's rotation (Transform2 / MathTransform 3x3 block) to a
        // direction vector, mapping component-local → assembly frame.
        private static Vector3 RotateByComponent(Component2 comp, Vector3 v)
        {
            MathTransform xform = comp.Transform2;
            if (xform == null) return v;
            if (!(xform.ArrayData is double[] d) || d.Length < 9) return v;
            float x = (float)(d[0] * v.X + d[3] * v.Y + d[6] * v.Z);
            float y = (float)(d[1] * v.X + d[4] * v.Y + d[7] * v.Z);
            float z = (float)(d[2] * v.X + d[5] * v.Y + d[8] * v.Z);
            return new Vector3(x, y, z);
        }

        // P9 — highlights in the viewport the reference geometry of the mate that
        // couples the two given link component-sets, so the user can see where a
        // joint's axis lives. Walks the mate tree, matches a mate whose two
        // top-level endpoints fall one in each set, clears the current selection
        // and selects that mate's reference entities. Returns true if matched.
        // COM-only, validated on a workstation.
        public bool HighlightMateReferences(
            System.Collections.Generic.ICollection<string> compIdsA,
            System.Collections.Generic.ICollection<string> compIdsB)
        {
            if (compIdsA == null || compIdsB == null) return false;
            var model = (IModelDoc2)_doc;
            model.ClearSelection2(true);

            bool matched = false;
            Feature feat = (Feature)model.FirstFeature();
            try
            {
                while (feat != null && !matched)
                {
                    if (feat.GetTypeName2() == "MateGroup")
                    {
                        Feature sub = (Feature)feat.GetFirstSubFeature();
                        try
                        {
                            while (sub != null && !matched)
                            {
                                matched = TrySelectMateIfMatch(sub, compIdsA, compIdsB);
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

            return matched;
        }

        // Selects a mate's reference entities iff its two top-level endpoints fall
        // one in each component set. Returns whether it matched.
        private static bool TrySelectMateIfMatch(
            Feature feat,
            System.Collections.Generic.ICollection<string> a,
            System.Collections.Generic.ICollection<string> b)
        {
            object specific = feat.GetSpecificFeature2();
            var mate = specific as Mate2;
            if (mate == null)
            {
                if (specific != null) Marshal.ReleaseComObject(specific);
                return false;
            }

            var ents = new List<MateEntity2>();
            try
            {
                string topA = null, topB = null;
                int n = mate.GetMateEntityCount();
                for (int i = 0; i < n; i++)
                {
                    MateEntity2 ent = mate.MateEntity(i);
                    if (ent == null) continue;
                    ents.Add(ent);
                    Component2 comp = ent.ReferenceComponent;
                    if (comp != null)
                    {
                        try
                        {
                            string t = TopLevelName(comp);
                            if (topA == null) topA = t;
                            else if (topB == null && t != topA) topB = t;
                        }
                        finally { Marshal.ReleaseComObject(comp); }
                    }
                }

                bool match = topA != null && topB != null &&
                    ((a.Contains(topA) && b.Contains(topB)) || (a.Contains(topB) && b.Contains(topA)));

                if (match)
                {
                    foreach (MateEntity2 ent in ents)
                    {
                        object refGeom = ent.Reference;
                        if (refGeom is Entity sel)
                        {
                            try { sel.Select4(true, null); }
                            catch { /* a non-selectable reference — skip it */ }
                        }
                        if (refGeom != null) Marshal.ReleaseComObject(refGeom);
                    }
                }

                return match;
            }
            finally
            {
                foreach (MateEntity2 ent in ents) Marshal.ReleaseComObject(ent);
                Marshal.ReleaseComObject(mate);
            }
        }

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
                //
                // MatePointAssembly: the mate's geometric reference point (e.g.
                // a concentric mate's cylindrical axis origin) in assembly frame.
                // Null when extraction couldn't find a deterministic geometric
                // reference — JointOriginResolver falls back to the legacy
                // anchor-only path.
                Vector3? matePoint = MateReferencePoint(mate);

                var spec = new MateSpec(
                    Name:              mateName,
                    Kind:              kind,
                    Origin:            Pose.Identity,
                    Axis:              axis,
                    LimitLower:        null,
                    LimitUpper:        null,
                    LimitEffort:       0.0,
                    LimitVelocity:     0.0,
                    Interface:         UrdfCmdInterface.Position,
                    ParentLink:        parentLink,
                    ChildLink:         childLink,
                    MatePointAssembly: matePoint);

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

        // Recursively collect leaf-component Name2 (instance-unique) identifiers.
        // A leaf is a component whose children are null/empty AND whose model is a part doc.
        // Name2 is used instead of GetPathName() so multi-instance assemblies
        // (same part used N times) produce N distinct anchor lookups in FindComponent.
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
                    paths.Add(comp.Name2);
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
