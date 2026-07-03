using System.Windows;

namespace DictateFlow.App.Views;

/// <summary>
/// History window shell. All behavior lives in <see cref="ViewModels.HistoryViewModel"/>;
/// the code-behind only initializes the view.
/// </summary>
public partial class HistoryWindow : Window
{
    /// <summary>Initializes a new instance of the <see cref="HistoryWindow"/> class.</summary>
    public HistoryWindow()
    {
        InitializeComponent();
    }
}
