/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Asset mode (single part -> reusable Gz Harmonic model). Tessellate the picked
part, bake the SW->ROS rotation into the verts (so the model is Z-up on its
own), recenter X/Y and drop the floor to z=0 (rests on ground when placed),
then write a clean drop-in model dir:
    <outputDir>/<name>/model.config
    <outputDir>/<name>/model.sdf
    <outputDir>/<name>/meshes/<name>.dae
Use it in a world via <include><uri>model://<name></uri></include>.

COM-free (takes IMeshTessellator) so it's unit-testable with a fake.
*/
using System;
using System.IO;
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Gz;
using SW2GZ.Math;
using SW2GZ.SwSurface.Abstractions;
using SW2GZ.Validate;
using SW2GZ.Write.Mesh;

namespace SW2GZ.URDFExport
{
    public static class Sw2gzAssetExporter
    {
        public static ValidationReport Export(
            IMeshTessellator tess, Sw2gzExportConfig config, string outputDir, Matrix3 swToRos)
        {
            if (tess == null) throw new ArgumentNullException(nameof(tess));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(outputDir))
                throw new SW2GZ.Exceptions.Sw2gzExportException(
                    "Output folder is empty — set a target directory in the Export dialog.");
            if (string.IsNullOrWhiteSpace(config.AssetBodyPart))
                throw new SW2GZ.Exceptions.Sw2gzExportException(
                    "No asset body picked — open Create Asset and pick a part.");

            string name = PackageNameSanitizer.Sanitize(config.PackageName).Value;
            string root = Path.Combine(outputDir, name);
            string meshesDir = Path.Combine(root, "meshes");
            string daeFile = name + ".dae";

            MeshData mesh = tess.Tessellate(config.AssetBodyPart, TessellationLod.Fine);
            mesh = RotateAndGround(mesh, swToRos);

            Directory.CreateDirectory(meshesDir);
            DaeWriter.Write(mesh, Path.Combine(meshesDir, daeFile), withNormals: true);

            new ModelConfigWriter(new ModelConfigWriter.Input
            {
                Name = name, Author = config.Author, Email = config.Email,
            }).Write(root);

            // A1 — a joint anchors the link to the world, which is invalid on a
            // static model, so any joint forces the asset dynamic (with a
            // placeholder inertial). No joint → honour the user's static choice.
            string jointType = string.IsNullOrWhiteSpace(config.AssetJointType)
                ? "none" : config.AssetJointType.Trim().ToLowerInvariant();
            bool hasJoint = jointType != "none";
            bool isStatic = config.AssetIsStatic && !hasJoint;

            var modelInput = new SdfAssetModelInput(
                ModelName: name,
                MeshFile: daeFile,
                IsStatic: isStatic,
                FrictionMu: config.AssetFrictionMu,
                Rgba: ToRgba(mesh.MaterialColor),
                Mass: isStatic ? 0.0 : 1.0,   // placeholder mass for dynamic
                JointType: jointType,
                JointAxisX: config.AssetJointAxisX,
                JointAxisY: config.AssetJointAxisY,
                JointAxisZ: config.AssetJointAxisZ,
                JointLower: config.AssetJointLower,
                JointUpper: config.AssetJointUpper);
            File.WriteAllText(Path.Combine(root, "model.sdf"), SdfAssetModelWriter.Write(modelInput));

            return new ValidationReport(Array.Empty<ValidationIssue>());
        }

        // Bake the SW->ROS rotation into the verts, then shift so the model is
        // centred in X/Y with its lowest point at z=0 (rests on the ground).
        private static MeshData RotateAndGround(MeshData mesh, Matrix3 r)
        {
            if (mesh?.Vertices == null || mesh.Vertices.Length == 0) return mesh;

            var rot = new Vector3[mesh.Vertices.Length];
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (int i = 0; i < rot.Length; i++)
            {
                Vector3 v = mesh.Vertices[i];
                var p = new Vector3(
                    (float)(r.M11 * v.X + r.M12 * v.Y + r.M13 * v.Z),
                    (float)(r.M21 * v.X + r.M22 * v.Y + r.M23 * v.Z),
                    (float)(r.M31 * v.X + r.M32 * v.Y + r.M33 * v.Z));
                rot[i] = p;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            // After the SW->ROS rotation the up axis IS ROS +Z, so ground = min Z.
            var shift = new Vector3((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, min.Z);
            for (int i = 0; i < rot.Length; i++) rot[i] -= shift;
            return new MeshData(rot, mesh.Triangles, mesh.MaterialColor);
        }

        private static double[] ToRgba(System.Drawing.Color? c)
        {
            if (c == null) return new[] { 0.8, 0.8, 0.8, 1.0 };
            return new[] { c.Value.R / 255.0, c.Value.G / 255.0, c.Value.B / 255.0, c.Value.A / 255.0 };
        }
    }
}
