/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

Unit tests for TfTreeFormatter — the preview-dialog TF tree builder.
*/
using SW2GZ.Build.Model;
using Xunit;

namespace SW2GZ.Test.Build.Model
{
    public class TfTreeFormatterTests
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void Urdf_WorldAnchoredRevolute_RendersFullTreeWithRpyDeg()
        {
            string urdf = @"<?xml version='1.0'?>
<robot name='r'>
  <link name='world'/>
  <joint name='world_to_base_link' type='fixed'>
    <parent link='world'/>
    <child link='base_link'/>
    <origin xyz='0 0 0' rpy='1.570796 0 1.570796'/>
  </joint>
  <link name='base_link'/>
  <link name='arm'/>
  <joint name='shoulder' type='revolute'>
    <parent link='base_link'/>
    <child link='arm'/>
    <origin xyz='0.1 0 0.2' rpy='0 0 0'/>
    <axis xyz='0 0 1'/>
    <limit lower='-1.57' upper='1.57' effort='100' velocity='1.0'/>
  </joint>
</robot>";

            string tree = TfTreeFormatter.FormatUrdf(urdf);

            Assert.Contains("world", tree);
            Assert.Contains("base_link", tree);
            Assert.Contains("arm", tree);
            Assert.Contains("[fixed: world_to_base_link]", tree);
            Assert.Contains("[revolute: shoulder]", tree);
            Assert.Contains("xyz: 0.1 0 0.2", tree);
            Assert.Contains("axis: 0 0 1", tree);
            // SW→ROS rotation legible as both radians AND degrees
            Assert.Contains("rpy: 1.570796 0 1.570796", tree);
            Assert.Contains("(deg: 90", tree);
            // Limits rendered
            Assert.Contains("limit: -1.57 .. 1.57", tree);
            Assert.Contains("effort=100", tree);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Urdf_TwoChildrenOfSameParent_RenderBothBranches()
        {
            string urdf = @"<?xml version='1.0'?>
<robot name='r'>
  <link name='base_link'/>
  <link name='left'/>
  <link name='right'/>
  <joint name='j_left' type='fixed'>
    <parent link='base_link'/><child link='left'/>
  </joint>
  <joint name='j_right' type='fixed'>
    <parent link='base_link'/><child link='right'/>
  </joint>
</robot>";

            string tree = TfTreeFormatter.FormatUrdf(urdf);

            Assert.Contains("├── left", tree);   // not-last child
            Assert.Contains("└── right", tree);  // last child
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Urdf_MalformedXml_ReturnsFriendlyMessage()
        {
            string tree = TfTreeFormatter.FormatUrdf("<robot><not closed");
            Assert.StartsWith("(could not parse URDF", tree);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Urdf_NoLinks_ReturnsFriendlyMessage()
        {
            string tree = TfTreeFormatter.FormatUrdf("<robot></robot>");
            Assert.Equal("(no <link> elements found)", tree);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Sdf_BasicModelWithJoint_RendersTree()
        {
            string sdf = @"<?xml version='1.0'?>
<sdf version='1.10'>
  <model name='r'>
    <link name='base_link'/>
    <link name='arm'/>
    <joint name='shoulder' type='revolute'>
      <parent>base_link</parent>
      <child>arm</child>
      <pose>0.1 0 0.2 0 0 0</pose>
      <axis><xyz>0 0 1</xyz></axis>
    </joint>
  </model>
</sdf>";

            string tree = TfTreeFormatter.FormatSdf(sdf);

            Assert.Contains("base_link", tree);
            Assert.Contains("arm", tree);
            Assert.Contains("[revolute: shoulder]", tree);
            Assert.Contains("pose: 0.1 0 0.2 0 0 0", tree);
            Assert.Contains("axis: 0 0 1", tree);
        }
    }
}
