using System.Text.RegularExpressions;
using DictateFlow.Core.Models;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Replacements;

/// <summary>
/// Default <see cref="ITextReplacementService"/> implementation. Reads
/// <see cref="AppSettings.Replacements"/> on every call (so edits apply live) and rewrites the
/// transcript one rule at a time, in configured order. Whole-word rules match only tokens
/// bounded by non-word characters; the replacement text is inserted literally, so <c>$</c> and
/// other regex-substitution characters in it are never interpreted.
/// </summary>
public sealed class TextReplacementService : ITextReplacementService
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<TextReplacementService> _logger;

    /// <summary>Initializes a new instance of the <see cref="TextReplacementService"/> class.</summary>
    /// <param name="settingsService">Supplies the replacement rules, read per call.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public TextReplacementService(ISettingsService settingsService, ILogger<TextReplacementService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Apply(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var rules = _settingsService.Current.Replacements;
        if (rules is not { Count: > 0 })
        {
            return text;
        }

        var applied = 0;
        var result = text;
        foreach (var rule in rules)
        {
            if (string.IsNullOrEmpty(rule.From))
            {
                continue;
            }

            var before = result;
            try
            {
                result = ApplyRule(result, rule);
            }
            catch (RegexMatchTimeoutException ex)
            {
                _logger.LogWarning(ex, "Replacement rule '{From}' timed out; skipping it", rule.From);
                continue;
            }

            if (!ReferenceEquals(before, result) && before != result)
            {
                applied++;
            }
        }

        if (applied > 0)
        {
            _logger.LogDebug("Replacement dictionary applied {AppliedCount} of {RuleCount} rules", applied, rules.Count);
        }

        return result;
    }

    /// <summary>Rewrites every occurrence of <paramref name="rule"/>'s <c>From</c> in <paramref name="text"/>.</summary>
    private static string ApplyRule(string text, ReplacementRule rule)
    {
        var pattern = Regex.Escape(rule.From);
        if (rule.WholeWord)
        {
            // Assert a non-word character (or string boundary) on each side, so "Marco" does
            // not match inside "Marconi". Lookarounds work even when From starts or ends with a
            // non-word character, where a plain \b would not.
            pattern = $@"(?<!\w){pattern}(?!\w)";
        }

        var options = RegexOptions.CultureInvariant | (rule.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);

        // A MatchEvaluator inserts the replacement verbatim — '$' groups are not expanded.
        return Regex.Replace(text, pattern, _ => rule.To, options, TimeSpan.FromSeconds(1));
    }
}
