using System.Collections.Generic;
using SW2GZ.Build.Model;
using SW2GZ.Ros2;
using SW2GZ.URDFExport;
using Xunit;

namespace Test.URDFExport
{
    public class StackRibbonGateTests
    {
        private static Sw2gzExportConfig Cfg(ExportMode mode, int links)
        {
            var c = new Sw2gzExportConfig { Mode = mode };
            for (int i = 0; i < links; i++) c.Links.Add(new LinkDef { Name = "l" + i });
            return c;
        }

        [Fact] public void Enabled_WhenRobotPackage_AndHasLinks()
            => Assert.True(StackRibbonGate.IsEnabled(Cfg(ExportMode.RobotPackage, 2)));

        [Fact] public void Disabled_WhenNoLinks()
            => Assert.False(StackRibbonGate.IsEnabled(Cfg(ExportMode.RobotPackage, 0)));

        [Fact] public void Disabled_ForSdfWorld()
            => Assert.False(StackRibbonGate.IsEnabled(Cfg(ExportMode.SdfWorld, 3)));

        [Fact] public void Disabled_ForSdfModel()
            => Assert.False(StackRibbonGate.IsEnabled(Cfg(ExportMode.SdfModel, 3)));

        [Fact] public void Disabled_WhenConfigNull()
            => Assert.False(StackRibbonGate.IsEnabled(null));
    }
}
