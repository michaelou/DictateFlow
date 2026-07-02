namespace DictateFlow.Core.Models;

/// <summary>
/// Everything an LLM enhancement call needs; provider-agnostic. Produced by
/// <c>IPromptResolver</c> with all prompt variables already replaced.
/// </summary>
/// <param name="SystemPrompt">The fully resolved system prompt (variables already replaced).</param>
/// <param name="Transcript">The raw transcript, sent as the user message.</param>
/// <param name="Temperature">The sampling temperature for this call.</param>
/// <param name="MaxTokens">The maximum number of completion tokens for this call.</param>
/// <param name="ModeName">Name of the prompt mode the context was resolved from.</param>
public sealed record PromptContext(
    string SystemPrompt,
    string Transcript,
    double Temperature,
    int MaxTokens,
    string ModeName);
