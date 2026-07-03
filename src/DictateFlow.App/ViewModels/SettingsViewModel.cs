using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Prompts;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Core.Services.Transcription;
using DictateFlow.Providers.AzureFoundry;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.ViewModels;

/// <summary>A microphone choice in the settings dropdown; a <see langword="null"/> id means the system default.</summary>
/// <param name="DeviceId">Persisted device identifier, or <see langword="null"/> for the system default.</param>
/// <param name="DisplayName">Name shown in the dropdown.</param>
public sealed record MicrophoneOption(string? DeviceId, string DisplayName);

/// <summary>One editable row on the Rules settings page.</summary>
public partial class ApplicationRuleItem : ObservableObject
{
    /// <summary>Gets or sets the foreground process name to match (without <c>.exe</c>).</summary>
    [ObservableProperty]
    private string _processName = "";

    /// <summary>Gets or sets the prompt-mode name applied when the rule matches.</summary>
    [ObservableProperty]
    private string _promptMode = "";
}

/// <summary>
/// View model backing the Settings window. Exposes the section navigation skeleton plus the
/// General/Recording page, the Speech and LLM pages (a provider dropdown fed by the provider
/// registry that switches between the AzureFoundry and Mock config sections, with test
/// connection), the Prompts page (loaded modes, active-mode selector, reload, open-folder,
/// and the Prompt Tester that runs a pasted transcript through the real resolver and the
/// selected LLM provider) and the Output page (registry-fed provider dropdown and
/// delivery-mode selector, read live by the pipeline on every run). Save persists through
/// <see cref="ISettingsService"/>, whose <c>SettingsChanged</c> event re-arms the hotkey
/// without a restart.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IProviderRegistry _providerRegistry;
    private readonly IProviderConfigReader _configReader;
    private readonly IPromptModeStore _promptModeStore;
    private readonly IPromptResolver _promptResolver;
    private readonly IAppPaths _appPaths;
    private readonly ILogger<SettingsViewModel> _logger;

    private bool _isPushToTalk;
    private bool _isAutomaticOutput;

    /// <summary>Initializes a new instance of the <see cref="SettingsViewModel"/> class.</summary>
    /// <param name="settingsService">Persists and reloads application settings.</param>
    /// <param name="microphoneEnumerator">Supplies the microphone dropdown entries.</param>
    /// <param name="providerRegistry">Supplies the provider dropdown names and resolves the provider under test.</param>
    /// <param name="configReader">Reads and writes the per-provider config sections.</param>
    /// <param name="promptModeStore">Supplies the prompt modes; reloaded when this window opens.</param>
    /// <param name="promptResolver">Resolves prompt variables for the Prompt Tester.</param>
    /// <param name="appPaths">Supplies the prompts folder for the "Open prompts folder" button.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public SettingsViewModel(
        ISettingsService settingsService,
        IMicrophoneEnumerator microphoneEnumerator,
        IProviderRegistry providerRegistry,
        IProviderConfigReader configReader,
        IPromptModeStore promptModeStore,
        IPromptResolver promptResolver,
        IAppPaths appPaths,
        ILogger<SettingsViewModel> logger)
    {
        _settingsService = settingsService;
        _providerRegistry = providerRegistry;
        _configReader = configReader;
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

        SpeechProviders = _providerRegistry.GetNames(ProviderKind.Transcription);
        LlmProviders = _providerRegistry.GetNames(ProviderKind.Llm);
        OutputProviders = _providerRegistry.GetNames(ProviderKind.Output);

        var activeProviders = _settingsService.Current.ActiveProviders;
        _selectedSpeechProvider = FindProvider(SpeechProviders, activeProviders.Transcription, "Speech");
        _selectedLlmProvider = FindProvider(LlmProviders, activeProviders.Llm, "LLM");
        _selectedOutputProvider = FindProvider(OutputProviders, activeProviders.Output, "Output");

        var speech = _configReader.GetConfig<AzureFoundryTranscriptionConfig>(
            ProviderKind.Transcription, AzureFoundryProviders.RegistrationName);
        _speechEndpoint = speech.Endpoint;
        _speechApiKey = speech.ApiKey;
        _speechDeploymentName = speech.DeploymentName;
        _speechLanguage = speech.Language;
        _speechTimeoutSeconds = speech.TimeoutSeconds;

        var mockSpeech = _configReader.GetConfig<MockTranscriptionConfig>(
            ProviderKind.Transcription, MockTranscriptionProvider.RegistrationName);
        _mockSpeechDelayMs = mockSpeech.DelayMs;
        _mockSpeechText = mockSpeech.Text;

        var llm = _configReader.GetConfig<AzureFoundryLlmConfig>(
            ProviderKind.Llm, AzureFoundryProviders.RegistrationName);
        _llmEndpoint = llm.Endpoint;
        _llmApiKey = llm.ApiKey;
        _llmDeploymentName = llm.DeploymentName;
        _llmTemperature = llm.Temperature;
        _llmMaxTokens = llm.MaxTokens;
        _llmTimeoutSeconds = llm.TimeoutSeconds;

        var mockLlm = _configReader.GetConfig<MockLlmConfig>(
            ProviderKind.Llm, MockLLMProvider.RegistrationName);
        _mockLlmDelayMs = mockLlm.DelayMs;

        // Pick up files the user edited or added since the last load.
        _promptModeStore.Reload();
        _promptModes = _promptModeStore.GetAll();
        _selectedPromptMode = FindMode(_settingsService.Current.ActivePromptMode);
        _testerMode = _selectedPromptMode;

        _isAutomaticOutput = !string.Equals(
            _settingsService.Current.Output.Mode, OutputModes.Preview, StringComparison.OrdinalIgnoreCase);

        var history = _settingsService.Current.History;
        _historyEnabled = history.Enabled;
        _historyMaxEntries = history.MaxEntries;

        DictionaryTerms = [.. _settingsService.Current.TechnicalDictionary];
        ApplicationRules = [.. _settingsService.Current.ApplicationRules
            .Select(r => new ApplicationRuleItem { ProcessName = r.ProcessName, PromptMode = r.PromptMode })];

        var pricing = _settingsService.Current.Pricing;
        _pricingSpeechPerMinute = pricing.SpeechPerMinute;
        _pricingLlmPromptPer1M = pricing.LlmPromptPer1M;
        _pricingLlmCompletionPer1M = pricing.LlmCompletionPer1M;
        _pricingCurrency = pricing.Currency;

        _selectedLogLevel = LogLevels.FirstOrDefault(
            l => string.Equals(l, _settingsService.Current.Logging.MinimumLevel, StringComparison.OrdinalIgnoreCase))
            ?? "Information";
    }

    /// <summary>Gets the navigation sections shown on the left side of the window.</summary>
    public IReadOnlyList<string> Sections { get; } =
        ["General", "Speech", "LLM", "Prompts", "Dictionary", "Rules", "Output", "History", "Pricing"];

    /// <summary>Gets the selectable minimum log levels (Serilog level names).</summary>
    public IReadOnlyList<string> LogLevels { get; } =
        ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];

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

    /// <summary>Gets the registered transcription provider names offered by the Speech dropdown.</summary>
    public IReadOnlyList<string> SpeechProviders { get; }

    /// <summary>Gets or sets the transcription provider persisted as <c>ActiveProviders.Transcription</c> on Save.</summary>
    [ObservableProperty]
    private string _selectedSpeechProvider;

    /// <summary>Gets or sets the mock transcription delay in milliseconds.</summary>
    [ObservableProperty]
    private int _mockSpeechDelayMs;

    /// <summary>Gets or sets the canned text the mock transcription provider returns.</summary>
    [ObservableProperty]
    private string _mockSpeechText;

    /// <summary>Gets or sets the speech service endpoint URL.</summary>
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

    /// <summary>Gets the registered LLM provider names offered by the LLM dropdown.</summary>
    public IReadOnlyList<string> LlmProviders { get; }

    /// <summary>Gets or sets the LLM provider persisted as <c>ActiveProviders.Llm</c> on Save.</summary>
    [ObservableProperty]
    private string _selectedLlmProvider;

    /// <summary>Gets or sets the mock LLM delay in milliseconds.</summary>
    [ObservableProperty]
    private int _mockLlmDelayMs;

    /// <summary>Gets or sets the LLM service endpoint URL.</summary>
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
    [NotifyPropertyChangedFor(nameof(PromptModeNames))]
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

    /// <summary>Gets or sets a value indicating whether dictation history is persisted.</summary>
    [ObservableProperty]
    private bool _historyEnabled;

    /// <summary>Gets or sets the history entry cap (0 keeps everything).</summary>
    [ObservableProperty]
    private int _historyMaxEntries;

    /// <summary>Gets the editable technical dictionary terms.</summary>
    public ObservableCollection<string> DictionaryTerms { get; }

    /// <summary>Gets or sets the term typed into the dictionary add box.</summary>
    [ObservableProperty]
    private string _newDictionaryTerm = "";

    /// <summary>Gets the editable application rules.</summary>
    public ObservableCollection<ApplicationRuleItem> ApplicationRules { get; }

    /// <summary>Gets the prompt-mode names offered by the rules dropdown.</summary>
    public IReadOnlyList<string> PromptModeNames => [.. PromptModes.Select(m => m.Name)];

    /// <summary>Gets or sets the speech price per minute of audio.</summary>
    [ObservableProperty]
    private double _pricingSpeechPerMinute;

    /// <summary>Gets or sets the LLM price per one million prompt tokens.</summary>
    [ObservableProperty]
    private double _pricingLlmPromptPer1M;

    /// <summary>Gets or sets the LLM price per one million completion tokens.</summary>
    [ObservableProperty]
    private double _pricingLlmCompletionPer1M;

    /// <summary>Gets or sets the display currency code.</summary>
    [ObservableProperty]
    private string _pricingCurrency;

    /// <summary>Gets or sets the minimum log level persisted to <c>Logging.MinimumLevel</c> (applied after a restart).</summary>
    [ObservableProperty]
    private string _selectedLogLevel;

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

    /// <summary>Gets the registered output provider names offered by the Output dropdown.</summary>
    public IReadOnlyList<string> OutputProviders { get; }

    /// <summary>Gets or sets the output provider persisted as <c>ActiveProviders.Output</c> on Save.</summary>
    [ObservableProperty]
    private string _selectedOutputProvider;

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

        if (MockSpeechDelayMs < 0 || MockLlmDelayMs < 0)
        {
            ValidationError = "Mock delays cannot be negative.";
            return;
        }

        if (HistoryMaxEntries < 0)
        {
            ValidationError = "History max entries cannot be negative (0 keeps everything).";
            return;
        }

        if (PricingSpeechPerMinute < 0 || PricingLlmPromptPer1M < 0 || PricingLlmCompletionPer1M < 0)
        {
            ValidationError = "Pricing rates cannot be negative.";
            return;
        }

        ValidationError = null;

        var recording = _settingsService.Current.Recording;
        recording.Mode = IsPushToTalk ? RecordingModes.PushToTalk : RecordingModes.Toggle;
        recording.Hotkey = Hotkey;
        recording.MicrophoneDeviceId = SelectedMicrophone.DeviceId;
        recording.SilenceTimeoutSeconds = SilenceTimeoutSeconds;

        ApplySpeechConfigs();
        ApplyLlmConfigs();

        var activeProviders = _settingsService.Current.ActiveProviders;
        activeProviders.Transcription = SelectedSpeechProvider;
        activeProviders.Llm = SelectedLlmProvider;
        activeProviders.Output = SelectedOutputProvider;

        _settingsService.Current.ActivePromptMode = SelectedPromptMode?.Name ?? DefaultPromptModes.RawModeName;

        _settingsService.Current.Output.Mode = IsAutomaticOutput ? OutputModes.Automatic : OutputModes.Preview;

        var history = _settingsService.Current.History;
        history.Enabled = HistoryEnabled;
        history.MaxEntries = HistoryMaxEntries;

        _settingsService.Current.TechnicalDictionary = [.. DictionaryTerms];
        _settingsService.Current.ApplicationRules = [.. ApplicationRules
            .Where(r => !string.IsNullOrWhiteSpace(r.ProcessName))
            .Select(r => new ApplicationRule { ProcessName = r.ProcessName.Trim(), PromptMode = r.PromptMode })];

        var pricing = _settingsService.Current.Pricing;
        pricing.SpeechPerMinute = PricingSpeechPerMinute;
        pricing.LlmPromptPer1M = PricingLlmPromptPer1M;
        pricing.LlmCompletionPer1M = PricingLlmCompletionPer1M;
        pricing.Currency = PricingCurrency.Trim();

        _settingsService.Current.Logging.MinimumLevel = SelectedLogLevel;

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
    /// Sends 0.5 s of silence through the transcription provider selected in the dropdown,
    /// using the values as entered (applied to the in-memory settings, not yet saved —
    /// Cancel reloads from disk), and reports success or the provider's actionable error
    /// inline.
    /// </summary>
    [RelayCommand]
    private async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        SpeechTestResult = "Testing…";
        try
        {
            ApplySpeechConfigs();
            var provider = _providerRegistry.Resolve<ITranscriptionProvider>(
                ProviderKind.Transcription, SelectedSpeechProvider);

            using var silence = SilentWavFactory.Create(TimeSpan.FromSeconds(0.5));
            await provider.TranscribeAsync(silence, cancellationToken);

            SpeechTestResult = IsMock(SelectedSpeechProvider)
                ? "✓ Mock provider responded — select a real provider to use a speech service."
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
    /// Sends a trivial prompt through the LLM provider selected in the dropdown, using the
    /// values as entered (applied to the in-memory settings, not yet saved — Cancel reloads
    /// from disk), and reports the round-trip result inline.
    /// </summary>
    [RelayCommand]
    private async Task TestLlmConnectionAsync(CancellationToken cancellationToken)
    {
        LlmTestResult = "Testing…";
        try
        {
            ApplyLlmConfigs();
            var provider = _providerRegistry.Resolve<ILLMProvider>(ProviderKind.Llm, SelectedLlmProvider);

            var context = new PromptContext(
                "You are a connectivity check. Reply with the single word OK.",
                "ping", Temperature: 0.0, MaxTokens: 16, ModeName: "ConnectionTest");
            var stopwatch = Stopwatch.StartNew();
            var reply = await provider.ProcessAsync(context, cancellationToken);
            stopwatch.Stop();

            LlmTestResult = IsMock(SelectedLlmProvider)
                ? "✓ Mock provider responded — select a real provider to use an LLM service."
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
    /// Runs the pasted transcript through the real resolver and the LLM provider selected on
    /// the LLM page (using the values as entered) so prompts can be debugged without any
    /// recording. The resolved system prompt is exposed for the preview expander.
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
            ApplyLlmConfigs();
            var provider = _providerRegistry.Resolve<ILLMProvider>(ProviderKind.Llm, SelectedLlmProvider);

            var context = _promptResolver.Resolve(TesterTranscript, TesterMode.Name);
            TesterResolvedPrompt = context.SystemPrompt;
            TesterResult = "Running…";

            var stopwatch = Stopwatch.StartNew();
            var output = await provider.ProcessAsync(context, cancellationToken);
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

    /// <summary>
    /// Adds the typed term to the technical dictionary, ignoring blanks and case-insensitive
    /// duplicates.
    /// </summary>
    [RelayCommand]
    private void AddDictionaryTerm()
    {
        var term = NewDictionaryTerm.Trim();
        if (term.Length == 0)
        {
            return;
        }

        if (DictionaryTerms.Any(t => string.Equals(t, term, StringComparison.OrdinalIgnoreCase)))
        {
            ValidationError = $"'{term}' is already in the dictionary.";
            return;
        }

        DictionaryTerms.Add(term);
        NewDictionaryTerm = "";
        ValidationError = null;
    }

    /// <summary>Removes one term from the technical dictionary.</summary>
    /// <param name="term">The term to remove.</param>
    [RelayCommand]
    private void RemoveDictionaryTerm(string? term)
    {
        if (term is not null)
        {
            DictionaryTerms.Remove(term);
        }
    }

    /// <summary>Appends an empty application rule row for editing.</summary>
    [RelayCommand]
    private void AddApplicationRule()
        => ApplicationRules.Add(new ApplicationRuleItem
        {
            PromptMode = PromptModeNames.FirstOrDefault() ?? "",
        });

    /// <summary>Removes one application rule row.</summary>
    /// <param name="rule">The row to remove.</param>
    [RelayCommand]
    private void RemoveApplicationRule(ApplicationRuleItem? rule)
    {
        if (rule is not null)
        {
            ApplicationRules.Remove(rule);
        }
    }

    /// <summary>
    /// Writes the edited Speech provider configs into the in-memory settings (persisted by
    /// Save; Cancel reloads from disk).
    /// </summary>
    private void ApplySpeechConfigs()
    {
        _configReader.SetConfig(ProviderKind.Transcription, AzureFoundryProviders.RegistrationName,
            new AzureFoundryTranscriptionConfig
            {
                Endpoint = SpeechEndpoint.Trim(),
                ApiKey = SpeechApiKey.Trim(),
                DeploymentName = SpeechDeploymentName.Trim(),
                Language = SpeechLanguage.Trim(),
                TimeoutSeconds = SpeechTimeoutSeconds,
            });
        _configReader.SetConfig(ProviderKind.Transcription, MockTranscriptionProvider.RegistrationName,
            new MockTranscriptionConfig { DelayMs = MockSpeechDelayMs, Text = MockSpeechText });
    }

    /// <summary>
    /// Writes the edited LLM provider configs into the in-memory settings (persisted by
    /// Save; Cancel reloads from disk).
    /// </summary>
    private void ApplyLlmConfigs()
    {
        _configReader.SetConfig(ProviderKind.Llm, AzureFoundryProviders.RegistrationName,
            new AzureFoundryLlmConfig
            {
                Endpoint = LlmEndpoint.Trim(),
                ApiKey = LlmApiKey.Trim(),
                DeploymentName = LlmDeploymentName.Trim(),
                Temperature = LlmTemperature,
                MaxTokens = LlmMaxTokens,
                TimeoutSeconds = LlmTimeoutSeconds,
            });
        _configReader.SetConfig(ProviderKind.Llm, MockLLMProvider.RegistrationName,
            new MockLlmConfig { DelayMs = MockLlmDelayMs });
    }

    /// <summary>
    /// Picks the dropdown entry for a configured active provider name. An empty name means
    /// "first registered" (the built-in default); an unknown name surfaces an inline error
    /// and falls back to the first registered provider (Mock for speech/LLM), which Save
    /// then persists.
    /// </summary>
    private string FindProvider(IReadOnlyList<string> names, string configuredName, string pageName)
    {
        if (string.IsNullOrWhiteSpace(configuredName))
        {
            return names.FirstOrDefault() ?? "";
        }

        var match = names.FirstOrDefault(n => string.Equals(n, configuredName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            ValidationError =
                $"Unknown {pageName} provider '{configuredName}' in settings; falling back to '{names.FirstOrDefault()}'.";
            return names.FirstOrDefault() ?? "";
        }

        return match;
    }

    /// <summary>Whether a provider dropdown selection is the built-in mock.</summary>
    private static bool IsMock(string providerName)
        => string.Equals(providerName, MockTranscriptionProvider.RegistrationName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Finds a loaded mode by name (case-insensitive), defaulting to the first mode.</summary>
    private PromptMode? FindMode(string name)
        => PromptModes.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? PromptModes.FirstOrDefault();

    /// <summary>Empty endpoints are allowed (provider not yet configured); anything else must be an absolute http(s) URL.</summary>
    private static bool IsValidOptionalEndpoint(string endpoint)
        => string.IsNullOrWhiteSpace(endpoint)
            || (Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));

    /// <summary>Shortens a reply for inline display.</summary>
    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : $"{text[..maxLength]}…";
}
