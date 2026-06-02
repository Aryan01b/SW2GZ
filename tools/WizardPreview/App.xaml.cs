/*
Standalone WPF preview harness for the SW2GZ wizard (dev-only).

Builds a WizardViewModel wired with the Null* services and representative
sample data, then shows the real WizardWindow — no SolidWorks, no Visual
Studio, just `dotnet run`.
*/
using System.Windows;
using SW2GZ.UI.Services;
using SW2GZ.UI.ViewModels;
using SW2GZ.UI.Wizard;

namespace WizardPreview
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var vm = new WizardViewModel(
                folderBrowser: new NullFolderBrowserService(),
                viewportSelection: new NullViewportSelectionService(),
                themeService: new NullThemeService(),
                exportRunner: new NullExportRunner(),
                links: SampleData.Links,
                jointCount: SampleData.Joints.Count,
                previewModel: SampleData.PreviewModel,
                joints: SampleData.Joints);

            var window = new WizardWindow(vm);
            window.Show();
        }
    }
}
