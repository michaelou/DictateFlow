using System.Windows;

namespace DictateFlow.App.Views;

/// <summary>
/// Modal editor dialog for one prompt mode, opened from the Settings Prompts page. All
/// content and behavior are bound to <see cref="ViewModels.PromptModeEditorViewModel"/>.
/// </summary>
public partial class PromptModeEditorWindow : Window
{
    /// <summary>Initializes a new instance of the <see cref="PromptModeEditorWindow"/> class.</summary>
    public PromptModeEditorWindow()
    {
        InitializeComponent();
    }
}
