namespace DictateFlow.Providers.Anthropic;

/// <summary>Well-known names of the Anthropic provider.</summary>
public static class AnthropicProviders
{
    /// <summary>The name the Anthropic LLM provider is registered and configured under.</summary>
    public const string RegistrationName = "Anthropic";
}

/// <summary>Configuration section (<c>Providers.Llm.Anthropic</c>) of <see cref="AnthropicLLMProvider"/>.</summary>
public sealed class AnthropicLlmConfig
{
    /// <summary>
    /// Gets or sets the API base URL. The default is the public Anthropic API; change it only
    /// when routing through a gateway or proxy that exposes the same Messages API.
    /// </summary>
    public string Endpoint { get; set; } = "https://api.anthropic.com";

    /// <summary>Gets or sets the Anthropic API key (<c>sk-ant-…</c>) used to authenticate.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Gets or sets the model id, e.g. <c>claude-opus-4-8</c> (most capable) or
    /// <c>claude-haiku-4-5</c> (fastest and cheapest).
    /// </summary>
    public string Model { get; set; } = "claude-opus-4-8";

    /// <summary>
    /// Gets or sets the sampling temperature; the default for prompt modes without their own.
    /// Current Anthropic models (Opus 4.7+, Sonnet 5, Fable 5) reject a non-default
    /// temperature — the provider retries without it automatically, so any model works.
    /// </summary>
    public double Temperature { get; set; } = 0.2;

    /// <summary>Gets or sets the maximum number of completion tokens per request.</summary>
    public int MaxTokens { get; set; } = 2000;

    /// <summary>Gets or sets the request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 60;
}
