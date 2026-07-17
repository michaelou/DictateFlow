using System.Windows;

namespace DictateFlow.App.Views;

/// <summary>
/// DictatePad window shell. All behavior lives in <see cref="ViewModels.DictatePadViewModel"/>;
/// the code-behind only initializes the view and places keyboard focus in the scratchpad on
/// open so the user can start dictating (or typing) immediately.
/// </summary>
public partial class DictatePadWindow : Window
{
    /// <summary>Initializes a new instance of the <see cref="DictatePadWindow"/> class.</summary>
    public DictatePadWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>Focuses the scratchpad and moves the caret to the end of any restored text.</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ScratchpadTextBox.Focus();
        ScratchpadTextBox.CaretIndex = ScratchpadTextBox.Text.Length;
    }
}
