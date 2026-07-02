using System.Windows;

namespace DictateFlow.App.Views;

/// <summary>
/// Settings window shell. All behavior lives in
/// <see cref="ViewModels.SettingsViewModel"/>; the code-behind only initializes the view.
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>Initializes a new instance of the <see cref="SettingsWindow"/> class.</summary>
    public SettingsWindow()
    {
        InitializeComponent();
    }
}
