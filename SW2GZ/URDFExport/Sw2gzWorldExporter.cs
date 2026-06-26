/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

World mode (assembly → Gz Harmonic world). Each picked component becomes one
inlined static <model> in a single self-contained worlds/<name>.sdf, its mesh
(assembly-frame, baked by the tessellator) shared between <visual> and
<collision>. Ground is just a labeled asset (its real CAD mesh); if no ground
was picked the writer falls back to a default flat ground_plane.

COM-free: takes IMeshTessellator (the SW boundary), so the whole flow is
unit-testable with a fake tessellator. Output layout (no ament package — the
approved v1 scope is one .sdf + meshes/):
    <outputDir>/<pkg>/<pkg>.sdf
    <outputDir>/<pkg>/meshes/<name>.dae
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Gz;
using SW2GZ.SwSurface.Abstractions;
using SW2GZ.Validate;
using SW2GZ.Write.Mesh;

namespace SW2GZ.URDFExport
{
    public static class Sw2gzWorldExporter
    {
        public static ValidationReport Export(
            IMeshTessellator tess, Sw2gzExportConfig config, string outputDir,
            double roll, double pitch, double yaw)
        {
            if (tess == null) throw new ArgumentNullException(nameof(tess));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(outputDir))
                throw new SW2GZ.Exceptions.Sw2gzExportException(
                    "Output folder is empty — set a target directory in the Export dialog.");

            string pkg = PackageNameSanitizer.Sanitize(config.PackageName).Value;
            string root = Path.Combine(outputDir, pkg);
            string meshesDir = Path.Combine(root, "meshes");

            // Ground first (labeled asset, real mesh), then the auto-located assets.
            var components = new List<string>();
            if (!string.IsNullOrWhiteSpace(config.WorldGround)) components.Add(config.WorldGround);
            if (config.WorldAssets != null) components.AddRange(config.WorldAssets);

            var issues = new List<ValidationIssue>();
            var usedNames = new HashSet<string>(StringComparer.Ordinal);

            // Pass 1 — tessellate everything (assembly-frame, baked) up front so
            // we can recenter the whole scene before writing any mesh.
            var tessellated = new List<(string ModelName, string DaeFile, MeshData Mesh)>();
            foreach (string compName in components)
            {
                if (string.IsNullOrWhiteSpace(compName)) continue;
                string modelName = UniqueName(RosNameSanitizer.Sanitize(compName).Value, usedNames);
                try
                {
                    MeshData mesh = tess.Tessellate(compName, TessellationLod.Fine);
                    tessellated.Add((modelName, modelName + ".dae", mesh));
                }
                catch (Exception ex)
                {
                    // One un-tessellatable component (e.g. a sub-assembly the
                    // tessellator can't read bodies from) must not sink the
                    // whole world — skip it and surface a warning for review.
                    usedNames.Remove(modelName);
                    issues.Add(new ValidationIssue(IssueSeverity.Warning, "WORLD.SKIP",
                        "Skipped component '" + compName + "' — could not tessellate: " + ex.Message,
                        "Sw2gzWorldExporter"));
                }
            }

            // Reframe — a SW assembly is modeled far from the origin, so the
            // baked placement would drop the world off-camera in Gz (default view
            // looks at 0,0,0). Center the two HORIZONTAL axes, and put the FLOOR
            // (the lowest point along the SW up axis) at 0 so the ground sits on
            // Gz's grid plane rather than the scene's vertical mid-point. The
            // SW→ROS rpy rides each model's <pose> and rotates the up axis onto
            // ROS +Z about the origin, so floor-at-0 lands the ground at Z=0.
            Vector3 shift = SceneShift(tessellated, config.SwUpAxis);

            var models = new List<SdfSceneModel>();
            Directory.CreateDirectory(meshesDir);
            foreach (var (modelName, daeFile, mesh) in tessellated)
            {
                MeshData shifted = Recenter(mesh, shift);
                DaeWriter.Write(shifted, Path.Combine(meshesDir, daeFile), withNormals: true);
                models.Add(new SdfSceneModel(modelName, daeFile, ToRgba(mesh.MaterialColor)));
            }

            var scene = new SdfSceneInput(
                WorldName: pkg,
                Models: models,
                IncludeGroundPlane: string.IsNullOrWhiteSpace(config.WorldGround),
                PhysicsEngine: config.WorldPhysicsEngine,
                MaxStepSize: config.WorldMaxStepSize,
                RealTimeFactor: config.WorldRealTimeFactor,
                Roll: roll, Pitch: pitch, Yaw: yaw);

            File.WriteAllText(Path.Combine(root, pkg + ".sdf"), SdfWorldWriter.WriteScene(scene));
            return new ValidationReport(issues);
        }

        // Per-axis reframe offset: center the two horizontal axes, and align the
        // floor (extreme point in the DOWN = -up direction) to 0 on the up axis.
        private static Vector3 SceneShift(List<(string, string, MeshData)> meshes, AxisDirection upAxis)
        {
            bool any = false;
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var (_, _, mesh) in meshes)
            {
                if (mesh?.Vertices == null) continue;
                foreach (Vector3 v in mesh.Vertices)
                {
                    any = true;
                    min = Vector3.Min(min, v);
                    max = Vector3.Max(max, v);
                }
            }
            if (!any) return Vector3.Zero;

            (double ux, double uy, double uz) = upAxis.ToVector();
            int upIdx = ux != 0 ? 0 : (uy != 0 ? 1 : 2);
            double upSign = upIdx == 0 ? ux : (upIdx == 1 ? uy : uz);

            float[] mn = { min.X, min.Y, min.Z };
            float[] mx = { max.X, max.Y, max.Z };
            var shift = new float[3];
            for (int i = 0; i < 3; i++)
            {
                if (i == upIdx)
                    // +up → floor is the min coord; -up → floor is the max coord.
                    shift[i] = upSign > 0 ? mn[i] : mx[i];
                else
                    shift[i] = (mn[i] + mx[i]) * 0.5f;
            }
            return new Vector3(shift[0], shift[1], shift[2]);
        }

        private static double[] ToRgba(System.Drawing.Color? c)
        {
            // Default neutral gray matches how SolidWorks shows a part with no
            // appearance assigned, so every asset still gets a clean flat color.
            if (c == null) return new[] { 0.8, 0.8, 0.8, 1.0 };
            return new[] { c.Value.R / 255.0, c.Value.G / 255.0, c.Value.B / 255.0, c.Value.A / 255.0 };
        }

        private static MeshData Recenter(MeshData mesh, Vector3 center)
        {
            if (center == Vector3.Zero || mesh?.Vertices == null) return mesh;
            var shifted = new Vector3[mesh.Vertices.Length];
            for (int i = 0; i < shifted.Length; i++) shifted[i] = mesh.Vertices[i] - center;
            return new MeshData(shifted, mesh.Triangles, mesh.MaterialColor);
        }

        private static string UniqueName(string baseName, HashSet<string> used)
        {
            string name = baseName;
            int n = 2;
            while (!used.Add(name)) name = baseName + "_" + n++;
            return name;
        }
    }
}
