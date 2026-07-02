namespace DictateFlow.Core.Models;

/// <summary>
/// Root application settings persisted to <c>settings.json</c> in the DictateFlow app data directory.
/// The full schema is defined up front so it stays stable across milestones; later milestones
/// consume the individual sections.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Gets or sets the audio recording configuration (consumed in M2).</summary>
    public RecordingSettings Recording { get; set; } = new();

    /// <summary>Gets or sets the speech-recognition provider configuration (consumed in M3).</summary>
    public SpeechSettings Speech { get; set; } = new();

    /// <summary>Gets or sets the LLM enhancement provider configuration (consumed in M4).</summary>
    public LlmSettings Llm { get; set; } = new();

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
    /// <summary>Gets or sets the recording activation mode (e.g. <c>PushToTalk</c> or <c>Toggle</c>).</summary>
    public string Mode { get; set; } = "PushToTalk";

    /// <summary>Gets or sets the global hotkey that starts and stops dictation.</summary>
    public string Hotkey { get; set; } = "Ctrl+Alt+D";

    /// <summary>Gets or sets the identifier of the microphone capture device; <see langword="null"/> selects the system default.</summary>
    public string? MicrophoneDeviceId { get; set; }

    /// <summary>Gets or sets the number of seconds of silence after which recording stops automatically.</summary>
    public int SilenceTimeoutSeconds { get; set; } = 30;
}

/// <summary>Speech-recognition (transcription) provider settings.</summary>
public sealed class SpeechSettings
{
    /// <summary>Gets or sets the service endpoint URL.</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>Gets or sets the API key used to authenticate against the service.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Gets or sets the model deployment name.</summary>
    public string DeploymentName { get; set; } = "";

    /// <summary>Gets or sets the spoken language as a BCP-47 tag.</summary>
    public string Language { get; set; } = "en-US";

    /// <summary>Gets or sets the request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>LLM text-enhancement provider settings.</summary>
public sealed class LlmSettings
{
    /// <summary>Gets or sets the service endpoint URL.</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>Gets or sets the API key used to authenticate against the service.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Gets or sets the model deployment name.</summary>
    public string DeploymentName { get; set; } = "";

    /// <summary>Gets or sets the sampling temperature.</summary>
    public double Temperature { get; set; } = 0.2;

    /// <summary>Gets or sets the maximum number of completion tokens per request.</summary>
    public int MaxTokens { get; set; } = 2000;

    /// <summary>Gets or sets the request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>Output (text delivery) pipeline settings.</summary>
public sealed class OutputSettings
{
    /// <summary>Gets or sets the output provider name; see <see cref="OutputProviderNames"/>.</summary>
    public string Provider { get; set; } = OutputProviderNames.ClipboardPaste;

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
    /// on insert once the cap is exceeded. Zero or negative disables pruning.
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
