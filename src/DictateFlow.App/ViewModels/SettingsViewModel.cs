using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DictateFlow.App.Services;
using DictateFlow.App.Views;
using DictateFlow.Core.Models;
using DictateFlow.Core.Services;
using DictateFlow.Core.Services.Audio;
using DictateFlow.Core.Services.Diagnostics;
using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Prompts;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Core.Services.Startup;
using DictateFlow.Core.Services.Transcription;
using DictateFlow.Core.Services.Transfer;
using DictateFlow.Core.Services.Validation;
using DictateFlow.Core.Services.Models;
using DictateFlow.Providers.AzureFoundry;
using DictateFlow.Providers.WhisperCpp;
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

/// <summary>One row on the Local Models settings page: a downloadable engine or model.</summary>
public partial class LocalModelItem : ObservableObject
{
    /// <summary>Initializes a row for one catalog component.</summary>
    /// <param name="definition">The component the row manages.</param>
    public LocalModelItem(ModelDefinition definition)
    {
        Definition = definition;
    }

    /// <summary>Gets the catalog definition behind this row.</summary>
    public ModelDefinition Definition { get; }

    /// <summary>Gets the name shown for the row.</summary>
    public string DisplayName => Definition.DisplayName;

    /// <summary>Gets the human-readable download size.</summary>
    public string SizeText => $"{Definition.SizeBytes / (1024.0 * 1024.0):F0} MB";

    /// <summary>Gets or sets a value indicating whether the component is installed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isInstalled;

    /// <summary>Gets or sets a value indicating whether a download or verification is running.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Gets or sets the download progress in percent (0–100).</summary>
    [ObservableProperty]
    private double _progressPercent;

    /// <summary>Gets or sets the progress detail line, e.g. <c>452 MB / 720 MB</c>.</summary>
    [ObservableProperty]
    private string? _progressText;

    /// <summary>Gets or sets the inline result of the last action on this row, if any.</summary>
    [ObservableProperty]
    private string? _actionResult;

    /// <summary>Gets the install-state line shown next to the name.</summary>
    public string StatusText => IsInstalled ? "✓ Installed" : $"⬇ Not installed ({SizeText})";

    /// <summary>Cancels the in-flight download of this row, when one is running.</summary>
    public CancellationTokenSource? DownloadCancellation { get; set; }
}

