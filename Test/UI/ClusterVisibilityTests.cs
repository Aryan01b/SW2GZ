using SW2GZ.UI.Ribbon;
using SW2GZ.URDFExport;
using Xunit;

namespace SW2GZ.Tests.UI
{
    public class ClusterVisibilityTests
    {
        [Theory]
        [InlineData(Sw2gzMode.Robot, RibbonCluster.Robot, true)]
        [InlineData(Sw2gzMode.Robot, RibbonCluster.World, false)]
        [InlineData(Sw2gzMode.Robot, RibbonCluster.Asset, false)]
        [InlineData(Sw2gzMode.World, RibbonCluster.Robot, false)]
        [InlineData(Sw2gzMode.World, RibbonCluster.World, true)]
        [InlineData(Sw2gzMode.World, RibbonCluster.Asset, false)]
        [InlineData(Sw2gzMode.Asset, RibbonCluster.Robot, false)]
        [InlineData(Sw2gzMode.Asset, RibbonCluster.World, false)]
        [InlineData(Sw2gzMode.Asset, RibbonCluster.Asset, true)]
        public void ModeClusters_Visible_OnlyOnMatch(
            Sw2gzMode mode, RibbonCluster cluster, bool expected)
        {
            Assert.Equal(expected, ClusterVisibility.IsVisible(mode, cluster));
        }

        [Theory]
        [InlineData(Sw2gzMode.Robot)]
        [InlineData(Sw2gzMode.World)]
        [InlineData(Sw2gzMode.Asset)]
        public void Common_AlwaysVisible(Sw2gzMode mode)
        {
            Assert.True(ClusterVisibility.IsVisible(mode, RibbonCluster.Common));
        }
    }
}
