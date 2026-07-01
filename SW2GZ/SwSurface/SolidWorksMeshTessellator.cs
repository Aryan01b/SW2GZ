/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Task 28: wires actual ITessellation API + color extraction from ModelDoc2.
The parameterless ctor preserves the original skeleton behaviour —
Tessellate() throws NotImplementedException when no SW handles are present.

SW_INTEROP is defined when building SW2GZ.csproj (which has the COM
references). It is NOT defined when building the xunit test project
(net8.0, no COM refs), allowing the same source file to compile in both.
*/
using System;
using System.Collections.Generic;
using SW2GZ.Build;
using SW2GZ.Exceptions;
using SW2GZ.SwSurface.Abstractions;

#if SW_INTEROP
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
#endif

namespace SW2GZ.SwSurface
{
    public sealed class SolidWorksMeshTessellator : IMeshTessellator
    {
#if SW_INTEROP
        private readonly SldWorks _swApp;
        private readonly AssemblyDoc _doc;
        private readonly PartDoc _part;   // set for a standalone part document
#endif

        // Skeleton ctor — preserves Moq test: Tessellate() throws NotImplementedException.
        public SolidWorksMeshTessellator() { }

#if SW_INTEROP
        // Real ctor for production use (assembly document).
        public SolidWorksMeshTessellator(SldWorks swApp, AssemblyDoc doc)
        {
            _swApp = swApp;
            _doc = doc;
        }

        // Part-document ctor — tessellates the part's own solid bodies (no
        // component / assembly transform; part-local IS the model frame). The
        // componentPathName arg to Tessellate is ignored in this mode.
        public SolidWorksMeshTessellator(SldWorks swApp, PartDoc part)
        {
            _swApp = swApp;
            _part = part;
        }
#endif

