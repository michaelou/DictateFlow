namespace DictateFlow.Providers.Parakeet;

/// <summary>Well-known names of the Parakeet local provider.</summary>
public static class ParakeetProviders
{
    /// <summary>The name the Parakeet transcription provider is registered and configured under.</summary>
    public const string RegistrationName = "Parakeet";
}

/// <summary>Configuration section (<c>Providers.Transcription.Parakeet</c>) of <see cref="ParakeetTranscriptionProvider"/>.</summary>
public sealed class ParakeetTranscriptionConfig
{
    /// <summary>
    /// Gets or sets the number of CPU threads the ONNX runtime may use; 0 picks a sensible
    /// default based on the machine's core count. Parakeet TDT v3 is multilingual (25
    /// European languages) and detects the spoken language itself, so there is no language
    /// setting.
    /// </summary>
    public int Threads { get; set; }

    /// <summary>
    /// Gets or sets the transcription timeout in seconds. Local inference is CPU-bound, so
    /// the default is more generous than the cloud providers'.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;
}
