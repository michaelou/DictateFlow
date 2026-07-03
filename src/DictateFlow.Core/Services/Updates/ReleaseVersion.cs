namespace DictateFlow.Core.Services.Updates;

/// <summary>
/// Parses and compares release version strings. Tolerates a leading <c>v</c> (GitHub tags
/// are <c>v0.1.0</c>) and any pre-release/build suffix (<c>0.1.0-beta</c>, <c>0.1.0+abc</c>),
/// comparing only the numeric <c>major.minor.patch</c> core.
/// </summary>
public static class ReleaseVersion
{
    /// <summary>
    /// Parses the numeric core of <paramref name="raw"/> into a <see cref="Version"/>.
    /// </summary>
    /// <param name="raw">A version or tag string, e.g. <c>v0.1.0</c> or <c>0.2.0-rc.1</c>.</param>
    /// <param name="version">The parsed version on success; <c>0.0</c> otherwise.</param>
    /// <returns><see langword="true"/> when a numeric core was found and parsed.</returns>
    public static bool TryParse(string? raw, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var span = raw.Trim();
        if (span.Length > 0 && (span[0] == 'v' || span[0] == 'V'))
        {
            span = span[1..];
        }

        // Keep only the leading dotted-numeric core, dropping any pre-release/build suffix.
        var end = 0;
        while (end < span.Length && (char.IsDigit(span[end]) || span[end] == '.'))
        {
            end++;
        }

        var core = span[..end].Trim('.');
        if (core.Length == 0)
        {
            return false;
        }

        // Normalize to at least major.minor.patch so that, e.g., "1.2" and "1.2.0" compare
        // equal (a missing component parses as -1 in System.Version, which would otherwise
        // read as older than an explicit 0).
        for (var parts = core.Split('.').Length; parts < 3; parts++)
        {
            core += ".0";
        }

        return Version.TryParse(core, out version!);
    }

    /// <summary>
    /// Whether <paramref name="latest"/> is a strictly newer release than
    /// <paramref name="current"/>. Returns <see langword="false"/> if either cannot be
    /// parsed, so an unreadable version never triggers a false "update available".
    /// </summary>
    public static bool IsNewer(string? latest, string? current)
        => TryParse(latest, out var latestVersion)
           && TryParse(current, out var currentVersion)
           && latestVersion > currentVersion;
}
