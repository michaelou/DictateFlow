namespace DictateFlow.Providers.AzureFoundry;

/// <summary>Well-known names of the Azure AI Foundry providers.</summary>
public static class AzureFoundryProviders
{
    /// <summary>The name both Azure AI Foundry providers are registered and configured under.</summary>
    public const string RegistrationName = "AzureFoundry";
}

/// <summary>Configuration section (<c>Providers.Transcription.AzureFoundry</c>) of <see cref="AzureFoundryTranscriptionProvider"/>.</summary>
public sealed class AzureFoundryTranscriptionConfig
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

/// <summary>Configuration section (<c>Providers.Llm.AzureFoundry</c>) of <see cref="AzureFoundryLLMProvider"/>.</summary>
public sealed class AzureFoundryLlmConfig
{
    /// <summary>Gets or sets the service endpoint URL.</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>Gets or sets the API key used to authenticate against the service.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Gets or sets the model deployment name.</summary>
    public string DeploymentName { get; set; } = "";

    /// <summary>Gets or sets the sampling temperature; the default for prompt modes without their own.</summary>
    public double Temperature { get; set; } = 0.2;

    /// <summary>Gets or sets the maximum number of completion tokens per request.</summary>
    public int MaxTokens { get; set; } = 2000;

    /// <summary>Gets or sets the request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 60;
}
