using System.Collections.Generic;
using SW2GZ.Build.Model;
using SW2GZ.Build.Urdf;
using SW2GZ.URDFExport;
using Xunit;

namespace SW2GZ.Tests.UI
{
    public class Sw2gzDocSnapshotTests
    {
        private static LinkDef MakeLink(string name, string parent = "", params string[] componentIds)
            => new LinkDef { Name = name, ParentName = parent, ComponentIds = new List<string>(componentIds) };

        [Fact]
        public void Clone_IsIndependent()
        {
            var orig = new Sw2gzDoc { Mode = Sw2gzMode.World };
            orig.Robot.Links.Add(MakeLink("base_link", "", "base<1>"));
            orig.World.Assets.Add("tree<1>");

            var copy = Sw2gzDocSnapshot.Clone(orig);

            Assert.Equal(orig.Mode, copy.Mode);
            Assert.Single(copy.Robot.Links);
            Assert.Equal("base_link", copy.Robot.Links[0].Name);
            Assert.Equal(orig.World.Assets, copy.World.Assets);
            Assert.NotSame(orig.Robot, copy.Robot);
            Assert.NotSame(orig.Robot.Links, copy.Robot.Links);
            Assert.NotSame(orig.Robot.Links[0], copy.Robot.Links[0]);
            Assert.NotSame(orig.Robot.Links[0].ComponentIds, copy.Robot.Links[0].ComponentIds);
            Assert.NotSame(orig.World.Assets, copy.World.Assets);
        }

        [Fact]
        public void Mutating_Original_Does_Not_Affect_Clone()
        {
            var orig = new Sw2gzDoc();
            orig.Robot.Links.Add(MakeLink("a"));
            var copy = Sw2gzDocSnapshot.Clone(orig);

            orig.Robot.Links.Add(MakeLink("b"));
            orig.Robot.Links[0].ComponentIds.Add("late<1>");
            orig.Mode = Sw2gzMode.Asset;

            Assert.Single(copy.Robot.Links);
            Assert.Empty(copy.Robot.Links[0].ComponentIds);
            Assert.Equal(Sw2gzMode.Robot, copy.Mode);
        }

        [Fact]
        public void Restore_CopiesInto_Target()
        {
            var live = new Sw2gzDoc { Mode = Sw2gzMode.Robot };
            live.Robot.Links.Add(MakeLink("a"));
            var snap = Sw2gzDocSnapshot.Clone(live);

            // Simulate PMP edits
            live.Mode = Sw2gzMode.World;
            live.Robot.Links.Add(MakeLink("b"));

            // Cancel → restore
            Sw2gzDocSnapshot.Restore(snap, live);

            Assert.Equal(Sw2gzMode.Robot, live.Mode);
            Assert.Single(live.Robot.Links);
            Assert.Equal("a", live.Robot.Links[0].Name);
        }

        [Fact]
        public void Clone_DeepCopies_JointDef()
        {
            var orig = new Sw2gzDoc();
            orig.Robot.Joints.Add(new JointDef
            {
                Name = "j1",
                ParentLink = "base",
                ChildLink = "arm",
                Type = UrdfJointType.Revolute,
                MateName = "Concentric1",
                AxisX = 1.0, AxisY = 0.0, AxisZ = 0.0,
                LimitLower = -1.5, LimitUpper = 1.5,
            });

            var copy = Sw2gzDocSnapshot.Clone(orig);

            Assert.Single(copy.Robot.Joints);
            var j = copy.Robot.Joints[0];
            Assert.Equal("j1", j.Name);
            Assert.Equal(UrdfJointType.Revolute, j.Type);
            Assert.Equal("Concentric1", j.MateName);
            Assert.Equal(1.0, j.AxisX);
            Assert.Equal(-1.5, j.LimitLower);
            Assert.Equal(1.5, j.LimitUpper);
            Assert.NotSame(orig.Robot.Joints[0], j);
        }

        [Fact]
        public void Restore_RestoresWorldAndAssetSubtrees()
        {
            var live = new Sw2gzDoc();
            live.World.Ground = "ground.STL";
            live.World.Assets.Add("tree<1>");
            live.World.Assets.Add("rock<1>");
            live.World.PhysicsEngine = "bullet";
            live.World.MaxStepSize = 0.002;
            live.World.RealTimeFactor = 0.5;
            live.Asset.BodyPart = "prop.SLDPRT";
            live.Asset.FrictionMu = 1.2;
            live.Asset.IsStatic = false;

            var snap = Sw2gzDocSnapshot.Clone(live);

            // Simulate PMP edits across every World + Asset field
            live.World.Ground = "";
            live.World.Assets.Clear();
            live.World.PhysicsEngine = "ode";
            live.World.MaxStepSize = 0.01;
            live.World.RealTimeFactor = 1.0;
            live.Asset.BodyPart = "";
            live.Asset.FrictionMu = 0.3;
            live.Asset.IsStatic = true;

            // Cancel → restore
            Sw2gzDocSnapshot.Restore(snap, live);

            Assert.Equal("ground.STL", live.World.Ground);
            Assert.Equal(2, live.World.Assets.Count);
            Assert.Equal("tree<1>", live.World.Assets[0]);
            Assert.Equal("rock<1>", live.World.Assets[1]);
            Assert.Equal("bullet", live.World.PhysicsEngine);
            Assert.Equal(0.002, live.World.MaxStepSize);
            Assert.Equal(0.5, live.World.RealTimeFactor);
            Assert.Equal("prop.SLDPRT", live.Asset.BodyPart);
            Assert.Equal(1.2, live.Asset.FrictionMu);
            Assert.False(live.Asset.IsStatic);
        }
    }
}
