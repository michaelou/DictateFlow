namespace DictateFlow.Core.Models;

/// <summary>
/// A named enhancement style loaded from a JSON file in the prompts directory
/// (one file per mode, <c>{Name}.json</c>).
/// </summary>
/// <param name="Name">Unique mode name (e.g. <c>Email</c>); also the active-mode selector value.</param>
/// <param name="Description">Short human-readable description shown in the Settings UI.</param>
/// <param name="SystemPrompt">System prompt template; may contain <c>{{Variable}}</c> tokens.</param>
/// <param name="Temperature">Per-mode temperature override; <see langword="null"/> uses <c>Llm.Temperature</c> from settings.</param>
public sealed record PromptMode(
    string Name,
    string Description,
    string SystemPrompt,
    double? Temperature);
