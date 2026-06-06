using SW2GZ.URDFExport;
using Xunit;

namespace SW2GZ.Tests.UI
{
    public class Sw2gzDocTests
    {
        [Fact]
        public void DefaultMode_IsRobot()
        {
            var doc = new Sw2gzDoc();
            Assert.Equal(Sw2gzMode.Robot, doc.Mode);
        }

        [Fact]
        public void DefaultRobotSubtree_IsNonNull()
        {
            var doc = new Sw2gzDoc();
            Assert.NotNull(doc.Robot);
            Assert.NotNull(doc.World);
            Assert.NotNull(doc.Asset);
        }

        [Fact]
        public void Robot_HasEmptyLinksJointsSensors()
        {
            var doc = new Sw2gzDoc();
            Assert.Empty(doc.Robot.Links);
            Assert.Empty(doc.Robot.Joints);
            Assert.Empty(doc.Robot.Sensors);
        }

        [Theory]
        [InlineData(Sw2gzMode.Robot)]
        [InlineData(Sw2gzMode.World)]
        [InlineData(Sw2gzMode.Asset)]
        public void SetMode_Persists(Sw2gzMode m)
        {
            var doc = new Sw2gzDoc { Mode = m };
            Assert.Equal(m, doc.Mode);
        }
    }
}
