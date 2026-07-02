using System.Windows;

namespace DictateFlow.App.Views;

/// <summary>
/// Modal preview dialog shown before output when <c>Output.Mode</c> is <c>Preview</c>.
/// Unlike the overlay this window intentionally takes focus (the user edits in it); the
/// output gate re-focuses the original target window after it closes. All content and
/// behavior are bound to <see cref="ViewModels.PreviewViewModel"/>.
/// </summary>
public partial class PreviewWindow : Window
{
    /// <summary>Initializes a new instance of the <see cref="PreviewWindow"/> class.</summary>
    public PreviewWindow()
    {
        InitializeComponent();
    }
}
