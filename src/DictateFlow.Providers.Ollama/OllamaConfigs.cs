namespace DictateFlow.Providers.Ollama;

/// <summary>Well-known names of the Ollama provider.</summary>
public static class OllamaProviders
{
    /// <summary>The name the Ollama LLM provider is registered and configured under.</summary>
    public const string RegistrationName = "Ollama";
}

/// <summary>Configuration section (<c>Providers.Llm.Ollama</c>) of <see cref="OllamaLLMProvider"/>.</summary>
public sealed class OllamaLlmConfig
{
    /// <summary>
    /// Gets or sets the Ollama server base URL. The default is the local Ollama daemon;
    /// set <c>https://ollama.com</c> (plus an API key) for Ollama Cloud, or any remote host.
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Gets or sets the API key sent as a bearer token. Empty for a local server (no
    /// authentication); required for Ollama Cloud.
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Gets or sets the model name, e.g. <c>llama3.2</c> or <c>qwen3</c>. The model must be
    /// pulled on the server (<c>ollama pull &lt;model&gt;</c>) before it can be used.
    /// </summary>
    public string Model { get; set; } = "llama3.2";

    /// <summary>Gets or sets the sampling temperature; the default for prompt modes without their own.</summary>
    public double Temperature { get; set; } = 0.2;

    /// <summary>Gets or sets the maximum number of completion tokens per request.</summary>
    public int MaxTokens { get; set; } = 2000;

    /// <summary>
    /// Gets or sets the request timeout in seconds. Local inference is hardware-bound, so
    /// the default is more generous than the cloud providers'.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 120;
}
