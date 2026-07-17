using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Prompts;
using DictateFlow.Core.Services.Replacements;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.ViewModels;

/// <summary>
/// View model backing the DictatePad window: a scratchpad the user dictates into across
/// several passes, then runs the LLM over the <em>whole</em> text with a chosen prompt mode.
/// Enhancement reuses the same seam as the dictation pipeline (<see cref="ITextReplacementService"/>
/// → <see cref="IPromptResolver"/> → <see cref="ILLMProvider"/>) and replaces the text in place,
/// keeping the pre-enhance text for a one-step Undo. The pad is just a focused window, so normal
/// dictation delivers into it with no pipeline changes.
/// </summary>
public partial class DictatePadViewModel : ObservableObject
{
    private readonly IPromptModeStore _modeStore;
    private readonly IPromptResolver _resolver;
    private readonly ILLMProvider _llm;
    private readonly ITextReplacementService _replacements;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<DictatePadViewModel> _logger;

    /// <summary>The text captured before the last Enhance/Clear, restored by <see cref="UndoCommand"/>.</summary>
    private string? _undoText;

    /// <summary>Initializes a new instance of the <see cref="DictatePadViewModel"/> class.</summary>
    /// <param name="modeStore">Supplies the selectable prompt modes.</param>
    /// <param name="resolver">Builds the prompt context for an enhancement call.</param>
    /// <param name="llm">The active LLM provider used to enhance the whole text.</param>
    /// <param name="replacements">Applies the deterministic replacement dictionary before enhancement.</param>
    /// <param name="settingsService">Supplies the default (active) prompt mode.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public DictatePadViewModel(
        IPromptModeStore modeStore,
        IPromptResolver resolver,
        ILLMProvider llm,
        ITextReplacementService replacements,
        ISettingsService settingsService,
        ILogger<DictatePadViewModel> logger)
    {
        _modeStore = modeStore;
        _resolver = resolver;
        _llm = llm;
        _replacements = replacements;
        _settingsService = settingsService;
        _logger = logger;

        LoadPromptModes();
    }

    /// <summary>Gets the names of the prompt modes available for enhancement, ordered by name.</summary>
    public ObservableCollection<string> PromptModes { get; } = [];

    /// <summary>Gets or sets the scratchpad text; dictation delivers into it and Enhance rewrites it.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnhanceCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private string _text = "";

    /// <summary>Gets or sets the name of the prompt mode applied by Enhance.</summary>
    [ObservableProperty]
    private string? _selectedPromptMode;

    /// <summary>Gets or sets the status line shown at the bottom of the window.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Gets or sets a value indicating whether an enhancement is currently running.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnhanceCommand))]
    private bool _isBusy;

    /// <summary>Gets or sets a value indicating whether the last change can be reverted.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private bool _canUndo;

    /// <summary>Runs the chosen prompt mode's LLM over the whole text, replacing it in place.</summary>
    [RelayCommand(CanExecute = nameof(CanEnhance))]
    private async Task EnhanceAsync(CancellationToken cancellationToken)
    {
        var current = Text;
        if (string.IsNullOrWhiteSpace(current))
        {
            return;
        }

        var modeName = string.IsNullOrEmpty(SelectedPromptMode)
            ? DefaultPromptModes.RawModeName
            : SelectedPromptMode;
        IsBusy = true;
        StatusMessage = $"Enhancing with \"{modeName}\"…";
        try
        {
            // Match the pipeline: deterministic replacements first, then the LLM.
            var input = _replacements.Apply(current);
            var context = _resolver.Resolve(input, modeName);

            if (!context.LlmEnabled)
            {
                _undoText = current;
                Text = input;
                CanUndo = !string.Equals(current, input, StringComparison.Ordinal);
                StatusMessage = $"\"{modeName}\" has the LLM disabled; applied replacements only.";
                return;
            }

            var result = await _llm.ProcessAsync(context, cancellationToken);
            _undoText = current;
            Text = result;
            CanUndo = true;
            StatusMessage = $"Enhanced with \"{modeName}\".";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Enhancement cancelled — your text is unchanged.";
        }
        catch (ProviderException ex)
        {
            // Provider messages are safe to show; the text is left untouched.
            _logger.LogWarning(ex, "DictatePad enhancement failed");
            StatusMessage = $"Enhancement failed: {ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error enhancing DictatePad text");
            StatusMessage = "Enhancement failed unexpectedly — your text is unchanged.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanEnhance() => !IsBusy && !string.IsNullOrWhiteSpace(Text);

    /// <summary>Restores the text captured before the last enhancement or clear.</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_undoText is null)
        {
            return;
        }

        Text = _undoText;
        _undoText = null;
        CanUndo = false;
        StatusMessage = "Reverted the last change.";
    }

    /// <summary>Copies the whole scratchpad text to the clipboard.</summary>
    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void Copy()
    {
        try
        {
            Clipboard.SetText(Text);
            StatusMessage = "Copied to clipboard.";
        }
        catch (Exception ex)
        {
            // The clipboard can be locked by another process.
            _logger.LogWarning(ex, "Could not copy the DictatePad text to the clipboard");
            StatusMessage = "Could not access the clipboard — try again.";
        }
    }

    private bool CanCopy() => !string.IsNullOrWhiteSpace(Text);

    /// <summary>Clears the scratchpad, keeping the cleared text available for Undo.</summary>
    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        _undoText = Text;
        Text = "";
        CanUndo = true;
        StatusMessage = "Cleared.";
    }

    private bool CanClear() => !string.IsNullOrEmpty(Text);

    /// <summary>Loads the prompt modes and defaults the selection to the active mode.</summary>
    private void LoadPromptModes()
    {
        try
        {
            _modeStore.Reload();
            PromptModes.Clear();
            foreach (var mode in _modeStore.GetAll())
            {
                PromptModes.Add(mode.Name);
            }

            var active = _settingsService.Current.ActivePromptMode;
            SelectedPromptMode = PromptModes.FirstOrDefault(
                    n => string.Equals(n, active, StringComparison.OrdinalIgnoreCase))
                ?? PromptModes.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load prompt modes for DictatePad");
        }
    }
}
