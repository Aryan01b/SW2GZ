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
            ComputeBounds(tessellated, out Vector3 min, out Vector3 max, out bool anyVerts);
            Vector3 shift = SceneShift(min, max, anyVerts, config.SwUpAxis);

            // GUI camera framed on the (post-reframe) scene so `gz sim` opens
            // looking at the assets. Reframe puts the footprint at the XY origin
            // and the floor at Z=0, so the camera target is the scene's
            // mid-height above origin and the distance scales to its size.
            SdfCamera camera = anyVerts
                ? FramingCamera(min, max, config.SwUpAxis, config.WorldInitialView)
                : null;

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
                Roll: roll, Pitch: pitch, Yaw: yaw,
                Camera: camera,
                Settings: (config.WorldScene ?? new Sw2gzWorldSceneConfig()).ToSceneSettings());

            File.WriteAllText(Path.Combine(root, pkg + ".sdf"), SdfWorldWriter.WriteScene(scene));
            return new ValidationReport(issues);
        }

        // Combined AABB over every tessellated mesh, in SW (pre-reframe) space.
        private static void ComputeBounds(
            List<(string, string, MeshData)> meshes, out Vector3 min, out Vector3 max, out bool any)
        {
            any = false;
            min = new Vector3(float.MaxValue);
            max = new Vector3(float.MinValue);
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
        }

        // Per-axis reframe offset: center the two horizontal axes, and align the
        // floor (extreme point in the DOWN = -up direction) to 0 on the up axis.
        private static Vector3 SceneShift(Vector3 min, Vector3 max, bool any, AxisDirection upAxis)
        {
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

        // Initial GUI camera framing the reframed scene. After reframe the
        // footprint is centered on the XY origin and the floor sits at Z=0 in the
        // ROS (Z-up) world frame, so the camera target is the scene's mid-height
        // above the origin and the stand-off distance scales to the scene size.
        // `view` picks the direction: "iso" (default) | "top" | "front".
        private static SdfCamera FramingCamera(Vector3 min, Vector3 max, AxisDirection upAxis, string view)
        {
            (double ux, double uy, double uz) = upAxis.ToVector();
            int upIdx = ux != 0 ? 0 : (uy != 0 ? 1 : 2);
            float[] mn = { min.X, min.Y, min.Z };
            float[] mx = { max.X, max.Y, max.Z };

            double height = mx[upIdx] - mn[upIdx];
            // The two horizontal extents (every axis that isn't "up").
            double w1 = 0, w2 = 0;
            bool firstSet = false;
            for (int i = 0; i < 3; i++)
            {
                if (i == upIdx) continue;
                double e = mx[i] - mn[i];
                if (!firstSet) { w1 = e; firstSet = true; } else { w2 = e; }
            }
            double footprint = System.Math.Sqrt(w1 * w1 + w2 * w2);
            double size = System.Math.Max(footprint, height);
            double R = size * 1.3 + 1.0;   // +1 keeps a tiny/degenerate scene framed

            float midZ = (float)(height * 0.5);
            var target = new Vector3(0f, 0f, midZ);

            Vector3 pos;
            string v = (view ?? "iso").Trim().ToLowerInvariant();
            if (v == "top")
            {
                pos = new Vector3(0f, 0f, (float)(midZ + R));
            }
            else if (v == "front")
            {
                pos = new Vector3(0f, (float)(-R), midZ);
            }
            else // iso — looking in from the front-left and above
            {
                double el = 30.0 * System.Math.PI / 180.0;   // elevation
                double az = 225.0 * System.Math.PI / 180.0;  // azimuth (front-left)
                pos = new Vector3(
                    (float)(R * System.Math.Cos(el) * System.Math.Cos(az)),
                    (float)(R * System.Math.Cos(el) * System.Math.Sin(az)),
                    (float)(midZ + R * System.Math.Sin(el)));
            }

            // Orient the camera (forward = its local +X) to look at the target.
            Vector3 dir = Vector3.Normalize(target - pos);
            double yaw = System.Math.Atan2(dir.Y, dir.X);
            double sinP = System.Math.Max(-1.0, System.Math.Min(1.0, -(double)dir.Z));
            double pitch = System.Math.Asin(sinP);
            return new SdfCamera(pos.X, pos.Y, pos.Z, 0.0, pitch, yaw);
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
