using SW2GZ.URDFExport;
using Xunit;

namespace SW2GZ.Tests.UI
{
    public class Sw2gzDocSnapshotTests
    {
        [Fact]
        public void Clone_IsIndependent()
        {
            var orig = new Sw2gzDoc { Mode = Sw2gzMode.World };
            orig.Robot.Links.Add("base_link");
            orig.World.Assets.Add("tree<1>");

            var copy = Sw2gzDocSnapshot.Clone(orig);

            Assert.Equal(orig.Mode, copy.Mode);
            Assert.Equal(orig.Robot.Links, copy.Robot.Links);
            Assert.Equal(orig.World.Assets, copy.World.Assets);
            Assert.NotSame(orig.Robot, copy.Robot);
            Assert.NotSame(orig.Robot.Links, copy.Robot.Links);
            Assert.NotSame(orig.World.Assets, copy.World.Assets);
        }

        [Fact]
        public void Mutating_Original_Does_Not_Affect_Clone()
        {
            var orig = new Sw2gzDoc();
            orig.Robot.Links.Add("a");
            var copy = Sw2gzDocSnapshot.Clone(orig);

            orig.Robot.Links.Add("b");
            orig.Mode = Sw2gzMode.Asset;

            Assert.Single(copy.Robot.Links);
            Assert.Equal(Sw2gzMode.Robot, copy.Mode);
        }

        [Fact]
        public void Restore_CopiesInto_Target()
        {
            var live = new Sw2gzDoc { Mode = Sw2gzMode.Robot };
            live.Robot.Links.Add("a");
            var snap = Sw2gzDocSnapshot.Clone(live);

            // Simulate PMP edits
            live.Mode = Sw2gzMode.World;
            live.Robot.Links.Add("b");

            // Cancel → restore
            Sw2gzDocSnapshot.Restore(snap, live);

            Assert.Equal(Sw2gzMode.Robot, live.Mode);
            Assert.Single(live.Robot.Links);
            Assert.Equal("a", live.Robot.Links[0]);
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