/// <summary>
/// View model backing the Settings window. Exposes the section navigation skeleton plus the
/// General/Recording page, the Speech and LLM pages (a provider dropdown fed by the provider
/// registry that switches between the AzureFoundry and Mock config sections, with test
/// connection), the Prompts page (loaded modes with in-app create/edit/delete, active-mode
/// selector, reload, open-folder,
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
    private readonly ISettingsValidator _validator;
    private readonly IStartupRegistration _startupRegistration;
    private readonly ISettingsTransfer _settingsTransfer;
    private readonly IPromptsArchive _promptsArchive;
    private readonly IDiagnosticsService _diagnosticsService;
    private readonly IDialogService _dialogService;
    private readonly WhisperCppModelManager _whisperModelManager;
    private readonly ILogger<SettingsViewModel> _logger;

    private bool _isAutomaticOutput;

    /// <summary>The warning text already shown once; pressing Save again with the same warnings closes the window.</summary>
    private string? _acknowledgedWarnings;

    /// <summary>Initializes a new instance of the <see cref="SettingsViewModel"/> class.</summary>
    /// <param name="settingsService">Persists and reloads application settings.</param>
    /// <param name="microphoneEnumerator">Supplies the microphone dropdown entries.</param>
    /// <param name="providerRegistry">Supplies the provider dropdown names and resolves the provider under test.</param>
    /// <param name="configReader">Reads and writes the per-provider config sections.</param>
    /// <param name="promptModeStore">Supplies the prompt modes; reloaded when this window opens.</param>
    /// <param name="promptResolver">Resolves prompt variables for the Prompt Tester.</param>
    /// <param name="appPaths">Supplies the data folders shown on the Diagnostics page.</param>
    /// <param name="validator">Validates the edited settings on Save and on import.</param>
    /// <param name="startupRegistration">Creates/removes the launch-with-Windows Run entry.</param>
    /// <param name="settingsTransfer">Exports and parses settings files.</param>
    /// <param name="promptsArchive">Exports and imports the prompts folder as a zip.</param>
    /// <param name="diagnosticsService">Supplies versions, the log tail and the copyable report.</param>
    /// <param name="dialogService">File pickers and confirmation prompts.</param>
    /// <param name="whisperModelManager">Manages the local whisper.cpp engine and models (Local Models page).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public SettingsViewModel(
        ISettingsService settingsService,
        IMicrophoneEnumerator microphoneEnumerator,
        IProviderRegistry providerRegistry,
        IProviderConfigReader configReader,
        IPromptModeStore promptModeStore,
        IPromptResolver promptResolver,
        IAppPaths appPaths,
        ISettingsValidator validator,
        IStartupRegistration startupRegistration,
        ISettingsTransfer settingsTransfer,
        IPromptsArchive promptsArchive,
        IDiagnosticsService diagnosticsService,
        IDialogService dialogService,
        WhisperCppModelManager whisperModelManager,
        ILogger<SettingsViewModel> logger)
    {
        _settingsService = settingsService;
        _providerRegistry = providerRegistry;
        _configReader = configReader;
        _promptModeStore = promptModeStore;
        _promptResolver = promptResolver;
        _appPaths = appPaths;
        _validator = validator;
        _startupRegistration = startupRegistration;
        _settingsTransfer = settingsTransfer;
        _promptsArchive = promptsArchive;
        _diagnosticsService = diagnosticsService;
        _dialogService = dialogService;
        _whisperModelManager = whisperModelManager;
        _logger = logger;

        var options = new List<MicrophoneOption> { new(null, "System default") };
        options.AddRange(microphoneEnumerator.GetMicrophones().Select(m => new MicrophoneOption(m.DeviceId, m.Name)));
        Microphones = options;

        var recording = _settingsService.Current.Recording;
        _pushToTalkHotkey = recording.PushToTalkHotkey;
        _toggleHotkey = recording.ToggleHotkey;
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

        var whisper = _configReader.GetConfig<WhisperCppTranscriptionConfig>(
            ProviderKind.Transcription, WhisperCppProviders.RegistrationName);
        WhisperModels = WhisperCppModelCatalog.Models;
        _selectedWhisperModel = WhisperCppModelCatalog.FindModel(whisper.Model) ?? WhisperCppModelCatalog.Small;
        _whisperLanguage = whisper.Language;
        _whisperTimeoutSeconds = whisper.TimeoutSeconds;

        LocalModels = [.. WhisperCppModelCatalog.All.Select(d => new LocalModelItem(d))];
        RefreshLocalModelState();

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

        _launchAtStartup = _settingsService.Current.General.LaunchAtStartup;
    }

    /// <summary>Gets the navigation sections shown on the left side of the window.</summary>
    public IReadOnlyList<string> Sections { get; } =
        ["General", "Speech", "Local Models", "LLM", "Prompts", "Dictionary", "Rules", "Output", "History", "Pricing", "Backup", "Diagnostics"];

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

    /// <summary>Gets or sets the push-to-talk hotkey in <c>"Ctrl+Alt+D"</c> format; empty disables it.</summary>
    [ObservableProperty]
    private string _pushToTalkHotkey;

    /// <summary>Gets or sets the toggle hotkey in <c>"Ctrl+Alt+D"</c> format; empty disables it.</summary>
    [ObservableProperty]
    private string _toggleHotkey;

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

    /// <summary>Gets the whisper models offered by the local model dropdown, in recommendation order.</summary>
    public IReadOnlyList<ModelDefinition> WhisperModels { get; }

    /// <summary>Gets or sets the whisper model transcriptions run with (persisted as the WhisperCpp config's model id).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WhisperStatusText))]
    private ModelDefinition? _selectedWhisperModel;

    /// <summary>Gets or sets the whisper spoken language as a BCP-47 tag; empty auto-detects.</summary>
    [ObservableProperty]
    private string _whisperLanguage;

    /// <summary>Gets or sets the local transcription timeout in seconds.</summary>
    [ObservableProperty]
    private int _whisperTimeoutSeconds;

    /// <summary>Gets the rows of the Local Models page, engine first.</summary>
    public ObservableCollection<LocalModelItem> LocalModels { get; }

    /// <summary>Gets the installed-engine line shown on the Local Models page.</summary>
    public string WhisperEngineStatus
        => _whisperModelManager.GetInstalledEngineVersion() is { } version
            ? $"✓ Whisper.cpp {version}"
            : "⬇ Whisper.cpp engine not installed";

    /// <summary>Gets the install-state summary for the Speech page's WhisperCpp section.</summary>
    public string WhisperStatusText
    {
        get
        {
            var engineInstalled = _whisperModelManager.IsInstalled(WhisperCppModelCatalog.Engine);
            var modelInstalled = SelectedWhisperModel is { } model && _whisperModelManager.IsInstalled(model);
            return engineInstalled && modelInstalled
                ? $"✓ Whisper.cpp {_whisperModelManager.GetInstalledEngineVersion()} and {SelectedWhisperModel!.DisplayName} are installed — transcription runs fully offline."
                : "⬇ Local transcription is not installed yet. Download the engine and model on the Local Models page.";
        }
    }

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

    /// <summary>Gets or sets a value indicating whether DictateFlow starts with Windows.</summary>
    [ObservableProperty]
    private bool _launchAtStartup;

    /// <summary>Gets or sets the status line of the last Backup-page action, if any.</summary>
    [ObservableProperty]
    private string? _backupStatus;

    /// <summary>Gets or sets the log tail shown on the Diagnostics page.</summary>
    [ObservableProperty]
    private string? _logTail;

    /// <summary>Gets or sets the status line of the last Diagnostics-page action, if any.</summary>
    [ObservableProperty]
    private string? _diagnosticsStatus;

    /// <summary>Gets the application version shown on the Diagnostics page.</summary>
    public string AppVersion => _diagnosticsService.AppVersion;

    /// <summary>Gets the .NET runtime description shown on the Diagnostics page.</summary>
    public string RuntimeVersion => _diagnosticsService.RuntimeVersion;

    /// <summary>Gets the application data root (holds the settings and database files).</summary>
    public string RootDirectory => _appPaths.RootDirectory;

    /// <summary>Gets the settings file location shown on the Diagnostics page.</summary>
    public string SettingsFilePath => _appPaths.SettingsFilePath;

    /// <summary>Gets the database file location shown on the Diagnostics page.</summary>
    public string DatabaseFilePath => _appPaths.DatabaseFilePath;

    /// <summary>Gets the logs directory shown on the Diagnostics page.</summary>
    public string LogsDirectory => _appPaths.LogsDirectory;

    /// <summary>Gets the prompts directory shown on the Diagnostics page.</summary>
    public string PromptsDirectoryPath => _appPaths.PromptsDirectory;

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
    /// Applies a chord captured by the push-to-talk hotkey textbox. Called by the view's
    /// key-capture handler with raw key data; formatting goes through <see cref="HotkeyParser"/>.
    /// </summary>
    /// <param name="modifiers">The modifier keys held down.</param>
    /// <param name="virtualKey">The virtual-key code of the pressed main key.</param>
    public void CapturePushToTalkHotkey(HotkeyModifiers modifiers, uint virtualKey)
    {
        if (TryCaptureChord(modifiers, virtualKey, out var formatted))
        {
            PushToTalkHotkey = formatted;
        }
    }

    /// <summary>Applies a chord captured by the toggle hotkey textbox. See <see cref="CapturePushToTalkHotkey"/>.</summary>
    /// <param name="modifiers">The modifier keys held down.</param>
    /// <param name="virtualKey">The virtual-key code of the pressed main key.</param>
    public void CaptureToggleHotkey(HotkeyModifiers modifiers, uint virtualKey)
    {
        if (TryCaptureChord(modifiers, virtualKey, out var formatted))
        {
            ToggleHotkey = formatted;
        }
    }

    /// <summary>Clears the push-to-talk hotkey, disabling that trigger.</summary>
    [RelayCommand]
    private void ClearPushToTalkHotkey()
    {
        PushToTalkHotkey = "";
        ValidationError = null;
    }

    /// <summary>Clears the toggle hotkey, disabling that trigger.</summary>
    [RelayCommand]
    private void ClearToggleHotkey()
    {
        ToggleHotkey = "";
        ValidationError = null;
    }

    /// <summary>Formats a captured chord, or reports an invalid key via <see cref="ValidationError"/>.</summary>
    private bool TryCaptureChord(HotkeyModifiers modifiers, uint virtualKey, out string formatted)
    {
        if (HotkeyParser.TryFromVirtualKey(modifiers, virtualKey, out var chord))
        {
            formatted = chord.ToString();
            ValidationError = null;
            return true;
        }

        formatted = "";
        ValidationError = "That key cannot be used as a hotkey.";
        return false;
    }

    /// <summary>
    /// Applies the edits to the in-memory settings, validates them with the
    /// <see cref="ISettingsValidator"/>, and persists. Errors block the save and navigate to
    /// the offending page; warnings save but stay visible — pressing Save again with the
    /// same warnings closes the window.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        // The mock delay fields are not covered by the validator (provider-specific).
        if (MockSpeechDelayMs < 0 || MockLlmDelayMs < 0)
        {
            ValidationError = "Mock delays cannot be negative.";
            return;
        }

        ApplyEditsToSettings();

        var findings = _validator.Validate(_settingsService.Current);
        var errors = findings.Where(f => f.Severity == SettingsValidationSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            ValidationError = "Not saved:\n" + string.Join("\n", errors.Select(f => $"• {f.Section}: {f.Message}"));
            if (Sections.Contains(errors[0].Section))
            {
                SelectedSection = errors[0].Section;
            }

            return;
        }

        var startupProblem = !ApplyStartupRegistration();

        await _settingsService.SaveAsync(cancellationToken);
        _logger.LogInformation("Settings saved from Settings window");

        if (startupProblem)
        {
            return; // saved, but keep the window open so the registry message is seen
        }

        var warnings = findings.Where(f => f.Severity == SettingsValidationSeverity.Warning).ToList();
        if (warnings.Count > 0)
        {
            var text = "Saved with warnings:\n" + string.Join("\n", warnings.Select(f => $"• {f.Section}: {f.Message}"));
            if (!string.Equals(text, _acknowledgedWarnings, StringComparison.Ordinal))
            {
                _acknowledgedWarnings = text;
                ValidationError = text;
                return; // saved; Save again (or close) once the warnings are read
            }
        }

        ValidationError = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Brings the HKCU Run entry in line with the checkbox. Honest about failure: a denied
    /// registry write unchecks the option and reports it instead of pretending it worked.
    /// </summary>
    /// <returns><see langword="false"/> when the registry write failed.</returns>
    private bool ApplyStartupRegistration()
    {
        if (LaunchAtStartup == _startupRegistration.IsEnabled())
        {
            return true;
        }

        if (_startupRegistration.TrySetEnabled(LaunchAtStartup))
        {
            return true;
        }

        LaunchAtStartup = false;
        _settingsService.Current.General.LaunchAtStartup = false;
        ValidationError = "Could not update the Windows startup entry (registry access was denied). Launch at startup stays off.";
        return false;
    }

    /// <summary>Writes every edited field into the in-memory settings (persisted by Save; Cancel reloads from disk).</summary>
    private void ApplyEditsToSettings()
    {
        var recording = _settingsService.Current.Recording;
        recording.PushToTalkHotkey = PushToTalkHotkey;
        recording.ToggleHotkey = ToggleHotkey;
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
        _settingsService.Current.General.LaunchAtStartup = LaunchAtStartup;
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
    /// Offers to install the missing local components as soon as Local Whisper.cpp is
    /// picked in the Speech dropdown — the download itself runs on the Local Models page.
    /// </summary>
    /// <param name="value">The newly selected provider name.</param>
    partial void OnSelectedSpeechProviderChanged(string value)
    {
        if (!string.Equals(value, WhisperCppProviders.RegistrationName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        OnPropertyChanged(nameof(WhisperStatusText));
        var model = SelectedWhisperModel ?? WhisperCppModelCatalog.Small;
        var missing = LocalModels
            .Where(i => !i.IsInstalled && !i.IsBusy)
            .Where(i => i.Definition.Kind == ModelComponentKind.Engine || i.Definition.Id == model.Id)
            .ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var components = string.Join("\n", missing.Select(i => $"  • {i.DisplayName} ({i.SizeText})"));
        if (!_dialogService.Confirm(
            "Local transcription is not installed",
            $"DictateFlow needs to download:\n\n{components}\n\nWould you like to download them now?"))
        {
            return;
        }

        SelectedSection = "Local Models";
        foreach (var item in missing)
        {
            DownloadLocalModelCommand.Execute(item);
        }
    }

    /// <summary>Jumps from the Speech page to the Local Models page.</summary>
    [RelayCommand]
    private void OpenLocalModels() => SelectedSection = "Local Models";

    /// <summary>
    /// Downloads, verifies and installs one component, reporting progress on the row. The
    /// installation is effective immediately — no restart or Save is needed.
    /// </summary>
    /// <param name="item">The row to install.</param>
    [RelayCommand]
    private async Task DownloadLocalModelAsync(LocalModelItem? item)
    {
        if (item is null || item.IsBusy)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        item.DownloadCancellation = cancellation;
        item.IsBusy = true;
        item.ActionResult = null;
        item.ProgressPercent = 0;
        item.ProgressText = $"0 MB / {item.SizeText}";
        try
        {
            var progress = new Progress<double>(fraction =>
            {
                item.ProgressPercent = fraction * 100;
                item.ProgressText =
                    $"{fraction * item.Definition.SizeBytes / (1024.0 * 1024.0):F0} MB / {item.SizeText}";
            });
            await _whisperModelManager.DownloadAsync(item.Definition, progress, cancellation.Token);
            item.ActionResult = "✓ Downloaded and verified.";
        }
        catch (OperationCanceledException)
        {
            item.ActionResult = "Download cancelled.";
        }
        catch (ProviderException ex)
        {
            _logger.LogWarning(ex, "Download of {DisplayName} failed", item.DisplayName);
            item.ActionResult = $"✗ {ex.Message}";
        }
        finally
        {
            item.IsBusy = false;
            item.ProgressText = null;
            item.DownloadCancellation = null;
            RefreshLocalModelState();
        }
    }

    /// <summary>Cancels the in-flight download of one row.</summary>
    /// <param name="item">The row being downloaded.</param>
    [RelayCommand]
    private void CancelLocalModelDownload(LocalModelItem? item)
        => item?.DownloadCancellation?.Cancel();

    /// <summary>Removes an installed component after confirmation.</summary>
    /// <param name="item">The row to remove.</param>
    [RelayCommand]
    private async Task DeleteLocalModelAsync(LocalModelItem? item)
    {
        if (item is null || item.IsBusy || !item.IsInstalled)
        {
            return;
        }

        if (!_dialogService.Confirm(
            "Delete local component",
            $"Delete {item.DisplayName}? Local transcription needs it and will offer to download it again."))
        {
            return;
        }

        try
        {
            await _whisperModelManager.DeleteAsync(item.Definition);
            item.ActionResult = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Delete of {DisplayName} failed", item.DisplayName);
            item.ActionResult = $"✗ Could not delete: {ex.Message}";
        }

        RefreshLocalModelState();
    }

    /// <summary>Re-verifies an installed component against its pinned checksum.</summary>
    /// <param name="item">The row to verify.</param>
    [RelayCommand]
    private async Task VerifyLocalModelAsync(LocalModelItem? item)
    {
        if (item is null || item.IsBusy || !item.IsInstalled)
        {
            return;
        }

        item.IsBusy = true;
        item.ActionResult = "Verifying…";
        try
        {
            var ok = await _whisperModelManager.VerifyAsync(item.Definition, CancellationToken.None);
            item.ActionResult = ok
                ? "✓ Verification succeeded."
                : "✗ Verification failed — delete and download the component again.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Verification of {DisplayName} failed", item.DisplayName);
            item.ActionResult = $"✗ Could not verify: {ex.Message}";
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    /// <summary>Re-reads the install state of every row and the status summaries.</summary>
    private void RefreshLocalModelState()
    {
        foreach (var item in LocalModels)
        {
            item.IsInstalled = _whisperModelManager.IsInstalled(item.Definition);
        }

        OnPropertyChanged(nameof(WhisperEngineStatus));
        OnPropertyChanged(nameof(WhisperStatusText));
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

    /// <summary>Opens the editor dialog to create a new prompt mode.</summary>
    [RelayCommand]
    private void NewPromptMode() => ShowPromptModeEditor(null);

    /// <summary>Opens the editor dialog for an existing prompt mode.</summary>
    [RelayCommand]
    private void EditPromptMode(PromptMode mode) => ShowPromptModeEditor(mode);

    /// <summary>Deletes a prompt mode after confirmation; the last remaining mode cannot be deleted.</summary>
    [RelayCommand]
    private void DeletePromptMode(PromptMode mode)
    {
        if (PromptModes.Count <= 1)
        {
            // With no files left the store would re-seed all five defaults on the next reload.
            ValidationError = "At least one prompt mode must exist.";
            return;
        }

        var isReferenced =
            string.Equals(_settingsService.Current.ActivePromptMode, mode.Name, StringComparison.OrdinalIgnoreCase)
            || ApplicationRules.Any(r => string.Equals(r.PromptMode, mode.Name, StringComparison.OrdinalIgnoreCase));
        var message = $"Delete the prompt mode '{mode.Name}'? Its file is removed from the prompts folder."
            + (isReferenced
                ? "\n\nThis mode is active or used by an application rule; affected dictations will fall back to Raw."
                : "");
        if (!_dialogService.Confirm("Delete prompt mode", message))
        {
            return;
        }

        try
        {
            _promptModeStore.Delete(mode.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete prompt mode '{Name}'", mode.Name);
            ValidationError = $"Could not delete the mode: {ex.Message}";
            return;
        }

        ReloadPrompts();
    }

    /// <summary>
    /// Shows the editor dialog and persists its result. A rename writes the new file, removes
    /// the old one, remaps application rules to the new name, and keeps the mode selected.
    /// </summary>
    private void ShowPromptModeEditor(PromptMode? existing)
    {
        var otherNames = PromptModes
            .Where(m => existing is null || !string.Equals(m.Name, existing.Name, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Name)
            .ToList();
        var viewModel = new PromptModeEditorViewModel(existing, otherNames);
        var window = new PromptModeEditorWindow { DataContext = viewModel };
        viewModel.CloseRequested += (_, _) => window.Close();
        window.ShowDialog();

        if (viewModel.Result is not { } result)
        {
            return;
        }

        var renamedFrom = viewModel.OriginalName is { } oldName
            && !string.Equals(oldName, result.Name, StringComparison.OrdinalIgnoreCase)
                ? oldName
                : null;
        try
        {
            _promptModeStore.Save(result);
            if (renamedFrom is not null)
            {
                _promptModeStore.Delete(renamedFrom);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save prompt mode '{Name}'", result.Name);
            ValidationError = $"Could not save the mode: {ex.Message}";
            return;
        }

        if (renamedFrom is not null)
        {
            foreach (var rule in ApplicationRules.Where(
                r => string.Equals(r.PromptMode, renamedFrom, StringComparison.OrdinalIgnoreCase)))
            {
                rule.PromptMode = result.Name;
            }
        }

        var selectedWasRenamed = renamedFrom is not null
            && string.Equals(SelectedPromptMode?.Name, renamedFrom, StringComparison.OrdinalIgnoreCase);
        var testerWasRenamed = renamedFrom is not null
            && string.Equals(TesterMode?.Name, renamedFrom, StringComparison.OrdinalIgnoreCase);
        ReloadPrompts();
        if (selectedWasRenamed)
        {
            SelectedPromptMode = FindMode(result.Name);
        }

        if (testerWasRenamed)
        {
            TesterMode = FindMode(result.Name);
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
            if (!context.LlmEnabled)
            {
                TesterResolvedPrompt = "(LLM disabled for this mode — no prompt is sent.)";
                TesterResult = TesterTranscript;
                return;
            }

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
    /// Exports the current settings to a JSON file chosen by the user, asking whether API
    /// keys should be included (excluded by default — keys are exported as empty strings).
    /// </summary>
    [RelayCommand]
    private async Task ExportSettingsAsync(CancellationToken cancellationToken)
    {
        var path = _dialogService.ShowSaveFile(
            "Export settings", "JSON files|*.json|All files|*.*", "dictateflow-settings.json");
        if (path is null)
        {
            return;
        }

        var includeSecrets = _dialogService.Confirm(
            "Include secrets?",
            "Include API keys in the exported file?\n\nChoose No to export them as empty strings (recommended when sharing the file).");

        try
        {
            var json = _settingsTransfer.ExportJson(_settingsService.Current, includeSecrets);
            await File.WriteAllTextAsync(path, json, cancellationToken);
            BackupStatus = includeSecrets
                ? $"✓ Settings exported (including API keys) to {path}."
                : $"✓ Settings exported (API keys blanked) to {path}.";
            _logger.LogInformation("Settings exported to {Path} (secrets: {IncludeSecrets})", path, includeSecrets);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Settings export failed");
            BackupStatus = $"✗ Export failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Imports a settings file: parses it (running the schema migrations, so pre-M7 files
    /// work), validates it, shows the findings, and applies it after confirmation. Applying
    /// saves and raises <c>SettingsChanged</c>, so the hotkey and providers re-apply live;
    /// the window closes because its edit state no longer reflects the settings.
    /// </summary>
    [RelayCommand]
    private async Task ImportSettingsAsync(CancellationToken cancellationToken)
    {
        var path = _dialogService.ShowOpenFile("Import settings", "JSON files|*.json|All files|*.*");
        if (path is null)
        {
            return;
        }

        AppSettings imported;
        try
        {
            imported = _settingsTransfer.ParseImport(await File.ReadAllTextAsync(path, cancellationToken));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Settings import failed");
            BackupStatus = $"✗ Import failed: {ex.Message}";
            return;
        }

        var findings = _validator.Validate(imported);
        var summary = findings.Count == 0
            ? "The file is valid."
            : "The file has findings:\n" + string.Join("\n", findings.Select(
                f => $"• [{(f.Severity == SettingsValidationSeverity.Error ? "Error" : "Warning")}] {f.Section}: {f.Message}"));

        if (!_dialogService.Confirm(
            "Import settings",
            $"{summary}\n\nReplace the current settings with this file? The window will close and the new settings apply immediately."))
        {
            BackupStatus = "Import cancelled.";
            return;
        }

        await _settingsService.ReplaceAsync(imported, cancellationToken);
        _logger.LogInformation("Settings imported from {Path} with {FindingCount} validation findings", path, findings.Count);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Exports the prompts folder as a zip archive.</summary>
    [RelayCommand]
    private async Task ExportPromptsAsync(CancellationToken cancellationToken)
    {
        var path = _dialogService.ShowSaveFile(
            "Export prompts", "Zip archives|*.zip|All files|*.*", "dictateflow-prompts.zip");
        if (path is null)
        {
            return;
        }

        try
        {
            var count = await Task.Run(() => _promptsArchive.ExportZip(path), cancellationToken);
            BackupStatus = $"✓ Exported {count} prompt file(s) to {path}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Prompts export failed");
            BackupStatus = $"✗ Export failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Imports a prompts zip into the prompts folder, confirming before existing files are
    /// overwritten (Yes overwrites them all, No imports only new files), then reloads the
    /// prompt modes.
    /// </summary>
    [RelayCommand]
    private async Task ImportPromptsAsync(CancellationToken cancellationToken)
    {
        var path = _dialogService.ShowOpenFile("Import prompts", "Zip archives|*.zip|All files|*.*");
        if (path is null)
        {
            return;
        }

        try
        {
            var overwrite = false;
            var conflicts = await Task.Run(() => _promptsArchive.GetConflictingFiles(path), cancellationToken);
            if (conflicts.Count > 0)
            {
                var answer = _dialogService.ConfirmYesNoCancel(
                    "Import prompts",
                    $"{conflicts.Count} prompt file(s) already exist:\n{string.Join("\n", conflicts)}\n\n"
                    + "Overwrite them? Choose No to keep the existing files and import only new ones.");
                if (answer is null)
                {
                    BackupStatus = "Import cancelled.";
                    return;
                }

                overwrite = answer.Value;
            }

            var count = await Task.Run(() => _promptsArchive.ImportZip(path, overwrite), cancellationToken);
            ReloadPrompts();
            BackupStatus = $"✓ Imported {count} prompt file(s).";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Prompts import failed");
            BackupStatus = $"✗ Import failed: {ex.Message}";
        }
    }

    /// <summary>Opens a folder in Windows Explorer (Diagnostics page path buttons).</summary>
    /// <param name="path">The directory to open.</param>
    [RelayCommand]
    private void OpenFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open folder '{Path}'", path);
            DiagnosticsStatus = "Could not open the folder.";
        }
    }

    /// <summary>Loads the last 100 lines of the newest log file into the Diagnostics page.</summary>
    [RelayCommand]
    private async Task RefreshLogTailAsync(CancellationToken cancellationToken)
    {
        var lines = await Task.Run(() => _diagnosticsService.ReadLogTail(100), cancellationToken);
        LogTail = string.Join(Environment.NewLine, lines);
    }

    /// <summary>Copies the diagnostics report (versions, OS, redacted settings) to the clipboard.</summary>
    [RelayCommand]
    private async Task CopyDiagnosticsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var report = await Task.Run(_diagnosticsService.BuildReport, cancellationToken);
            Clipboard.SetText(report);
            DiagnosticsStatus = "✓ Diagnostics copied to the clipboard (API keys redacted).";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Copy diagnostics failed");
            DiagnosticsStatus = $"✗ Could not copy diagnostics: {ex.Message}";
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

        // Read-modify-write keeps config-only fields (e.g. Threads) that have no UI.
        var whisper = _configReader.GetConfig<WhisperCppTranscriptionConfig>(
            ProviderKind.Transcription, WhisperCppProviders.RegistrationName);
        whisper.Model = SelectedWhisperModel?.Id ?? WhisperCppModelCatalog.SmallModelId;
        whisper.Language = WhisperLanguage.Trim();
        whisper.TimeoutSeconds = WhisperTimeoutSeconds;
        _configReader.SetConfig(ProviderKind.Transcription, WhisperCppProviders.RegistrationName, whisper);
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

    /// <summary>Shortens a reply for inline display.</summary>
    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : $"{text[..maxLength]}…";
}
