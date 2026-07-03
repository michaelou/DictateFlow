using System.Windows;

namespace DictateFlow.App.Views;

/// <summary>
/// Cost Dashboard window shell. All behavior lives in
/// <see cref="ViewModels.CostDashboardViewModel"/>; the code-behind only initializes the view.
/// </summary>
public partial class CostDashboardWindow : Window
{
    /// <summary>Initializes a new instance of the <see cref="CostDashboardWindow"/> class.</summary>
    public CostDashboardWindow()
    {
        InitializeComponent();
    }
}
