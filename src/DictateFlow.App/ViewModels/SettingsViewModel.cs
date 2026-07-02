using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.Transcription;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.ViewModels;

/// <summary>A microphone choice in the settings dropdown; a <see langword="null"/> id means the system default.</summary>
/// <param name="DeviceId">Persisted device identifier, or <see langword="null"/> for the system default.</param>
/// <param name="DisplayName">Name shown in the dropdown.</param>
public sealed record MicrophoneOption(string? DeviceId, string DisplayName);

/// <summary>
/// View model backing the Settings window. Exposes the section navigation skeleton plus the
/// General/Recording page (mode, hotkey, microphone, silence timeout) and the Speech page
/// (endpoint, key, deployment, language, timeout, test connection); the remaining section
/// pages are filled in by later milestones. Save persists through <see cref="ISettingsService"/>,
/// whose <c>SettingsChanged</c> event re-arms the hotkey without a restart.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ITranscriptionProvider _transcriptionProvider;
    private readonly ILogger<SettingsViewModel> _logger;

    private bool _isPushToTalk;

    /// <summary>Initializes a new instance of the <see cref="SettingsViewModel"/> class.</summary>
    /// <param name="settingsService">Persists and reloads application settings.</param>
    /// <param name="microphoneEnumerator">Supplies the microphone dropdown entries.</param>
    /// <param name="transcriptionProvider">Used by the Speech "Test connection" check.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public SettingsViewModel(
        ISettingsService settingsService,
        IMicrophoneEnumerator microphoneEnumerator,
        ITranscriptionProvider transcriptionProvider,
        ILogger<SettingsViewModel> logger)
    {
        _settingsService = settingsService;
        _transcriptionProvider = transcriptionProvider;
        _logger = logger;

        var options = new List<MicrophoneOption> { new(null, "System default") };
        options.AddRange(microphoneEnumerator.GetMicrophones().Select(m => new MicrophoneOption(m.DeviceId, m.Name)));
        Microphones = options;

        var recording = _settingsService.Current.Recording;
        _isPushToTalk = !string.Equals(recording.Mode, RecordingModes.Toggle, StringComparison.OrdinalIgnoreCase);
        _hotkey = recording.Hotkey;
        _silenceTimeoutSeconds = recording.SilenceTimeoutSeconds;
        _selectedMicrophone = options.FirstOrDefault(o => o.DeviceId == recording.MicrophoneDeviceId) ?? options[0];

        var speech = _settingsService.Current.Speech;
        _speechEndpoint = speech.Endpoint;
        _speechApiKey = speech.ApiKey;
        _speechDeploymentName = speech.DeploymentName;
        _speechLanguage = speech.Language;
        _speechTimeoutSeconds = speech.TimeoutSeconds;
    }

    /// <summary>Gets the navigation sections shown on the left side of the window.</summary>
    public IReadOnlyList<string> Sections { get; } =
        ["General", "Speech", "LLM", "Prompts", "Output", "History"];

    /// <summary>Gets or sets the currently selected navigation section.</summary>
    [ObservableProperty]
    private string _selectedSection = "General";

    /// <summary>Gets the microphone choices, headed by the system-default entry.</summary>
    public IReadOnlyList<MicrophoneOption> Microphones { get; }

    /// <summary>Gets or sets the selected microphone.</summary>
    [ObservableProperty]
    private MicrophoneOption _selectedMicrophone;

    /// <summary>Gets or sets the hotkey in <c>"Ctrl+Alt+D"</c> format.</summary>
    [ObservableProperty]
    private string _hotkey;

    /// <summary>Gets or sets the validation message shown under the recording controls, if any.</summary>
    [ObservableProperty]
    private string? _validationError;

    /// <summary>Gets or sets the silence auto-stop timeout in seconds.</summary>
    [ObservableProperty]
    private int _silenceTimeoutSeconds;

    /// <summary>Gets or sets the speech service endpoint URL; empty selects the mock provider.</summary>
    [ObservableProperty]
    private string _speechEndpoint;

    /// <summary>Gets or sets the speech service API key.</summary>
    [ObservableProperty]
    private string _speechApiKey;

    /// <summary>Gets or sets the speech model deployment name.</summary>
    [ObservableProperty]
    private string _speechDeploymentName;

    /// <summary>Gets or sets the spoken language as a BCP-47 tag.</summary>
    [ObservableProperty]
    private string _speechLanguage;

    /// <summary>Gets or sets the speech request timeout in seconds.</summary>
    [ObservableProperty]
    private int _speechTimeoutSeconds;

    /// <summary>Gets or sets the inline result of the last "Test connection" run, if any.</summary>
    [ObservableProperty]
    private string? _speechTestResult;

    /// <summary>Gets or sets a value indicating whether push-to-talk mode is selected.</summary>
    public bool IsPushToTalk
    {
        get => _isPushToTalk;
        set
        {
            if (SetProperty(ref _isPushToTalk, value))
            {
                OnPropertyChanged(nameof(IsToggle));
            }
        }
    }

    /// <summary>Gets or sets a value indicating whether toggle mode is selected.</summary>
    public bool IsToggle
    {
        get => !_isPushToTalk;
        set => IsPushToTalk = !value;
    }

    /// <summary>Raised when the window hosting this view model should close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Applies a hotkey chord captured by the hotkey textbox. Called by the view's
    /// key-capture handler with raw key data; formatting goes through <see cref="HotkeyParser"/>.
    /// </summary>
    /// <param name="modifiers">The modifier keys held down.</param>
    /// <param name="virtualKey">The virtual-key code of the pressed main key.</param>
    public void CaptureHotkey(HotkeyModifiers modifiers, uint virtualKey)
    {
        if (HotkeyParser.TryFromVirtualKey(modifiers, virtualKey, out var chord))
        {
            Hotkey = chord.ToString();
            ValidationError = null;
        }
        else
        {
            ValidationError = "That key cannot be used as a hotkey.";
        }
    }

    /// <summary>Validates and persists the current settings, then closes the window.</summary>
    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (!HotkeyParser.TryParse(Hotkey, out _))
        {
            ValidationError = $"'{Hotkey}' is not a valid hotkey (expected e.g. Ctrl+Alt+D).";
            return;
        }

        if (SilenceTimeoutSeconds < 1)
        {
            ValidationError = "Silence timeout must be at least 1 second.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(SpeechEndpoint)
            && (!Uri.TryCreate(SpeechEndpoint.Trim(), UriKind.Absolute, out var endpoint)
                || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)))
        {
            ValidationError = $"'{SpeechEndpoint}' is not a valid http(s) endpoint URL.";
            return;
        }

        if (SpeechTimeoutSeconds < 1)
        {
            ValidationError = "Speech timeout must be at least 1 second.";
            return;
        }

        ValidationError = null;

        var recording = _settingsService.Current.Recording;
        recording.Mode = IsPushToTalk ? RecordingModes.PushToTalk : RecordingModes.Toggle;
        recording.Hotkey = Hotkey;
        recording.MicrophoneDeviceId = SelectedMicrophone.DeviceId;
        recording.SilenceTimeoutSeconds = SilenceTimeoutSeconds;

        ApplySpeechSettings(_settingsService.Current.Speech);

        await _settingsService.SaveAsync(cancellationToken);
        _logger.LogInformation("Settings saved from Settings window");
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Discards unsaved edits by reloading settings from disk, then closes the window.</summary>
    [RelayCommand]
    private async Task CancelAsync(CancellationToken cancellationToken)
    {
        await _settingsService.LoadAsync(cancellationToken);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Sends 0.5 s of silence through the transcription provider using the values as
    /// entered (applied to the in-memory settings, not yet saved — Cancel reloads from disk)
    /// and reports success or the provider's actionable error inline.
    /// </summary>
    [RelayCommand]
    private async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        SpeechTestResult = "Testing…";
        try
        {
            ApplySpeechSettings(_settingsService.Current.Speech);

            using var silence = SilentWavFactory.Create(TimeSpan.FromSeconds(0.5));
            await _transcriptionProvider.TranscribeAsync(silence, cancellationToken);

            SpeechTestResult = string.IsNullOrWhiteSpace(SpeechEndpoint)
                ? "✓ Mock provider responded — configure an endpoint to use a real speech service."
                : "✓ Connection succeeded.";
            _logger.LogInformation("Speech test connection succeeded");
        }
        catch (OperationCanceledException)
        {
            SpeechTestResult = null;
        }
        catch (ProviderException ex)
        {
            _logger.LogWarning(ex, "Speech test connection failed");
            SpeechTestResult = $"✗ {ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Speech test connection failed unexpectedly");
            SpeechTestResult = $"✗ {ex.Message}";
        }
    }

    /// <summary>Copies the edited Speech values into <paramref name="speech"/>.</summary>
    private void ApplySpeechSettings(SpeechSettings speech)
    {
        speech.Endpoint = SpeechEndpoint.Trim();
        speech.ApiKey = SpeechApiKey.Trim();
        speech.DeploymentName = SpeechDeploymentName.Trim();
        speech.Language = SpeechLanguage.Trim();
        speech.TimeoutSeconds = SpeechTimeoutSeconds;
    }
}
