/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Unit tests for LinkAnchorMap, MeshRebase, and JointOriginResolver.
These three pure helpers drive the URDF link-anchor / joint-origin
pass in Sw2gzPipeline. Identity-anchor paths are exercised to keep
the legacy byte-identical golden-output guarantee.
*/
using System.Collections.Generic;
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Math;
using SW2GZ.SwSurface.Abstractions;
using Xunit;

namespace SW2GZ.Test.Build.Model
{
    public class LinkAnchorAndJointTests
    {
        private const float Eps = 1e-5f;

        private sealed class FakePoseSource : IComponentPoseSource
        {
            private readonly Dictionary<string, Pose> _map = new Dictionary<string, Pose>();
            public FakePoseSource Set(string path, Pose p) { _map[path] = p; return this; }
            public Pose GetComponentPose(string partPath) =>
                _map.TryGetValue(partPath, out Pose p) ? p : Pose.Identity;
        }

        // ── LinkAnchorMap ────────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        public void LinkAnchorMap_NullSource_AllIdentity()
        {
            var specs = new[]
            {
                new LinkSpec("base", new[] { "/p/base" }),
                new LinkSpec("arm",  new[] { "/p/arm" }),
            };
            var map = LinkAnchorMap.Build(specs, source: null);
            Assert.Equal(Pose.Identity.Position, map["base"].Position);
            Assert.Equal(Pose.Identity.Position, map["arm"].Position);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void LinkAnchorMap_UsesFirstPartPose()
        {
            var src = new FakePoseSource()
                .Set("/p/a1", new Pose(new Vector3(1, 0, 0), Quaternion.Identity))
                .Set("/p/a2", new Pose(new Vector3(99, 0, 0), Quaternion.Identity));   // ignored

            var specs = new[] { new LinkSpec("a", new[] { "/p/a1", "/p/a2" }) };
            var map = LinkAnchorMap.Build(specs, src);
            Assert.Equal(new Vector3(1, 0, 0), map["a"].Position);
        }

        // ── MeshRebase ───────────────────────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        public void MeshRebase_Identity_ReturnsInputUnchanged()
        {
            var verts = new[] { new Vector3(1, 2, 3), new Vector3(4, 5, 6) };
            var mesh = new MeshData(verts, new[] { 0, 1, 0 }, null);
            MeshData out_ = MeshRebase.Apply(mesh, Pose.Identity);
            Assert.Same(mesh, out_);                          // reference-equal: short-circuit
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void MeshRebase_TranslationOnly_SubtractsFromVertices()
        {
            var verts = new[] { new Vector3(5, 5, 5), new Vector3(10, 0, 0) };
            var mesh = new MeshData(verts, new[] { 0, 1, 0 }, null);
            var anchor = new Pose(new Vector3(5, 0, 0), Quaternion.Identity);
            MeshData rebased = MeshRebase.Apply(mesh, anchor);
            Assert.Equal(new Vector3(0, 5, 5), rebased.Vertices[0]);
            Assert.Equal(new Vector3(5, 0, 0), rebased.Vertices[1]);
            Assert.Equal(mesh.Triangles, rebased.Triangles);   // indices preserved
        }

        // ── JointOriginResolver ──────────────────────────────────────────

        [Fact]
        [Trait("Category", "Unit")]
        public void JointOriginResolver_IdentityAnchors_PreserveLegacyOutput()
        {
            var r = JointOriginResolver.Compute(Pose.Identity, Pose.Identity, Vector3.UnitZ);
            Assert.Equal(Vector3.Zero, r.Origin.Position);
            Assert.Equal(Vector3.UnitZ, r.AxisInJointFrame);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void JointOriginResolver_PureTranslation_OriginIsChildMinusParent()
        {
            var parent = new Pose(new Vector3(1, 0, 0), Quaternion.Identity);
            var child  = new Pose(new Vector3(3, 4, 0), Quaternion.Identity);

            var r = JointOriginResolver.Compute(parent, child, Vector3.UnitZ);
            Assert.InRange(r.Origin.Position.X, 2f - Eps, 2f + Eps);
            Assert.InRange(r.Origin.Position.Y, 4f - Eps, 4f + Eps);
            Assert.InRange(r.AxisInJointFrame.Z, 1f - Eps, 1f + Eps);   // axis unchanged
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void JointOriginResolver_RotatedChild_AxisExpressedInChildFrame()
        {
            // Child rotated +90° about Z relative to world. An axis along world
            // +X must come out as world +X seen from the rotated child = +Y in
            // child frame... actually with q rotating world +X by -90° to land
            // on child +Y. Let me reason in System.Numerics:
            //   q = +90° about Z applied to a vector: (1,0,0) → (0,1,0).
            //   childAnchor.Rotation = q; we want axis in CHILD frame given an
            //   assembly-frame axis. childFrameVec = q⁻¹ · v_world.
            //   For v_world = (0,1,0): q⁻¹ rotates by -90° about Z → (1,0,0).
            var child = new Pose(Vector3.Zero,
                Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(System.Math.PI / 2)));
            var r = JointOriginResolver.Compute(Pose.Identity, child, new Vector3(0, 1, 0));
            Assert.InRange(r.AxisInJointFrame.X, 1f - Eps, 1f + Eps);
            Assert.InRange(r.AxisInJointFrame.Y, -Eps, Eps);
            Assert.InRange(r.AxisInJointFrame.Z, -Eps, Eps);
        }
    }
}
