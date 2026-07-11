namespace DictateFlow.Providers.OpenRouter;

/// <summary>Well-known names of the OpenRouter providers.</summary>
public static class OpenRouterProviders
{
    /// <summary>The name both OpenRouter providers are registered and configured under.</summary>
    public const string RegistrationName = "OpenRouter";
}

/// <summary>Configuration section (<c>Providers.Llm.OpenRouter</c>) of <see cref="OpenRouterLLMProvider"/>.</summary>
public sealed class OpenRouterLlmConfig
{
    /// <summary>
    /// Gets or sets the API base URL. The default is the public OpenRouter API; the
    /// <c>/chat/completions</c> route is appended automatically when the base is given.
    /// </summary>
    public string Endpoint { get; set; } = "https://openrouter.ai/api/v1";

    /// <summary>Gets or sets the OpenRouter API key (<c>sk-or-…</c>) used to authenticate.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Gets or sets the model slug routed to, e.g. <c>anthropic/claude-sonnet-4</c>,
    /// <c>openai/gpt-4o-mini</c> or <c>google/gemini-2.5-flash</c>. See openrouter.ai/models.
    /// </summary>
    public string Model { get; set; } = "openai/gpt-4o-mini";

    /// <summary>Gets or sets the sampling temperature; the default for prompt modes without their own.</summary>
    public double Temperature { get; set; } = 0.2;

    /// <summary>Gets or sets the maximum number of completion tokens per request.</summary>
    public int MaxTokens { get; set; } = 2000;

    /// <summary>Gets or sets the request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// Configuration section (<c>Providers.Transcription.OpenRouter</c>) of
/// <see cref="OpenRouterTranscriptionProvider"/>. OpenRouter has no dedicated speech-to-text
/// endpoint, so transcription is done by sending the recording as multimodal audio input to an
/// audio-capable chat model.
/// </summary>
public sealed class OpenRouterTranscriptionConfig
{
    /// <summary>
    /// Gets or sets the API base URL. The default is the public OpenRouter API; the
    /// <c>/chat/completions</c> route is appended automatically when the base is given.
    /// </summary>
    public string Endpoint { get; set; } = "https://openrouter.ai/api/v1";

    /// <summary>Gets or sets the OpenRouter API key (<c>sk-or-…</c>) used to authenticate.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Gets or sets the audio-capable model slug used to transcribe, e.g.
    /// <c>google/gemini-2.5-flash</c> or <c>openai/gpt-4o-audio-preview</c>. The model must
    /// accept audio input — a text-only model returns an error.
    /// </summary>
    public string Model { get; set; } = "google/gemini-2.5-flash";

    /// <summary>
    /// Gets or sets an optional spoken-language hint (a BCP-47 tag such as <c>en-US</c>) added
    /// to the transcription instruction; empty lets the model detect the language itself.
    /// </summary>
    public string Language { get; set; } = "";

    /// <summary>Gets or sets the request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 60;
}
