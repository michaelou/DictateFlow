using System.Text;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// Escapes a spoken argument for safe insertion into a <b>double-quoted</b> position of a
/// Windows process-arguments template (the <c>{{Argument}}</c> placeholder is written quoted,
/// e.g. <c>"{{Argument}}"</c>). Following the <c>CommandLineToArgvW</c> rules, embedded quotes
/// are backslash-escaped and runs of backslashes are doubled where a quote follows, so the
/// spoken text can only ever become the content of that single argument — never break out into
/// additional arguments or a second executable.
/// </summary>
public static class ProcessArgumentEscaper
{
    /// <summary>Escapes <paramref name="argument"/> for placement inside a double-quoted argument token.</summary>
    /// <param name="argument">The raw spoken text.</param>
    public static string EscapeInsideQuotes(string argument)
    {
        var builder = new StringBuilder(argument.Length);
        var index = 0;
        while (index < argument.Length)
        {
            var backslashes = 0;
            while (index < argument.Length && argument[index] == '\\')
            {
                backslashes++;
                index++;
            }

            if (index == argument.Length)
            {
                // Trailing backslashes precede the template's closing quote: double them so
                // that quote stays a real quote rather than being escaped.
                builder.Append('\\', backslashes * 2);
            }
            else if (argument[index] == '"')
            {
                // Double the preceding backslashes, then escape the quote itself.
                builder.Append('\\', backslashes * 2 + 1);
                builder.Append('"');
                index++;
            }
            else
            {
                builder.Append('\\', backslashes);
                builder.Append(argument[index]);
                index++;
            }
        }

        return builder.ToString();
    }
}
