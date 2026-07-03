using System.Text.Json;
using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Providers;
using DictateFlow.Core.Services.Transcription;

namespace DictateFlow.Core.Models;

/// <summary>
/// Root application settings persisted to <c>settings.json</c> in the DictateFlow app data directory.
/// Provider configuration is fully named since M7: <see cref="ActiveProviders"/> selects one
/// provider per <see cref="ProviderKind"/> and <see cref="Providers"/> holds one config
/// subsection per registered provider. Files with the pre-M7 flat <c>Speech</c>/<c>Llm</c>
/// sections are migrated at load time (see <c>ISettingsMigration</c>).
/// </summary>
public sealed class AppSettings
{
    /// <summary>Gets or sets the general application behavior configuration (consumed in M8).</summary>
    public GeneralSettings General { get; set; } = new();

    /// <summary>Gets or sets the audio recording configuration (consumed in M2).</summary>
    public RecordingSettings Recording { get; set; } = new();

    /// <summary>Gets or sets the names of the active providers, one per <see cref="ProviderKind"/> (consumed in M7).</summary>
    public ActiveProviderSettings ActiveProviders { get; set; } = new();

    /// <summary>Gets or sets the per-provider configuration sections (consumed in M7).</summary>
    public ProviderConfigurationSettings Providers { get; set; } = new();

    /// <summary>Gets or sets the output pipeline configuration (consumed in M5).</summary>
    public OutputSettings Output { get; set; } = new();

    /// <summary>Gets or sets the dictation history configuration (consumed in M6).</summary>
    public HistorySettings History { get; set; } = new();

    /// <summary>Gets or sets the provider pricing rates used for cost estimation (consumed in M6).</summary>
    public PricingSettings Pricing { get; set; } = new();

    /// <summary>Gets or sets the diagnostic logging configuration (consumed in M6).</summary>
    public LoggingSettings Logging { get; set; } = new();

    /// <summary>Gets or sets the name of the active prompt mode used for LLM enhancement (consumed in M4).</summary>
    public string ActivePromptMode { get; set; } = "Raw";

    /// <summary>Gets or sets user-defined technical terms that bias transcription and enhancement (consumed in M4).</summary>
    public List<string> TechnicalDictionary { get; set; } = [];

    /// <summary>
    /// Gets or sets the per-application prompt-mode rules (consumed in M6). Evaluated in order
    /// against the foreground process name captured at record-start; the first match wins and
    /// no match falls back to <see cref="ActivePromptMode"/>.
    /// </summary>
    public List<ApplicationRule> ApplicationRules { get; set; } = [];

    /// <summary>
    /// Gets or sets the remembered size and position of each application window (consumed in
    /// M8), keyed by a stable window name (e.g. <c>Settings</c>, <c>History</c>).
    /// </summary>
    public Dictionary<string, WindowPlacement> WindowState { get; set; } = [];
}

/// <summary>General application behavior settings.</summary>
public sealed class GeneralSettings
{
    /// <summary>Gets or sets a value indicating whether DictateFlow starts with Windows (HKCU Run key).</summary>
    public bool LaunchAtStartup { get; set; }

    /// <summary>Gets or sets a value indicating whether the one-time first-run welcome has been shown.</summary>
    public bool FirstRunCompleted { get; set; }
}

/// <summary>The remembered placement of one application window, in device-independent pixels.</summary>
public sealed class WindowPlacement
{
    /// <summary>Gets or sets the distance from the left edge of the virtual screen.</summary>
    public double Left { get; set; }

    /// <summary>Gets or sets the distance from the top edge of the virtual screen.</summary>
    public double Top { get; set; }

    /// <summary>Gets or sets the window width.</summary>
    public double Width { get; set; }

    /// <summary>Gets or sets the window height.</summary>
    public double Height { get; set; }
}

/// <summary>
/// One per-application prompt-mode rule: dictations started while <see cref="ProcessName"/>
/// is the foreground process use <see cref="PromptMode"/> instead of the active mode.
/// </summary>
public sealed class ApplicationRule
{
    /// <summary>Gets or sets the foreground process name to match (without <c>.exe</c>, case-insensitive, e.g. <c>OUTLOOK</c>).</summary>
    public string ProcessName { get; set; } = "";

    /// <summary>Gets or sets the name of the prompt mode selected when the rule matches.</summary>
    public string PromptMode { get; set; } = "";
}

/// <summary>Audio recording settings.</summary>
public sealed class RecordingSettings
{
    /// <summary>
    /// Gets or sets the push-to-talk hotkey: recording runs while this chord is held down.
    /// An empty value disables push-to-talk. Independent of <see cref="ToggleHotkey"/>.
    /// </summary>
    public string PushToTalkHotkey { get; set; } = "Ctrl+Alt+D";

