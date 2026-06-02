/*
Copyright (c) 2026 Aryan Arlikar. MIT License — see CONTRIBUTING.md.

P8 — Step 3. Output folder + package name, with a live ament-safe
sanitization preview (via PackageNameSanitizer). Browse delegates to
IFolderBrowserService. CanAdvance requires a non-empty folder and a
non-empty sanitized package name.
*/
using SW2GZ.Build;
using SW2GZ.UI.Mvvm;
using SW2GZ.UI.Services;

namespace SW2GZ.UI.ViewModels
{
    public sealed class OutputStepViewModel : StepViewModelBase
    {
        private readonly IFolderBrowserService _folderBrowser;
        private string _outputFolder = string.Empty;
        private string _packageName = string.Empty;

        public OutputStepViewModel(IFolderBrowserService folderBrowser)
            : base("Output", "Folder & package")
        {
            _folderBrowser = folderBrowser ?? new NullFolderBrowserService();
            BrowseCommand = new RelayCommand(Browse);
        }

        public string OutputFolder
        {
            get => _outputFolder;
            set
            {
                if (SetProperty(ref _outputFolder, value ?? string.Empty))
                    OnAdvanceabilityChanged();
            }
        }

        public string PackageName
        {
            get => _packageName;
            set
            {
                if (SetProperty(ref _packageName, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(SanitizedPackageName));
                    OnAdvanceabilityChanged();
                }
            }
        }

        /// Live ament-compliant preview of PackageName.
        public string SanitizedPackageName =>
            PackageNameSanitizer.Sanitize(_packageName).Value;

        public RelayCommand BrowseCommand { get; }

        public override bool CanAdvance() =>
            !string.IsNullOrWhiteSpace(_outputFolder) &&
            !string.IsNullOrWhiteSpace(SanitizedPackageName);

        private void Browse()
        {
            string chosen = _folderBrowser.BrowseForFolder(_outputFolder);
            if (!string.IsNullOrEmpty(chosen))
                OutputFolder = chosen;
        }
    }
}
