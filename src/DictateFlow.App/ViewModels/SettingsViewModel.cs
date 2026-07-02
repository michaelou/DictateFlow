using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Prompts;
using DictateFlow.Core.Services.Transcription;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.ViewModels;

/// <summary>A microphone choice in the settings dropdown; a <see langword="null"/> id means the system default.</summary>
/// <param name="DeviceId">Persisted device identifier, or <see langword="null"/> for the system default.</param>
/// <param name="DisplayName">Name shown in the dropdown.</param>
public sealed record MicrophoneOption(string? DeviceId, string DisplayName);

/// <summary>
/// View model backing the Settings window. Exposes the section navigation skeleton plus the
/// General/Recording page, the Speech page, the LLM page (endpoint, key, deployment,
/// temperature, max tokens, timeout, test connection) and the Prompts page (loaded modes,
/// active-mode selector, reload, open-folder, and the Prompt Tester that runs a pasted
/// transcript through the real resolver and LLM provider) and the Output page (provider and
/// delivery-mode selectors, read live by the pipeline on every run). Save persists through
/// <see cref="ISettingsService"/>, whose <c>SettingsChanged</c> event re-arms the hotkey
/// without a restart.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ITranscriptionProvider _transcriptionProvider;
    private readonly ILLMProvider _llmProvider;
    private readonly IPromptModeStore _promptModeStore;
    private readonly IPromptResolver _promptResolver;
    private readonly IAppPaths _appPaths;
    private readonly ILogger<SettingsViewModel> _logger;

    private bool _isPushToTalk;
    private bool _isClipboardPasteOutput;
    private bool _isAutomaticOutput;

    /// <summary>Initializes a new instance of the <see cref="SettingsViewModel"/> class.</summary>
    /// <param name="settingsService">Persists and reloads application settings.</param>
    /// <param name="microphoneEnumerator">Supplies the microphone dropdown entries.</param>
    /// <param name="transcriptionProvider">Used by the Speech "Test connection" check.</param>
    /// <param name="llmProvider">Used by the LLM "Test connection" check and the Prompt Tester.</param>
    /// <param name="promptModeStore">Supplies the prompt modes; reloaded when this window opens.</param>
    /// <param name="promptResolver">Resolves prompt variables for the Prompt Tester.</param>
    /// <param name="appPaths">Supplies the prompts folder for the "Open prompts folder" button.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public SettingsViewModel(
        ISettingsService settingsService,
        IMicrophoneEnumerator microphoneEnumerator,
        ITranscriptionProvider transcriptionProvider,
        ILLMProvider llmProvider,
        IPromptModeStore promptModeStore,
        IPromptResolver promptResolver,
        IAppPaths appPaths,
        ILogger<SettingsViewModel> logger)
    {
        _settingsService = settingsService;
        _transcriptionProvider = transcriptionProvider;
        _llmProvider = llmProvider;
        _promptModeStore = promptModeStore;
        _promptResolver = promptResolver;
        _appPaths = appPaths;
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

        var llm = _settingsService.Current.Llm;
        _llmEndpoint = llm.Endpoint;
        _llmApiKey = llm.ApiKey;
        _llmDeploymentName = llm.DeploymentName;
        _llmTemperature = llm.Temperature;
        _llmMaxTokens = llm.MaxTokens;
        _llmTimeoutSeconds = llm.TimeoutSeconds;

        // Pick up files the user edited or added since the last load.
        _promptModeStore.Reload();
        _promptModes = _promptModeStore.GetAll();
        _selectedPromptMode = FindMode(_settingsService.Current.ActivePromptMode);
        _testerMode = _selectedPromptMode;

        var output = _settingsService.Current.Output;
        _isClipboardPasteOutput = !string.Equals(
            output.Provider, OutputProviderNames.SimulatedKeyboard, StringComparison.OrdinalIgnoreCase);
        _isAutomaticOutput = !string.Equals(
            output.Mode, OutputModes.Preview, StringComparison.OrdinalIgnoreCase);
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

    /// <summary>Gets or sets the validation message shown under the settings controls, if any.</summary>
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

    /// <summary>Gets or sets the inline result of the last Speech "Test connection" run, if any.</summary>
    [ObservableProperty]
    private string? _speechTestResult;

    /// <summary>Gets or sets the LLM service endpoint URL; empty selects the mock provider.</summary>
    [ObservableProperty]
    private string _llmEndpoint;

    /// <summary>Gets or sets the LLM service API key.</summary>
    [ObservableProperty]
    private string _llmApiKey;

    /// <summary>Gets or sets the LLM model deployment name.</summary>
    [ObservableProperty]
    private string _llmDeploymentName;

    /// <summary>Gets or sets the default sampling temperature (0–2); modes can override it.</summary>
    [ObservableProperty]
    private double _llmTemperature;

    /// <summary>Gets or sets the maximum number of completion tokens per request.</summary>
    [ObservableProperty]
    private int _llmMaxTokens;

    /// <summary>Gets or sets the LLM request timeout in seconds.</summary>
    [ObservableProperty]
    private int _llmTimeoutSeconds;

    /// <summary>Gets or sets the inline result of the last LLM "Test connection" run, if any.</summary>
    [ObservableProperty]
    private string? _llmTestResult;

    /// <summary>Gets or sets the loaded prompt modes shown on the Prompts page.</summary>
    [ObservableProperty]
    private IReadOnlyList<PromptMode> _promptModes;

    /// <summary>Gets or sets the mode persisted as <c>ActivePromptMode</c> on Save.</summary>
    [ObservableProperty]
    private PromptMode? _selectedPromptMode;

    /// <summary>Gets or sets the transcript pasted into the Prompt Tester.</summary>
    [ObservableProperty]
    private string _testerTranscript = "";

    /// <summary>Gets or sets the mode the Prompt Tester runs with.</summary>
    [ObservableProperty]
    private PromptMode? _testerMode;

    /// <summary>Gets or sets the Prompt Tester output (or its error message).</summary>
    [ObservableProperty]
    private string? _testerResult;

    /// <summary>Gets or sets the resolved system prompt shown in the tester's preview expander.</summary>
    [ObservableProperty]
    private string? _testerResolvedPrompt;

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

    /// <summary>Gets or sets a value indicating whether the clipboard-paste output provider is selected.</summary>
    public bool IsClipboardPasteOutput
    {
        get => _isClipboardPasteOutput;
        set
        {
            if (SetProperty(ref _isClipboardPasteOutput, value))
            {
                OnPropertyChanged(nameof(IsSimulatedKeyboardOutput));
            }
        }
    }

    /// <summary>Gets or sets a value indicating whether the simulated-keyboard output provider is selected.</summary>
    public bool IsSimulatedKeyboardOutput
    {
        get => !_isClipboardPasteOutput;
        set => IsClipboardPasteOutput = !value;
    }

    /// <summary>Gets or sets a value indicating whether text is delivered automatically, without a preview.</summary>
    public bool IsAutomaticOutput
    {
        get => _isAutomaticOutput;
        set
        {
            if (SetProperty(ref _isAutomaticOutput, value))
            {
                OnPropertyChanged(nameof(IsPreviewOutput));
            }
        }
    }

    /// <summary>Gets or sets a value indicating whether the preview dialog is shown before delivery.</summary>
    public bool IsPreviewOutput
    {
        get => !_isAutomaticOutput;
        set => IsAutomaticOutput = !value;
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

        if (!IsValidOptionalEndpoint(SpeechEndpoint))
        {
            ValidationError = $"'{SpeechEndpoint}' is not a valid http(s) endpoint URL.";
            return;
        }

        if (SpeechTimeoutSeconds < 1)
        {
            ValidationError = "Speech timeout must be at least 1 second.";
            return;
        }

        if (!IsValidOptionalEndpoint(LlmEndpoint))
        {
            ValidationError = $"'{LlmEndpoint}' is not a valid http(s) endpoint URL.";
            return;
        }

        if (LlmTemperature is < 0 or > 2)
        {
            ValidationError = "Temperature must be between 0 and 2.";
            return;
        }

        if (LlmMaxTokens < 1)
        {
            ValidationError = "Max tokens must be at least 1.";
            return;
        }

        if (LlmTimeoutSeconds < 1)
        {
            ValidationError = "LLM timeout must be at least 1 second.";
            return;
        }

        ValidationError = null;

        var recording = _settingsService.Current.Recording;
        recording.Mode = IsPushToTalk ? RecordingModes.PushToTalk : RecordingModes.Toggle;
        recording.Hotkey = Hotkey;
        recording.MicrophoneDeviceId = SelectedMicrophone.DeviceId;
        recording.SilenceTimeoutSeconds = SilenceTimeoutSeconds;

        ApplySpeechSettings(_settingsService.Current.Speech);
        ApplyLlmSettings(_settingsService.Current.Llm);
        _settingsService.Current.ActivePromptMode = SelectedPromptMode?.Name ?? DefaultPromptModes.RawModeName;

        var output = _settingsService.Current.Output;
        output.Provider = IsClipboardPasteOutput ? OutputProviderNames.ClipboardPaste : OutputProviderNames.SimulatedKeyboard;
        output.Mode = IsAutomaticOutput ? OutputModes.Automatic : OutputModes.Preview;

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

    /// <summary>
    /// Sends a trivial prompt through the LLM provider using the values as entered (applied
    /// to the in-memory settings, not yet saved — Cancel reloads from disk) and reports the
    /// round-trip result inline.
    /// </summary>
    [RelayCommand]
    private async Task TestLlmConnectionAsync(CancellationToken cancellationToken)
    {
        LlmTestResult = "Testing…";
        try
        {
            ApplyLlmSettings(_settingsService.Current.Llm);

            var context = new PromptContext(
                "You are a connectivity check. Reply with the single word OK.",
                "ping", Temperature: 0.0, MaxTokens: 16, ModeName: "ConnectionTest");
            var stopwatch = Stopwatch.StartNew();
            var reply = await _llmProvider.ProcessAsync(context, cancellationToken);
            stopwatch.Stop();

            LlmTestResult = string.IsNullOrWhiteSpace(LlmEndpoint)
                ? "✓ Mock provider responded — configure an endpoint to use a real LLM service."
                : $"✓ Connection succeeded in {stopwatch.ElapsedMilliseconds} ms — reply: {Truncate(reply, 80)}";
            _logger.LogInformation("LLM test connection succeeded");
        }
        catch (OperationCanceledException)
        {
            LlmTestResult = null;
        }
        catch (ProviderException ex)
        {
            _logger.LogWarning(ex, "LLM test connection failed");
            LlmTestResult = $"✗ {ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM test connection failed unexpectedly");
            LlmTestResult = $"✗ {ex.Message}";
        }
    }

    /// <summary>Re-scans the prompts directory and refreshes the mode lists, keeping selections by name.</summary>
    [RelayCommand]
    private void ReloadPrompts()
    {
        var selected = SelectedPromptMode?.Name;
        var testerSelected = TesterMode?.Name;

        _promptModeStore.Reload();
        PromptModes = _promptModeStore.GetAll();
        SelectedPromptMode = FindMode(selected ?? _settingsService.Current.ActivePromptMode);
        TesterMode = FindMode(testerSelected ?? "") ?? SelectedPromptMode;
        _logger.LogInformation("Prompt modes reloaded: {Count} available", PromptModes.Count);
    }

    /// <summary>Opens the prompts directory in Windows Explorer.</summary>
    [RelayCommand]
    private void OpenPromptsFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_appPaths.PromptsDirectory}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open the prompts folder");
            ValidationError = "Could not open the prompts folder.";
        }
    }

    /// <summary>
    /// Runs the pasted transcript through the real resolver and LLM provider (using the LLM
    /// values as entered) so prompts can be debugged without any recording. The resolved
    /// system prompt is exposed for the preview expander.
    /// </summary>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RunTesterAsync(CancellationToken cancellationToken)
    {
        if (TesterMode is null || string.IsNullOrWhiteSpace(TesterTranscript))
        {
            TesterResult = "Paste a transcript and pick a mode first.";
            return;
        }

        try
        {
            ApplyLlmSettings(_settingsService.Current.Llm);

            var context = _promptResolver.Resolve(TesterTranscript, TesterMode.Name);
            TesterResolvedPrompt = context.SystemPrompt;
            TesterResult = "Running…";

            var stopwatch = Stopwatch.StartNew();
            var output = await _llmProvider.ProcessAsync(context, cancellationToken);
            stopwatch.Stop();

            TesterResult = output;
            _logger.LogInformation(
                "Prompt tester run completed with mode '{ModeName}' in {ElapsedMs} ms",
                context.ModeName, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            TesterResult = "Cancelled.";
        }
        catch (ProviderException ex)
        {
            _logger.LogWarning(ex, "Prompt tester run failed");
            TesterResult = $"✗ {ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Prompt tester run failed unexpectedly");
            TesterResult = $"✗ {ex.Message}";
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

    /// <summary>Copies the edited LLM values into <paramref name="llm"/>.</summary>
    private void ApplyLlmSettings(LlmSettings llm)
    {
        llm.Endpoint = LlmEndpoint.Trim();
        llm.ApiKey = LlmApiKey.Trim();
        llm.DeploymentName = LlmDeploymentName.Trim();
        llm.Temperature = LlmTemperature;
        llm.MaxTokens = LlmMaxTokens;
        llm.TimeoutSeconds = LlmTimeoutSeconds;
    }

    /// <summary>Finds a loaded mode by name (case-insensitive), defaulting to the first mode.</summary>
    private PromptMode? FindMode(string name)
        => PromptModes.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? PromptModes.FirstOrDefault();

    /// <summary>Empty endpoints are allowed (mock provider); anything else must be an absolute http(s) URL.</summary>
    private static bool IsValidOptionalEndpoint(string endpoint)
        => string.IsNullOrWhiteSpace(endpoint)
            || (Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));

    /// <summary>Shortens a reply for inline display.</summary>
    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : $"{text[..maxLength]}…";
}
