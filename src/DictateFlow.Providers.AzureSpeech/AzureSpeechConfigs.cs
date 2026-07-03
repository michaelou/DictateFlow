namespace DictateFlow.Providers.AzureSpeech;

/// <summary>Well-known names of the Azure Speech provider.</summary>
public static class AzureSpeechProviders
{
    /// <summary>The name the provider is registered and configured under.</summary>
    public const string RegistrationName = "AzureSpeech";
}

/// <summary>Configuration section (<c>Providers.Transcription.AzureSpeech</c>) of <see cref="AzureSpeechTranscriptionProvider"/>.</summary>
public sealed class AzureSpeechTranscriptionConfig
{
    /// <summary>
    /// Gets or sets the Speech resource endpoint URL
    /// (e.g. <c>https://myresource.cognitiveservices.azure.com/</c> — the same endpoint an
    /// AI Foundry resource exposes).
    /// </summary>
    public string Endpoint { get; set; } = "";

    /// <summary>Gets or sets the API key used to authenticate against the service.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Gets or sets the candidate spoken languages as comma-separated BCP-47 tags
    /// (e.g. <c>en-US, el-GR</c>). One tag pins the recognition language; several enable
    /// continuous language identification restricted to those languages; empty uses the
    /// service default (<c>en-US</c>).
    /// </summary>
    public string Language { get; set; } = "en-US";

    /// <summary>Gets or sets the timeout in seconds for non-streaming (whole-capture) transcription.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}