        public MeshData Tessellate(string componentPathName, TessellationLod lod)
        {
#if SW_INTEROP
            if (_swApp == null || (_doc == null && _part == null))
#endif
                throw new NotImplementedException(
                    "SolidWorksMeshTessellator.Tessellate() not yet wired to SldWorks API — see Task 28.");

#if SW_INTEROP
            if (_part != null) return TessellatePart();

            // Find the component.
            Component2 comp = SolidWorksMassProperties.FindComponent(
                (object[])_doc.GetComponents(false), componentPathName);
            if (comp == null)
                throw new Sw2gzExportException(
                    "Component path not found in active assembly: " + componentPathName);

            IModelDoc2 model = (IModelDoc2)comp.GetModelDoc2();
            if (model == null)
                throw new Sw2gzMeshException(
                    "Could not obtain model doc for component: " + componentPathName);

            // model and tess are released in the outer finally below (model is used late
            // for MaterialPropertyValues, so it must not be released earlier).
            ITessellation tess = null;
            try
            {
                // Collect the leaf components that actually carry solid bodies.
                // A part component → itself; a sub-assembly component → its
                // descendant part components (recursive), so sub-assembly assets
                // tessellate instead of being skipped. Each leaf bakes its OWN
                // assembly-frame Component2.Transform2.
                var leaves = new List<Component2>();
                CollectBodyComponents(comp, leaves);
                if (leaves.Count == 0)
                    throw new Sw2gzMeshException(
                        "No solid bodies found in component (including sub-components): " + componentPathName);

                var verts = new List<System.Numerics.Vector3>();
                var tris  = new List<int>();

                try
                {
                    foreach (Component2 leaf in leaves)
                    {
                        object[] bodyObjs = null;
                        try { bodyObjs = leaf.GetBodies2((int)swBodyType_e.swSolidBody) as object[]; }
                        catch (InvalidCastException) { }
                        if (bodyObjs == null || bodyObjs.Length == 0) continue;

                        // Per-leaf assembly-frame transform. ArrayData layout:
                        // [0..8] rotation 3x3 COLUMN-major, [9..11] translation,
                        // [12] scale, [13..15] padding. Verified against
                        // Component2.GetBox ground truth (see memory
                        // sw-mathtransform-column-major) — the naive row-major
                        // read silently inverts rotation for any non-identity
                        // component orientation.
                        MathTransform xform = leaf.Transform2;
                        double[] d = xform?.ArrayData as double[];
                        bool hasXf = d != null && d.Length >= 12;
                        double sc = (hasXf && d.Length >= 13 && d[12] > 1e-9) ? d[12] : 1.0;

                        foreach (object bodyObj in bodyObjs)
                        {
                            Body2 body = bodyObj as Body2;
                            if (body == null) continue;

                            // GetTessellation(null) requests tessellation of all faces.
                            var bodyTess = (ITessellation)body.GetTessellation(null);
                            bodyTess.NeedVertexNormal = false;
                            bodyTess.NeedFaceFacetMap = false;
                            bodyTess.NeedEdgeFinMap   = false;

                            bool ok = bodyTess.Tessellate();
                            if (!ok)
                            {
                                Marshal.ReleaseComObject(bodyTess);
                                throw new Sw2gzMeshException(
                                    "ITessellation.Tessellate() returned false for: " + componentPathName);
                            }

                            int facetCount = bodyTess.GetFacetCount();
                            for (int f = 0; f < facetCount; f++)
                            {
                                // Each facet has exactly 3 fins; each fin is an edge with 2 vertex ids.
                                // Take vertex [0] of each of the 3 fins for the triangle.
                                int[] fins = (int[])bodyTess.GetFacetFins(f);  // int[3]
                                int baseIdx = verts.Count;

                                for (int fi = 0; fi < 3; fi++)
                                {
                                    int[] finVerts = (int[])bodyTess.GetFinVertices(fins[fi]); // int[2]
                                    double[] pt = (double[])bodyTess.GetVertexPoint(finVerts[0]); // double[3]
                                    double x = pt[0], y = pt[1], z = pt[2];
                                    // Bake the leaf's assembly-frame transform per vertex
                                    // (GetVertexPoint returns part-local coords).
                                    if (hasXf)
                                    {
                                        double rx = d[0] * x + d[3] * y + d[6] * z;
                                        double ry = d[1] * x + d[4] * y + d[7] * z;
                                        double rz = d[2] * x + d[5] * y + d[8] * z;
                                        x = rx * sc + d[9]; y = ry * sc + d[10]; z = rz * sc + d[11];
                                    }
                                    verts.Add(new System.Numerics.Vector3((float)x, (float)y, (float)z));
                                }

                                tris.Add(baseIdx);
                                tris.Add(baseIdx + 1);
                                tris.Add(baseIdx + 2);
                            }
                            Marshal.ReleaseComObject(bodyTess);
                        }
                    }
                }
                catch (COMException ex)
                {
                    throw new Sw2gzMeshException(
                        "Tessellation failed for component: " + componentPathName, ex);
                }

                if (verts.Count == 0)
                    throw new Sw2gzMeshException(
                        "Tessellation produced no geometry for component: " + componentPathName);

                // Extract material color. Prefer the component's appearance
                // (an override set on the instance inside the assembly — the
                // usual way users colour an environment), falling back to the
                // part document's own material. double[9]:
                // [ R, G, B, Ambient, Diffuse, Specular, Shininess, Transparency, Emission ]
                System.Drawing.Color? color = null;
                try
                {
                    double[] matProps = null;
                    try
                    {
                        matProps = comp.GetMaterialPropertyValues2(
                            (int)swInConfigurationOpts_e.swThisConfiguration, null) as double[];
                    }
                    catch (COMException) { /* fall back to part material */ }

                    // Treat null/short/all-black component values as "no override".
                    bool compUnset = matProps == null || matProps.Length < 8 ||
                                     (matProps[0] <= 0 && matProps[1] <= 0 && matProps[2] <= 0);
                    if (compUnset)
                    {
                        // Part material of the first leaf (for a sub-assembly the
                        // top component's own model doc carries no part material).
                        try
                        {
                            var leafModel = leaves[0].GetModelDoc2() as IModelDoc2;
                            if (leafModel != null) matProps = leafModel.MaterialPropertyValues as double[];
                        }
                        catch (COMException) { }
                    }

                    if (matProps != null && matProps.Length >= 8)
                    {
                        double r = matProps[0];
                        double g = matProps[1];
                        double b = matProps[2];
                        double transparency = matProps[7];

                        // Only use if not all-zeros (unset / default material).
                        if (r > 0 || g > 0 || b > 0)
                        {
                            color = System.Drawing.Color.FromArgb(
                                (int)((1.0 - transparency) * 255),
                                (int)(r * 255),
                                (int)(g * 255),
                                (int)(b * 255));
                        }
                    }
                }
                catch (COMException)
                {
                    // Color is optional — swallow and leave color null.
                }

                return new MeshData(verts.ToArray(), tris.ToArray(), color);
            }
            finally
            {
                if (tess != null) Marshal.ReleaseComObject(tess);
                if (model != null) Marshal.ReleaseComObject(model);
            }
#endif
        }

#if SW_INTEROP
        // Tessellate a standalone part document: union all its solid bodies in
        // part-local coords (the model frame), colour from the part material.
        private MeshData TessellatePart()
        {
            var modelDoc = (IModelDoc2)_part;
            object[] bodyObjs = null;
            try { bodyObjs = _part.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[]; }
            catch (Exception ex) { throw new Sw2gzMeshException("Cannot read solid bodies from the part.", ex); }
            if (bodyObjs == null || bodyObjs.Length == 0)
                throw new Sw2gzMeshException("No solid bodies found in the part document.");

            var verts = new List<System.Numerics.Vector3>();
            var tris  = new List<int>();
            try
            {
                foreach (object bodyObj in bodyObjs)
                {
                    Body2 body = bodyObj as Body2;
                    if (body == null) continue;
                    var bodyTess = (ITessellation)body.GetTessellation(null);
                    bodyTess.NeedVertexNormal = false;
                    bodyTess.NeedFaceFacetMap = false;
                    bodyTess.NeedEdgeFinMap   = false;
                    if (!bodyTess.Tessellate())
                    {
                        Marshal.ReleaseComObject(bodyTess);
                        throw new Sw2gzMeshException("ITessellation.Tessellate() returned false for the part.");
                    }
                    int facetCount = bodyTess.GetFacetCount();
                    for (int f = 0; f < facetCount; f++)
                    {
                        int[] fins = (int[])bodyTess.GetFacetFins(f);
                        int baseIdx = verts.Count;
                        for (int fi = 0; fi < 3; fi++)
                        {
                            int[] finVerts = (int[])bodyTess.GetFinVertices(fins[fi]);
                            double[] pt = (double[])bodyTess.GetVertexPoint(finVerts[0]);
                            verts.Add(new System.Numerics.Vector3((float)pt[0], (float)pt[1], (float)pt[2]));
                        }
                        tris.Add(baseIdx); tris.Add(baseIdx + 1); tris.Add(baseIdx + 2);
                    }
                    Marshal.ReleaseComObject(bodyTess);
                }
            }
            catch (COMException ex)
            {
                throw new Sw2gzMeshException("Tessellation failed for the part document.", ex);
            }
            if (verts.Count == 0)
                throw new Sw2gzMeshException("Tessellation produced no geometry for the part.");

            System.Drawing.Color? color = null;
            try
            {
                double[] matProps = modelDoc.MaterialPropertyValues as double[];
                if (matProps != null && matProps.Length >= 8 &&
                    (matProps[0] > 0 || matProps[1] > 0 || matProps[2] > 0))
                {
                    color = System.Drawing.Color.FromArgb(
                        (int)((1.0 - matProps[7]) * 255),
                        (int)(matProps[0] * 255), (int)(matProps[1] * 255), (int)(matProps[2] * 255));
                }
            }
            catch (COMException) { }

            return new MeshData(verts.ToArray(), tris.ToArray(), color);
        }

        // Recursively gather every descendant component that carries solid
        // bodies. A part component is itself a leaf; a sub-assembly component has
        // no bodies of its own, so we descend into GetChildren(). Defensive: any
        // COM hiccup on a branch is swallowed so one bad child can't sink the
        // whole asset. Each returned leaf's own Transform2 is assembly-frame, so
        // no transform composition is needed at the call site.
        private static void CollectBodyComponents(Component2 comp, List<Component2> leaves)
        {
            if (comp == null) return;
            try { if (comp.IsSuppressed()) return; } catch { }

            object[] bodies = null;
            try { bodies = comp.GetBodies2((int)swBodyType_e.swSolidBody) as object[]; }
            catch { bodies = null; }
            if (bodies != null && bodies.Length > 0) { leaves.Add(comp); return; }

            object[] children = null;
            try { children = comp.GetChildren() as object[]; }
            catch { children = null; }
            if (children == null) return;
            foreach (object o in children)
                CollectBodyComponents(o as Component2, leaves);
        }
#endif
    }
}
