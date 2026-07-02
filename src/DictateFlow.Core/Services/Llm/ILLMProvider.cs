using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Llm;

/// <summary>Enhances a transcript using a resolved prompt.</summary>
public interface ILLMProvider
{
    /// <summary>
    /// Sends the resolved prompt and transcript to the model and returns the enhanced text.
    /// </summary>
    /// <param name="context">The resolved prompt, transcript and sampling parameters.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The enhanced text.</returns>
    /// <exception cref="ProviderException">The provider failed in a user-actionable way.</exception>
    Task<string> ProcessAsync(
        PromptContext context,
        CancellationToken cancellationToken);
}
