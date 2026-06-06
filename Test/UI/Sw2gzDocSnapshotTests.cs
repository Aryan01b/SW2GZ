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
    }
}
