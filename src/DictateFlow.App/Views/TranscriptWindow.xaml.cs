using System.Windows;

namespace DictateFlow.App.Views;

/// <summary>
/// Temporary (until M5 output) window that displays the latest transcript with a manual
/// Copy button. All behavior lives in <see cref="ViewModels.TranscriptViewModel"/>.
/// </summary>
public partial class TranscriptWindow : Window
{
    /// <summary>Initializes a new instance of the <see cref="TranscriptWindow"/> class.</summary>
    public TranscriptWindow()
    {
        InitializeComponent();
    }
}
