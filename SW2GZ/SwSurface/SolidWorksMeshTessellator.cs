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
#endif

        // Skeleton ctor — preserves Moq test: Tessellate() throws NotImplementedException.
        public SolidWorksMeshTessellator() { }

#if SW_INTEROP
        // Real ctor for production use.
        public SolidWorksMeshTessellator(SldWorks swApp, AssemblyDoc doc)
        {
            _swApp = swApp;
            _doc = doc;
        }
#endif

        public MeshData Tessellate(string componentPathName, TessellationLod lod)
        {
#if SW_INTEROP
            if (_swApp == null || _doc == null)
#endif
                throw new NotImplementedException(
                    "SolidWorksMeshTessellator.Tessellate() not yet wired to SldWorks API — see Task 28.");

#if SW_INTEROP
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
                // Get solid bodies directly from the component (works for parts; sub-assemblies
                // would need recursion via GetBodies3 — deferred to v2.1).
                object[] bodyObjs = null;
                try
                {
                    bodyObjs = (object[])comp.GetBodies2((int)swBodyType_e.swSolidBody);
                }
                catch (InvalidCastException)
                {
                    throw new Sw2gzMeshException(
                        "Cannot obtain solid bodies for component: " + componentPathName);
                }

                if (bodyObjs == null || bodyObjs.Length == 0)
                    throw new Sw2gzMeshException(
                        "No solid bodies found in component: " + componentPathName);

                Body2 body = (Body2)bodyObjs[0];

                // Build the tessellation using the ITessellation API.
                var verts = new List<System.Numerics.Vector3>();
                var tris  = new List<int>();

                try
                {
                    // GetTessellation(null) requests tessellation of all faces.
                    tess = (ITessellation)body.GetTessellation(null);
                    tess.NeedVertexNormal = false;
                    tess.NeedFaceFacetMap = false;
                    tess.NeedEdgeFinMap   = false;

                    bool ok = tess.Tessellate();
                    if (!ok)
                        throw new Sw2gzMeshException(
                            "ITessellation.Tessellate() returned false for: " + componentPathName);

                    int facetCount = tess.GetFacetCount();
                    for (int f = 0; f < facetCount; f++)
                    {
                        // Each facet has exactly 3 fins; each fin is an edge with 2 vertex ids.
                        // We collect the 3 unique vertex ids that form the triangle by taking the
                        // first vertex of each of the 3 fins.
                        int[] fins = (int[])tess.GetFacetFins(f);  // int[3]
                        int baseIdx = verts.Count;

                        for (int fi = 0; fi < 3; fi++)
                        {
                            int finId = fins[fi];
                            // GetFinVertices returns int[2] — use vertex [0] of each fin.
                            int[] finVerts = (int[])tess.GetFinVertices(finId);  // int[2]
                            double[] pt = (double[])tess.GetVertexPoint(finVerts[0]); // double[3]
                            verts.Add(new System.Numerics.Vector3(
                                (float)pt[0], (float)pt[1], (float)pt[2]));
                        }

                        tris.Add(baseIdx);
                        tris.Add(baseIdx + 1);
                        tris.Add(baseIdx + 2);
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

                // Extract material color from the model doc (same source as v1 ExportHelperExtension).
                // ModelDoc2.MaterialPropertyValues → double[9]
                // [ R, G, B, Ambient, Diffuse, Specular, Shininess, Transparency, Emission ]
                System.Drawing.Color? color = null;
                try
                {
                    double[] matProps = (double[])model.MaterialPropertyValues;
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
    }
}
