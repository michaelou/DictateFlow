namespace DictateFlow.Providers.WhisperCpp;

/// <summary>Well-known names of the whisper.cpp local provider.</summary>
public static class WhisperCppProviders
{
    /// <summary>The name the whisper.cpp transcription provider is registered and configured under.</summary>
    public const string RegistrationName = "WhisperCpp";
}

/// <summary>Configuration section (<c>Providers.Transcription.WhisperCpp</c>) of <see cref="WhisperCppTranscriptionProvider"/>.</summary>
public sealed class WhisperCppTranscriptionConfig
{
    /// <summary>
    /// Gets or sets the model to transcribe with, by catalog id (<c>ggml-small</c> or
    /// <c>ggml-medium</c>). The model must be installed via the Local Models settings page.
    /// </summary>
    public string Model { get; set; } = WhisperCppModelCatalog.SmallModelId;

    /// <summary>
    /// Gets or sets the spoken language as a BCP-47 tag (e.g. <c>en</c> or <c>el-GR</c>;
    /// whisper.cpp uses only the primary language subtag). Empty lets the model auto-detect,
    /// which also handles mixed-language dictation.
    /// </summary>
    public string Language { get; set; } = "";

    /// <summary>
    /// Gets or sets the number of CPU threads whisper.cpp may use; 0 lets it decide.
    /// </summary>
    public int Threads { get; set; }

    /// <summary>
    /// Gets or sets the transcription timeout in seconds. Local inference is CPU-bound, so
    /// the default is more generous than the cloud providers'.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;
}
