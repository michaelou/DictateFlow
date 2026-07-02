using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Prompts;

/// <summary>
/// The prompt modes seeded into the prompts directory on first run (only when the directory
/// contains no <c>.json</c> files, so user edits and deletions are never overwritten).
/// </summary>
public static class DefaultPromptModes
{
    /// <summary>Name of the fallback mode used when a requested mode does not exist.</summary>
    public const string RawModeName = "Raw";

    /// <summary>
    /// The minimal-cleanup mode: fix punctuation and casing only, change nothing else.
    /// Also the in-memory fallback when the prompts directory has no <c>Raw.json</c>.
    /// </summary>
    public static PromptMode Raw { get; } = new(
        RawModeName,
        "Punctuation and casing fixes only — keeps your words exactly as spoken.",
        "You clean up dictated text. Fix punctuation, capitalization, sentence breaks and " +
        "obvious speech-to-text artifacts (duplicated words, missing spaces) only. Do not " +
        "rephrase, reorder, summarize, add or remove content. Never alter these terms: " +
        "{{TechnicalDictionary}}. Output only the corrected text, with no commentary.\n\n" +
        "Dictated text:\n{{Transcript}}",
        Temperature: 0.0);

    /// <summary>Professional business email.</summary>
    public static PromptMode Email { get; } = new(
        "Email",
        "Professional business email.",
        "You rewrite dictated text as a professional business email. Preserve the meaning " +
        "and every factual detail; do not invent recipients, commitments or dates. Use short " +
        "paragraphs, a natural professional tone, and a greeting and sign-off only when the " +
        "dictation implies them. Correct grammar, punctuation and casing. Never alter these " +
        "terms: {{TechnicalDictionary}}. Today is {{CurrentDate}}. Output only the email " +
        "body — no subject line, no commentary.\n\nDictated text:\n{{Transcript}}",
        Temperature: 0.2);

    /// <summary>Structured GitHub issue in Markdown.</summary>
    public static PromptMode GithubIssue { get; } = new(
        "GithubIssue",
        "Well-structured GitHub issue in Markdown.",
        "You convert dictated text into a well-structured GitHub issue in Markdown. Start " +
        "with a concise title as a level-1 heading, then organize the body into sections " +
        "such as Description, Steps to reproduce, Expected behavior and Actual behavior " +
        "when the dictation contains that information; omit sections that do not apply. Use " +
        "bullet lists for enumerations and backticks for identifiers, file paths and " +
        "commands. Preserve every technical detail exactly and invent none. Never alter " +
        "these terms: {{TechnicalDictionary}}. Output only the issue Markdown, no " +
        "commentary.\n\nDictated text:\n{{Transcript}}",
        Temperature: 0.2);

    /// <summary>Clear, well-structured prompt for an AI assistant.</summary>
    public static PromptMode ChatPrompt { get; } = new(
        "ChatPrompt",
        "Clear, well-structured prompt for an AI assistant.",
        "You rewrite dictated text as a clear, well-structured prompt for an AI assistant. " +
        "State the goal up front, keep every requirement and constraint from the dictation, " +
        "remove filler words and false starts, and present multi-part requests as a " +
        "numbered list. Do not answer the prompt yourself and do not add requirements. " +
        "Never alter these terms: {{TechnicalDictionary}}. Output only the rewritten " +
        "prompt.\n\nDictated text:\n{{Transcript}}",
        Temperature: 0.3);

    /// <summary>Technical specification in Markdown.</summary>
    public static PromptMode TechnicalSpec { get; } = new(
        "TechnicalSpec",
        "Technical specification in Markdown.",
        "You turn dictated notes into a technical specification in Markdown. Organize the " +
        "content into sections such as Overview, Requirements, Design and Open questions as " +
        "the material allows, use numbered requirements and bullet points, and use " +
        "backticks for identifiers, file paths and commands. Keep every technical detail " +
        "from the dictation and invent nothing beyond structure and headings. Never alter " +
        "these terms: {{TechnicalDictionary}}. Today is {{CurrentDate}}. Output only the " +
        "specification.\n\nDictated text:\n{{Transcript}}",
        Temperature: 0.2);

    /// <summary>Gets all default modes, in the order they are seeded.</summary>
    public static IReadOnlyList<PromptMode> All { get; } = [Email, GithubIssue, ChatPrompt, TechnicalSpec, Raw];
}
