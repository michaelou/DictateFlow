using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Commands;

namespace DictateFlow.App.ViewModels;

/// <summary>
/// View model backing the voice command editor dialog: the command's fields as editable text,
/// validation on Save (name, uniqueness, and the selected action type's own configuration rules),
/// and the validated command surfaced through <see cref="Result"/> (<see langword="null"/> when
/// the dialog was cancelled). Mirrors <see cref="PromptModeEditorViewModel"/>.
/// </summary>
public partial class VoiceCommandEditorViewModel : ObservableObject
{
    private readonly IReadOnlyList<string> _otherNames;
    private readonly ICommandActionResolver _actionResolver;

    /// <summary>Initializes a new instance of the <see cref="VoiceCommandEditorViewModel"/> class.</summary>
    /// <param name="existing">The command being edited, or <see langword="null"/> to create a new one.</param>
    /// <param name="otherNames">Names of all other user commands, for uniqueness validation (excluding <paramref name="existing"/>).</param>
    /// <param name="actionTypes">The action types offered in the dropdown.</param>
    /// <param name="actionResolver">Validates the chosen action type's configuration on Save.</param>
    public VoiceCommandEditorViewModel(
        CommandDefinition? existing,
        IReadOnlyList<string> otherNames,
        IReadOnlyList<string> actionTypes,
        ICommandActionResolver actionResolver)
    {
        _otherNames = otherNames;
        _actionResolver = actionResolver;
        ActionTypes = actionTypes;

        IsNew = existing is null;
        OriginalName = existing?.Name;
        _name = existing?.Name ?? "";
        _phrasesText = existing is null ? "" : string.Join(Environment.NewLine, existing.Phrases);
        _actionType = existing?.ActionType is { Length: > 0 } type && actionTypes.Contains(type, StringComparer.OrdinalIgnoreCase)
            ? actionTypes.First(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase))
            : actionTypes.FirstOrDefault() ?? "";
        _actionValue = existing?.ActionValue ?? "";
        _actionArguments = existing?.ActionArguments ?? "";
        _enabled = existing?.Enabled ?? true;
        _requiresConfirmation = existing?.RequiresConfirmation ?? false;
    }

    /// <summary>Gets a value indicating whether the dialog creates a new command rather than editing one.</summary>
    public bool IsNew { get; }

    /// <summary>Gets the name the edited command had when the dialog opened; <see langword="null"/> for a new command.</summary>
    public string? OriginalName { get; }

    /// <summary>Gets the window title.</summary>
    public string Title => IsNew ? "New voice command" : $"Edit voice command — {OriginalName}";

    /// <summary>Gets the action types offered in the dropdown.</summary>
    public IReadOnlyList<string> ActionTypes { get; }

    /// <summary>Gets the validated command after a successful Save; <see langword="null"/> until then and on Cancel.</summary>
    public CommandDefinition? Result { get; private set; }

    /// <summary>Raised when the window hosting this view model should close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Gets or sets the command name (also the JSON file name).</summary>
    [ObservableProperty]
    private string _name;

    /// <summary>Gets or sets the trigger phrases, one per line.</summary>
    [ObservableProperty]
    private string _phrasesText;

    /// <summary>Gets or sets the selected action type.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueHint))]
    [NotifyPropertyChangedFor(nameof(SupportsArguments))]
    private string _actionType;

    /// <summary>Gets or sets the configured action payload (executable, URL or folder).</summary>
    [ObservableProperty]
    private string _actionValue;

    /// <summary>Gets or sets the optional arguments template; only meaningful for actions that support it.</summary>
    [ObservableProperty]
    private string _actionArguments;

    /// <summary>Gets or sets a value indicating whether the command participates in matching.</summary>
    [ObservableProperty]
    private bool _enabled;

    /// <summary>Gets or sets a value indicating whether the command asks for confirmation before it runs.</summary>
    [ObservableProperty]
    private bool _requiresConfirmation;

    /// <summary>Gets or sets the validation message shown under the fields; <see langword="null"/> hides it.</summary>
    [ObservableProperty]
    private string? _validationError;

    /// <summary>Gets whether the selected action type consumes a separate arguments template.</summary>
    public bool SupportsArguments
        => string.Equals(ActionType, ProcessStartAction.RegistrationName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets a short hint describing what the "value" field should contain for the selected action.</summary>
    public string ValueHint => ActionType switch
    {
        _ when string.Equals(ActionType, ProcessStartAction.RegistrationName, StringComparison.OrdinalIgnoreCase)
            => "The executable to launch (e.g. notepad.exe or a full path). Must not contain {{Argument}}.",
        _ when string.Equals(ActionType, OpenUrlAction.RegistrationName, StringComparison.OrdinalIgnoreCase)
            => "An absolute http/https URL. May contain {{Argument}} to inject the spoken words (URL-encoded).",
        _ when string.Equals(ActionType, OpenFolderAction.RegistrationName, StringComparison.OrdinalIgnoreCase)
            => "A folder path (e.g. %USERPROFILE%\\Downloads) or a well-known name: Downloads, Documents, Desktop, Pictures.",
        _ => "The value the action operates on.",
    };

    /// <summary>Validates the fields; on success exposes <see cref="Result"/> and closes the dialog.</summary>
    [RelayCommand]
    private void Save()
    {
        if (CommandNameRules.Validate(Name) is { } nameError)
        {
            ValidationError = nameError;
            return;
        }

        var name = Name.Trim();
        if (_otherNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
        {
            ValidationError = $"A command named '{name}' already exists.";
            return;
        }

        var phrases = PhrasesText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (phrases.Count == 0)
        {
            ValidationError = "At least one trigger phrase is required (one per line).";
            return;
        }

        if (string.IsNullOrWhiteSpace(ActionType))
        {
            ValidationError = "An action type is required.";
            return;
        }

        var candidate = new CommandDefinition
        {
            Name = name,
            Enabled = Enabled,
            Phrases = phrases,
            ActionType = ActionType,
            ActionValue = ActionValue.Trim(),
            ActionArguments = SupportsArguments ? ActionArguments.Trim() : "",
            RequiresConfirmation = RequiresConfirmation,
        };

        // Run the action type's own configuration rules so the user gets an action-specific message
        // (e.g. "OpenUrl 'value' must be an absolute http or https URL") instead of a silent skip.
        if (!_actionResolver.TryResolve(ActionType, out var action))
        {
            ValidationError = $"Unknown action type '{ActionType}'.";
            return;
        }

        if (action is ICommandActionValidator validator && validator.Validate(candidate) is { } actionError)
        {
            ValidationError = actionError;
            return;
        }

        Result = candidate;
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
