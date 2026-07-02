using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Llm;

/// <summary>
/// Fake <see cref="ILLMProvider"/> that prefixes the transcript after an optional delay.
/// Used when no LLM endpoint is configured (so the whole dictation flow is demoable without
/// Azure) and by pipeline tests.
/// </summary>
public sealed class MockLLMProvider : ILLMProvider
{
    /// <summary>Gets or sets the prefix prepended to the transcript.</summary>
    public string Prefix { get; set; } = "[enhanced] ";

    /// <summary>Gets or sets an artificial processing delay before the result is returned.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <inheritdoc />
    public async Task<string> ProcessAsync(PromptContext context, CancellationToken cancellationToken)
    {
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
        }

        return Prefix + context.Transcript;
    }
}
