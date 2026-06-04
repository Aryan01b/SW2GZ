/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Unit tests for UrdfTransforms — the world-frame link-placement walker
consumed by the 3D preview.
*/
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SW2GZ.Build.Model;
using Xunit;

namespace SW2GZ.Test.Build.Model
{
    public class UrdfTransformsTests
    {
        private const float Eps = 1e-5f;

        [Fact]
        [Trait("Category", "Unit")]
        public void TwoLink_FixedJointWithTranslation_PlacesChildInWorld()
        {
            string urdf = @"<?xml version='1.0'?>
<robot name='r'>
  <link name='base_link'/>
  <link name='arm'/>
  <joint name='base_to_arm' type='fixed'>
    <parent link='base_link'/><child link='arm'/>
    <origin xyz='1 2 3' rpy='0 0 0'/>
  </joint>
</robot>";

            var placements = UrdfTransforms.Compute(urdf).ToDictionary(p => p.LinkName, p => p.LinkToWorld);

            Assert.Equal(Matrix4x4.Identity, placements["base_link"]);
            Vector3 armPos = placements["arm"].Translation;
            Assert.InRange(armPos.X, 1 - Eps, 1 + Eps);
            Assert.InRange(armPos.Y, 2 - Eps, 2 + Eps);
            Assert.InRange(armPos.Z, 3 - Eps, 3 + Eps);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void WorldToBaseLink_NinetyDegRpy_PlacesBaseAtRotatedFrame()
        {
            // world (root) → base_link via a +90° roll. Probe vector (0,1,0)
            // in base frame becomes (0,0,1) in world after the rotation.
            string urdf = @"<?xml version='1.0'?>
<robot name='r'>
  <link name='world'/>
  <link name='base_link'/>
  <joint name='world_to_base' type='fixed'>
    <parent link='world'/><child link='base_link'/>
    <origin xyz='0 0 0' rpy='1.5707963 0 0'/>
  </joint>
</robot>";

            var placements = UrdfTransforms.Compute(urdf).ToDictionary(p => p.LinkName, p => p.LinkToWorld);
            Matrix4x4 m = placements["base_link"];

            Vector3 yInBase = new Vector3(0, 1, 0);
            Vector3 yInWorld = Vector3.Transform(yInBase, m);

            Assert.InRange(yInWorld.X, -Eps, Eps);
            Assert.InRange(yInWorld.Y, -Eps, Eps);
            Assert.InRange(yInWorld.Z, 1 - Eps, 1 + Eps);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void NestedJoints_ComposeTransformsLeftToRight()
        {
            // world → a (+x by 1) → b (+y by 1 in a's frame).
            string urdf = @"<?xml version='1.0'?>
<robot name='r'>
  <link name='world'/>
  <link name='a'/>
  <link name='b'/>
  <joint name='j1' type='fixed'>
    <parent link='world'/><child link='a'/>
    <origin xyz='1 0 0' rpy='0 0 0'/>
  </joint>
  <joint name='j2' type='fixed'>
    <parent link='a'/><child link='b'/>
    <origin xyz='0 1 0' rpy='0 0 0'/>
  </joint>
</robot>";

            var placements = UrdfTransforms.Compute(urdf).ToDictionary(p => p.LinkName, p => p.LinkToWorld);

            Assert.Equal(new Vector3(1, 0, 0), placements["a"].Translation);
            Assert.Equal(new Vector3(1, 1, 0), placements["b"].Translation);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void MalformedXml_ReturnsEmpty()
        {
            Assert.Empty(UrdfTransforms.Compute("<robot><not closed"));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Empty_ReturnsEmpty()
        {
            Assert.Empty(UrdfTransforms.Compute(""));
        }
    }
}
