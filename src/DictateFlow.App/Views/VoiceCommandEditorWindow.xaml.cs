using System.Windows;

namespace DictateFlow.App.Views;

/// <summary>
/// Modal editor dialog for one user voice command, opened from the Settings Voice Commands page.
/// All content and behavior are bound to <see cref="ViewModels.VoiceCommandEditorViewModel"/>.
/// </summary>
public partial class VoiceCommandEditorWindow : Window
{
    /// <summary>Initializes a new instance of the <see cref="VoiceCommandEditorWindow"/> class.</summary>
    public VoiceCommandEditorWindow()
    {
        InitializeComponent();
    }
}
