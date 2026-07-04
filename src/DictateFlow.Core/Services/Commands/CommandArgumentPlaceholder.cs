using System.Text.RegularExpressions;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// The <c>{{Argument}}</c> placeholder used in command action configuration — the single,
/// explicit point at which the spoken utterance remainder (<c>CommandContext.Argument</c>) is
/// injected into an action. Matched case-insensitively with optional inner whitespace, the same
/// token style as the prompt-mode <c>{{Variable}}</c> tokens.
/// </summary>
/// <remarks>
/// The spoken argument is always data, never code. This type only locates and replaces the
/// placeholder; each action type is responsible for escaping the replacement appropriately for
/// where it lands (a single quoted process argument, a URL-encoded query value, …). The
/// substitution never touches anything outside the placeholder, so the executable, URL scheme,
/// host and folder target in the configuration can never be altered by speech.
/// </remarks>
public static partial class CommandArgumentPlaceholder
{
    /// <summary>The canonical placeholder spelling, for documentation and seeded examples.</summary>
    public const string Token = "{{Argument}}";

    /// <summary>Whether <paramref name="text"/> contains at least one <c>{{Argument}}</c> placeholder.</summary>
    /// <param name="text">The template to inspect; <see langword="null"/> counts as no placeholder.</param>
    public static bool Contains(string? text)
        => !string.IsNullOrEmpty(text) && PlaceholderRegex().IsMatch(text);

    /// <summary>
    /// Replaces every <c>{{Argument}}</c> placeholder in <paramref name="template"/> with the
    /// already-escaped <paramref name="replacement"/>. The replacement is inserted verbatim
    /// (no regex substitution semantics), so escaping performed by the caller survives intact.
    /// </summary>
    /// <param name="template">The template containing the placeholder(s).</param>
    /// <param name="replacement">The escaped value to insert at each placeholder.</param>
    public static string Substitute(string template, string replacement)
        => PlaceholderRegex().Replace(template, _ => replacement);

    [GeneratedRegex(@"\{\{\s*argument\s*\}\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();
}
