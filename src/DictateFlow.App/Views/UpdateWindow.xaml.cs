using System.Windows;

namespace DictateFlow.App.Views;

/// <summary>
/// The "Check for updates" dialog shell. All behavior lives in
/// <see cref="ViewModels.UpdateViewModel"/>; the code-behind only initializes the view.
/// </summary>
public partial class UpdateWindow : Window
{
    /// <summary>Initializes a new instance of the <see cref="UpdateWindow"/> class.</summary>
    public UpdateWindow()
    {
        InitializeComponent();
    }
}
