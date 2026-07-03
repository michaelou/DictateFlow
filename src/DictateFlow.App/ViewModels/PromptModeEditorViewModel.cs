using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Prompts;

namespace DictateFlow.App.ViewModels;

/// <summary>
/// View model backing the prompt mode editor dialog: the mode's fields as editable text,
/// validation on Save, and the validated mode surfaced through <see cref="Result"/>
/// (<see langword="null"/> when the dialog was cancelled).
/// </summary>
public partial class PromptModeEditorViewModel : ObservableObject
{
    private readonly IReadOnlyList<string> _otherModeNames;

    /// <summary>Initializes a new instance of the <see cref="PromptModeEditorViewModel"/> class.</summary>
    /// <param name="existing">The mode being edited, or <see langword="null"/> to create a new one.</param>
    /// <param name="otherModeNames">Names of all other modes, for uniqueness validation (excluding <paramref name="existing"/>).</param>
    public PromptModeEditorViewModel(PromptMode? existing, IReadOnlyList<string> otherModeNames)
    {
        _otherModeNames = otherModeNames;
        IsNew = existing is null;
        OriginalName = existing?.Name;
        _name = existing?.Name ?? "";
        _description = existing?.Description ?? "";
        _systemPrompt = existing?.SystemPrompt ?? "";
        _temperatureText = existing?.Temperature?.ToString(CultureInfo.InvariantCulture) ?? "";
        _llmEnabled = existing?.LlmEnabled ?? true;
    }

    /// <summary>Gets a value indicating whether the dialog creates a new mode rather than editing one.</summary>
    public bool IsNew { get; }

    /// <summary>Gets the name the edited mode had when the dialog opened; <see langword="null"/> for a new mode.</summary>
    public string? OriginalName { get; }

    /// <summary>Gets the window title.</summary>
    public string Title => IsNew ? "New prompt mode" : $"Edit prompt mode — {OriginalName}";

    /// <summary>Gets the validated mode after a successful Save; <see langword="null"/> until then and on Cancel.</summary>
    public PromptMode? Result { get; private set; }

    /// <summary>Raised when the window hosting this view model should close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Gets or sets the mode name (also the JSON file name).</summary>
    [ObservableProperty]
    private string _name;

    /// <summary>Gets or sets the short description shown in the mode lists.</summary>
    [ObservableProperty]
    private string _description;

    /// <summary>Gets or sets the system prompt template; may contain <c>{{Variable}}</c> tokens.</summary>
    [ObservableProperty]
    private string _systemPrompt;

    /// <summary>Gets or sets the temperature as text; empty uses the provider default.</summary>
    [ObservableProperty]
    private string _temperatureText;

    /// <summary>Gets or sets a value indicating whether transcripts are sent through the LLM.</summary>
    [ObservableProperty]
    private bool _llmEnabled;

    /// <summary>Gets or sets the validation message shown under the fields; <see langword="null"/> hides it.</summary>
    [ObservableProperty]
    private string? _validationError;

    /// <summary>Validates the fields; on success exposes <see cref="Result"/> and closes the dialog.</summary>
    [RelayCommand]
    private void Save()
    {
        if (PromptModeNameRules.Validate(Name) is { } nameError)
        {
            ValidationError = nameError;
            return;
        }

        var name = Name.Trim();
        if (_otherModeNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
        {
            ValidationError = $"A mode named '{name}' already exists.";
            return;
        }

        if (LlmEnabled && string.IsNullOrWhiteSpace(SystemPrompt))
        {
            ValidationError = "System prompt is required when the LLM is enabled.";
            return;
        }

        double? temperature = null;
        if (!string.IsNullOrWhiteSpace(TemperatureText))
        {
            if (!double.TryParse(TemperatureText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                || parsed is < 0 or > 2)
            {
                ValidationError = "Temperature must be a number between 0 and 2, or empty for the provider default.";
                return;
            }

            temperature = parsed;
        }

        Result = new PromptMode(name, Description.Trim(), SystemPrompt, temperature, LlmEnabled);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Discards the edits.</summary>
    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
