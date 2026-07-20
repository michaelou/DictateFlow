using System.Windows;

namespace DictateFlow.App.Views;

/// <summary>
/// Cloud Recordings review window shell. All behavior lives in
/// <see cref="ViewModels.CloudRecordingsViewModel"/>; the window service calls the view model's
/// <c>Cleanup</c> when the window closes to release the audio player and temp files.
/// </summary>
public partial class CloudRecordingsWindow : Window
{
    /// <summary>Initializes a new instance of the <see cref="CloudRecordingsWindow"/> class.</summary>
    public CloudRecordingsWindow() => InitializeComponent();
}
