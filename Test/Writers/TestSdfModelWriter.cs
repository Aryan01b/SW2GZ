/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.
*/
using System.Numerics;
using SW2GZ.Build;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.Gz;
using SW2GZ.Math;
using Xunit;

namespace SW2GZ.Test.Writers
{
    public class TestSdfModelWriter : WriterTestBase
    {
        // ---- helpers ----
        // MeshData ctor is (Vector3[] Vertices, int[] Triangles, Color? MaterialColor).
        private static MeshData OneTri() => new MeshData(
            new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) },
            new[] { 0, 1, 2 }, null);

        private static RobotModel TwoLinkModel()
        {
            var l0 = LinkBuilder.Build("base_link",
                new MassProps(2.0, Vector3.Zero, Matrix3.Identity), OneTri(), OneTri());
            var l1 = LinkBuilder.Build("arm",
                new MassProps(1.0, Vector3.Zero, Matrix3.Identity), OneTri(), OneTri());
            var links = new[]
            {
                new ModelLink(l0, "blue", null),
                new ModelLink(l1, null, null),
            };
            var joints = new[]
            {
                new UrdfJoint("shoulder", UrdfJointType.Revolute, "base_link", "arm",
                    Pose.Identity, Vector3.UnitZ, -1.0, 1.0, 10.0, 2.0, UrdfCmdInterface.Position),
            };
            var mats = new[] { new MaterialDef("blue", 0.0, 0.0, 1.0, 1.0) };
            var meta = new RobotMeta("my_asset", "A", "a@b", "MIT", CoordinateConvention.Identity);
            // ControlSpec has no static Default — construct it (it is unused by the SDF writer).
            var control = new ControlSpec(new[] { "shoulder" }, ControlSpec.DefaultJointStateBroadcaster);
            return new RobotModel(meta, links, joints, mats,
                System.Array.Empty<SensorDef>(), control);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Serialize_EmitsModelWithLinksVisualCollisionInertial()
        {
            string sdf = SdfModelWriter.Serialize(TwoLinkModel());
            Assert.Contains("<sdf version=\"1.10\">", sdf);
            Assert.Contains("<model name=\"my_asset\">", sdf);
            Assert.Contains("<link name=\"base_link\">", sdf);
            Assert.Contains("<inertial>", sdf);
            Assert.Contains("<mass>2</mass>", sdf);
            Assert.Contains("<visual name=\"base_link_visual\">", sdf);
            Assert.Contains("<collision name=\"base_link_collision\">", sdf);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Serialize_MeshUrisUseModelScheme()
        {
            string sdf = SdfModelWriter.Serialize(TwoLinkModel());
            Assert.Contains("<uri>model://my_asset/meshes/base_link.dae</uri>", sdf);
            Assert.Contains("<uri>model://my_asset/meshes/base_link_collision.stl</uri>", sdf);
            Assert.DoesNotContain("package://", sdf);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Serialize_EmitsMaterialColorWhenNamed()
        {
            string sdf = SdfModelWriter.Serialize(TwoLinkModel());
            Assert.Contains("<diffuse>0 0 1 1</diffuse>", sdf);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Serialize_EmitsSdfJointWithParentChildAxisLimit()
        {
            string sdf = SdfModelWriter.Serialize(TwoLinkModel());
            Assert.Contains("<joint name=\"shoulder\" type=\"revolute\">", sdf);
            Assert.Contains("<parent>base_link</parent>", sdf);
            Assert.Contains("<child>arm</child>", sdf);
            Assert.Contains("<xyz>0 0 1</xyz>", sdf);
            Assert.Contains("<lower>-1</lower><upper>1</upper><effort>10</effort><velocity>2</velocity>", sdf);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Serialize_PosesChildLinkRelativeToParentViaJointOrigin()
        {
            var l0 = LinkBuilder.Build("base_link",
                new MassProps(1.0, Vector3.Zero, Matrix3.Identity), OneTri(), OneTri());
            var l1 = LinkBuilder.Build("arm",
                new MassProps(1.0, Vector3.Zero, Matrix3.Identity), OneTri(), OneTri());
            // origin: 0.5 along X, 0.25 along Z, no rotation — adjust the Pose ctor call
            // to match the actual Pose constructor you confirmed.
            var origin = new Pose(new Vector3(0.5f, 0f, 0.25f), System.Numerics.Quaternion.Identity);
            var joint = new UrdfJoint("shoulder", UrdfJointType.Revolute, "base_link", "arm",
                origin, Vector3.UnitZ, -1.0, 1.0, 10.0, 2.0, UrdfCmdInterface.Position);
            var meta = new RobotMeta("posed", "A", "a@b", "MIT", CoordinateConvention.Identity);
            var model = new RobotModel(meta,
                new[] { new ModelLink(l0, null, null), new ModelLink(l1, null, null) },
                new[] { joint }, System.Array.Empty<MaterialDef>(),
                System.Array.Empty<SensorDef>(),
                new ControlSpec(new[] { "shoulder" }, ControlSpec.DefaultJointStateBroadcaster));

            string sdf = SdfModelWriter.Serialize(model);
            // Child link 'arm' is placed relative to 'base_link' via the joint origin.
            Assert.Contains("<pose relative_to=\"base_link\">0.5 0 0.25 0 0 0</pose>", sdf);
            // The joint itself no longer carries a pose.
            Assert.DoesNotContain("relative_to=\"base_link\">0.5 0 0.25 0 0 0</pose>\n    </joint>", sdf);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Serialize_ContinuousJointMapsToRevoluteWithoutLimit()
        {
            var l0 = LinkBuilder.Build("base_link",
                new MassProps(1.0, Vector3.Zero, Matrix3.Identity), OneTri(), OneTri());
            var l1 = LinkBuilder.Build("wheel",
                new MassProps(1.0, Vector3.Zero, Matrix3.Identity), OneTri(), OneTri());
            var joint = new UrdfJoint("axle", UrdfJointType.Continuous, "base_link", "wheel",
                Pose.Identity, Vector3.UnitY, null, null, 5.0, 3.0, UrdfCmdInterface.Velocity);
            var meta = new RobotMeta("cont", "A", "a@b", "MIT", CoordinateConvention.Identity);
            var model = new RobotModel(meta,
                new[] { new ModelLink(l0, null, null), new ModelLink(l1, null, null) },
                new[] { joint }, System.Array.Empty<MaterialDef>(),
                System.Array.Empty<SensorDef>(),
                new ControlSpec(new[] { "axle" }, ControlSpec.DefaultJointStateBroadcaster));

            string sdf = SdfModelWriter.Serialize(model);
            Assert.Contains("<joint name=\"axle\" type=\"revolute\">", sdf);
            Assert.Contains("<xyz>0 1 0</xyz>", sdf);
            Assert.DoesNotContain("<limit>", sdf);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Write_RobotModel_WritesModelSdfFile()
        {
            SdfModelWriter.Write(TwoLinkModel(), TempDir);
            Assert.True(Exists("model.sdf"));
        }
    }
}
