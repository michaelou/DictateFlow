using System.Windows;

namespace DictateFlow.App.Views;

/// <summary>
/// DictatePad window shell. All behavior lives in <see cref="ViewModels.DictatePadViewModel"/>;
/// the code-behind only initializes the view.
/// </summary>
public partial class DictatePadWindow : Window
{
    /// <summary>Initializes a new instance of the <see cref="DictatePadWindow"/> class.</summary>
    public DictatePadWindow()
    {
        InitializeComponent();
    }
}
