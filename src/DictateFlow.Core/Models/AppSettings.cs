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

    /// <summary>Gets or sets the name of the active prompt mode used for LLM enhancement (consumed in M4).</summary>
    public string ActivePromptMode { get; set; } = "Raw";

    /// <summary>Gets or sets user-defined technical terms that bias transcription and enhancement (consumed in M4).</summary>
    public List<string> TechnicalDictionary { get; set; } = [];

    /// <summary>Gets or sets per-application output rules (consumed in later milestones).</summary>
    public List<string> ApplicationRules { get; set; } = [];
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
    /// <summary>Gets or sets the output provider name (e.g. <c>ClipboardPaste</c>).</summary>
    public string Provider { get; set; } = "ClipboardPaste";

    /// <summary>Gets or sets the delivery mode (e.g. <c>Automatic</c> or <c>Manual</c>).</summary>
    public string Mode { get; set; } = "Automatic";
}

/// <summary>Dictation history settings.</summary>
public sealed class HistorySettings
{
    /// <summary>Gets or sets a value indicating whether dictation history is persisted to the local database.</summary>
    public bool Enabled { get; set; } = true;
}
