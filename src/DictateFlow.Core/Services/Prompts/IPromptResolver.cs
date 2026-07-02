using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Prompts;

/// <summary>Builds the <see cref="PromptContext"/> an LLM call needs from a transcript.</summary>
public interface IPromptResolver
{
    /// <summary>
    /// Builds a <see cref="PromptContext"/> for a transcript: picks the mode (falling back
    /// to <c>Raw</c> when the requested mode does not exist) and replaces the
    /// <c>{{Variable}}</c> tokens in its system prompt.
    /// </summary>
    /// <param name="transcript">The raw transcript to enhance.</param>
    /// <param name="modeName">Name of the prompt mode to apply.</param>
    /// <returns>The resolved context, ready for <c>ILLMProvider.ProcessAsync</c>.</returns>
    PromptContext Resolve(string transcript, string modeName);
}
