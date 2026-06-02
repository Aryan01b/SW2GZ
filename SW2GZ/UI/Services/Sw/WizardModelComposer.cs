/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P2/P8 — SolidWorks-side composition root for the wizard. Walks the active
assembly (links + mates), runs the same Build steps the pipeline uses, and
produces the three artifacts the wizard view-models need:

  * a preview RobotModel (handed to ReviewStepViewModel.PreviewModel),
  * the LinkDto list (fed to LinksStepViewModel),
  * the joint count (ReviewStepViewModel.jointCount).

This is COM / write-only — it depends on the SW boundary services (mass,
tessellation, appearances) exactly like Sw2gzPipeline, so the body is guarded
by #if SW_INTEROP. It is NOT source-linked into the net8 test project. Its
correctness is validated later via SolidWorks smoke testing.

Kept deliberately thin: it re-uses RobotModelBuilder + JointGraphBuilder
rather than duplicating any assembly logic. The actual SwAddin toolbar hook
that constructs this composer and launches WizardWindow is intentionally NOT
wired here — see the note in the class summary / the task report.
*/
using System;
using System.Collections.Generic;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Math;
using SW2GZ.UI.ViewModels;

#if SW_INTEROP
using SW2GZ.SwSurface.Abstractions;
#endif

namespace SW2GZ.UI.Services.Sw
{
    /// Result bundle the wizard composition root produces from a live assembly.
    public sealed record WizardPreview(
        RobotModel Model,
        IReadOnlyList<LinkDto> Links,
        int JointCount,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<JointDto> Joints);

    public sealed class WizardModelComposer
    {
#if SW_INTEROP
        private readonly IMassProperties _mass;
        private readonly IAssemblyWalker _walker;
        private readonly IMeshTessellator _tess;
        private readonly IAppearanceSource _appearances;

        public WizardModelComposer(
            IMassProperties mass, IAssemblyWalker walker,
            IMeshTessellator tess, IAppearanceSource appearances)
        {
            _mass = mass ?? throw new ArgumentNullException(nameof(mass));
            _walker = walker ?? throw new ArgumentNullException(nameof(walker));
            _tess = tess ?? throw new ArgumentNullException(nameof(tess));
            _appearances = appearances ?? throw new ArgumentNullException(nameof(appearances));
        }
#endif

        // Skeleton ctor so the type is referenceable without SW handles.
        public WizardModelComposer() { }

        /// Walks the active assembly and assembles a preview RobotModel + the
        /// wizard DTOs. `meta` supplies the package metadata the wizard collected
        /// in earlier steps (package name / author / email / license).
        public WizardPreview Compose(RobotMeta meta)
        {
            if (meta == null) throw new ArgumentNullException(nameof(meta));

#if SW_INTEROP
            // ── Links: mirror Sw2gzPipeline Steps 2-3 ────────────────────────
            IReadOnlyList<LinkSpec> specs = _walker.WalkActive();
            var links = new List<UrdfLink>(specs.Count);
            var linksWithPaths = new List<(UrdfLink Link, string PartPath)>(specs.Count);
            var linkDtos = new List<LinkDto>(specs.Count);

            foreach (LinkSpec spec in specs)
            {
                string primaryPath = spec.FlattenedPartPaths[0];
                MeshData visual = _tess.Tessellate(primaryPath, TessellationLod.Fine);
                MeshData collision = ConvexHullCollider.Build(visual, ColliderStrategy.ConvexHull);

                var partsForAgg = new List<(MassProps, Pose)>(spec.FlattenedPartPaths.Count);
                foreach (string partPath in spec.FlattenedPartPaths)
                    partsForAgg.Add((_mass.Get(partPath), Pose.Identity));

                MassProps agg = InertialAggregator.Combine(partsForAgg);
                UrdfLink link = LinkBuilder.Build(spec.Name, agg, visual, collision);

                links.Add(link);
                linksWithPaths.Add((link, primaryPath));
                linkDtos.Add(new LinkDto(link.Name, link.Mass, link.VisualMeshFile));
            }

            // ── Joints: P2 mate graph ────────────────────────────────────────
            IReadOnlyList<MateSpec> mates = _walker.WalkMates();
            var (joints, _root, warnings) = JointGraphBuilder.Build(links, mates);

            // Surface the extracted joints to the wizard's editable Joints step.
            var jointDtos = new List<JointDto>(joints.Count);
            foreach (UrdfJoint j in joints)
                jointDtos.Add(JointDto.From(j));

            // ── Preview RobotModel: reuse the pipeline's build steps ─────────
            var (modelLinks, materials) =
                RobotModelBuilder.AssembleLinksWithMaterials(linksWithPaths, _appearances);
            RobotModel model = RobotModelBuilder.Build(meta, modelLinks, joints, materials);

            return new WizardPreview(model, linkDtos, joints.Count, warnings, jointDtos);
#else
            throw new NotImplementedException(
                "WizardModelComposer.Compose() requires the SolidWorks COM build (SW_INTEROP).");
#endif
        }
    }
}
