namespace DictateFlow.Core.Services.Llm;

/// <summary>
/// The sampling defaults an LLM provider's config section may carry (<c>Temperature</c>,
/// <c>MaxTokens</c>) — read from the active provider's section when building a prompt
/// context for modes without their own temperature. Providers without these fields (e.g.
/// the mock) simply yield the defaults below; extra fields in the section are ignored.
/// </summary>
public sealed class LlmSamplingDefaults
{
    /// <summary>Gets or sets the default sampling temperature.</summary>
    public double Temperature { get; set; } = 0.2;

    /// <summary>Gets or sets the maximum number of completion tokens per request.</summary>
    public int MaxTokens { get; set; } = 2000;
}