    /// <summary>
    /// Gets or sets the toggle hotkey: pressing this chord starts recording, pressing it
    /// again stops. An empty value disables toggle. Independent of <see cref="PushToTalkHotkey"/>.
    /// </summary>
    public string ToggleHotkey { get; set; } = "";

    /// <summary>Gets or sets the identifier of the microphone capture device; <see langword="null"/> selects the system default.</summary>
    public string? MicrophoneDeviceId { get; set; }

    /// <summary>Gets or sets the number of seconds of silence after which recording stops automatically.</summary>
    public int SilenceTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets a value indicating whether transcription starts while recording is still
    /// in progress, showing the partial transcript on the overlay. Only takes effect when the
    /// active transcription provider supports streaming; otherwise the standard workflow runs.
    /// </summary>
    public bool EnableStreamingTranscription { get; set; }
}

/// <summary>
/// The name of the active provider for each <see cref="ProviderKind"/>, matching a name
/// registered through the provider registration extensions (case-insensitive). An empty
/// name selects the first registered provider of the kind.
/// </summary>
public sealed class ActiveProviderSettings
{
    /// <summary>Gets or sets the active transcription provider name.</summary>
    public string Transcription { get; set; } = MockTranscriptionProvider.RegistrationName;

    /// <summary>Gets or sets the active LLM provider name.</summary>
    public string Llm { get; set; } = MockLLMProvider.RegistrationName;

    /// <summary>Gets or sets the active output provider name; empty selects the first registered provider.</summary>
    public string Output { get; set; } = "";
}

/// <summary>
/// The per-provider configuration sections, one dictionary per <see cref="ProviderKind"/>
/// keyed by provider name. Values stay raw JSON so provider projects define their own config
/// types without Core knowing them; providers read their section through
/// <see cref="IProviderConfigReader"/>. The built-in mock sections are present by default so
/// a fresh file documents the shape.
/// </summary>
public sealed class ProviderConfigurationSettings
{
    /// <summary>Gets or sets the transcription provider sections, keyed by provider name.</summary>
    public Dictionary<string, JsonElement> Transcription { get; set; } = new()
    {
        [MockTranscriptionProvider.RegistrationName] = JsonSerializer.SerializeToElement(new MockTranscriptionConfig()),
    };

    /// <summary>Gets or sets the LLM provider sections, keyed by provider name.</summary>
    public Dictionary<string, JsonElement> Llm { get; set; } = new()
    {
        [MockLLMProvider.RegistrationName] = JsonSerializer.SerializeToElement(new MockLlmConfig()),
    };

    /// <summary>Gets or sets the output provider sections, keyed by provider name (none of the built-in output providers need one).</summary>
    public Dictionary<string, JsonElement> Output { get; set; } = [];
}

/// <summary>Output (text delivery) pipeline settings. The provider itself is selected via <see cref="ActiveProviderSettings.Output"/>.</summary>
public sealed class OutputSettings
{
    /// <summary>Gets or sets the delivery mode; see <see cref="OutputModes"/>.</summary>
    public string Mode { get; set; } = OutputModes.Automatic;
}

/// <summary>Dictation history settings.</summary>
public sealed class HistorySettings
{
    /// <summary>Gets or sets a value indicating whether dictation history is persisted to the local database.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of history entries kept; the oldest entries are pruned
    /// on insert once the cap is exceeded. Settings validation requires at least 1.
    /// </summary>
    public int MaxEntries { get; set; } = 1000;
}

/// <summary>
/// Provider pricing rates used to estimate the cost of each billable call at insert time.
/// Purely informational — DictateFlow never bills anything; the rates mirror what the user's
/// Azure subscription charges.
/// </summary>
public sealed class PricingSettings
{
    /// <summary>Gets or sets the speech-to-text price per minute of audio.</summary>
    public double SpeechPerMinute { get; set; } = 0.006;

    /// <summary>Gets or sets the LLM price per one million prompt tokens.</summary>
    public double LlmPromptPer1M { get; set; } = 2.50;

    /// <summary>Gets or sets the LLM price per one million completion tokens.</summary>
    public double LlmCompletionPer1M { get; set; } = 10.00;

    /// <summary>Gets or sets the display currency code (e.g. <c>USD</c>); no conversion is performed.</summary>
    public string Currency { get; set; } = "USD";
}

/// <summary>Diagnostic logging settings.</summary>
public sealed class LoggingSettings
{
    /// <summary>
    /// Gets or sets the minimum Serilog log level (<c>Verbose</c>, <c>Debug</c>,
    /// <c>Information</c>, <c>Warning</c>, <c>Error</c> or <c>Fatal</c>). Read once at
    /// startup — changing it requires an application restart.
    /// </summary>
    public string MinimumLevel { get; set; } = "Information";
}
