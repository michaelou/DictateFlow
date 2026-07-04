using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using DictateFlow.Core.Services.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Updates;

/// <summary>
/// <see cref="IUpdateService"/> backed by the GitHub "latest release" API. Reads the current
/// version from <see cref="IDiagnosticsService"/> and compares it with the latest published
/// release tag. It only reports — nothing is downloaded or installed.
/// </summary>
public sealed class GitHubUpdateService : IUpdateService
{
    /// <summary>Public endpoint for the newest non-draft release of the repository.</summary>
    private const string LatestReleaseUrl = "https://api.github.com/repos/michaelou/DictateFlow/releases/latest";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IDiagnosticsService _diagnostics;
    private readonly ILogger<GitHubUpdateService> _logger;

    /// <summary>Initializes a new instance of the <see cref="GitHubUpdateService"/> class.</summary>
    /// <param name="httpClient">The typed HTTP client (User-Agent and timeout are configured at registration).</param>
    /// <param name="diagnostics">Supplies the installed application version.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public GitHubUpdateService(HttpClient httpClient, IDiagnosticsService diagnostics, ILogger<GitHubUpdateService> logger)
    {
        _httpClient = httpClient;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var current = _diagnostics.AppVersion;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GitHub update check returned HTTP {StatusCode}", (int)response.StatusCode);
                return UpdateCheckResult.Failed(
                    current,
                    $"GitHub returned {(int)response.StatusCode} ({response.ReasonPhrase}). Please try again later.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            {
                _logger.LogWarning("GitHub update check returned no usable release tag");
                return UpdateCheckResult.Failed(current, "The latest release information could not be read.");
            }

            var latestDisplay = release.TagName.TrimStart('v', 'V');
            if (ReleaseVersion.IsNewer(release.TagName, current))
            {
                var installer = SelectInstallerAsset(release.Assets);
                _logger.LogInformation(
                    "Update available: {Latest} (installed {Current}); installer asset {Installer}",
                    latestDisplay,
                    current,
                    installer?.Name ?? "(none)");
                return UpdateCheckResult.Available(
                    current,
                    latestDisplay,
                    release.Body,
                    release.HtmlUrl,
                    installer?.BrowserDownloadUrl,
                    installer?.Size ?? 0);
            }

            _logger.LogInformation("No update available (installed {Current}, latest {Latest})", current, latestDisplay);
            return UpdateCheckResult.UpToDate(current, latestDisplay);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A genuine caller cancellation — let it propagate.
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient surfaces its own timeout as a cancellation with our token untouched.
            _logger.LogWarning("Update check timed out");
            return UpdateCheckResult.Failed(current, "The update check timed out. Please try again later.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException)
        {
            _logger.LogWarning(ex, "Update check failed");
            return UpdateCheckResult.Failed(current, "Could not reach GitHub — check your internet connection and try again.");
        }
    }

    /// <summary>
    /// Picks the Inno Setup installer from a release's assets: the one named
    /// <c>DictateFlowSetup-v*.exe</c> (as produced by <c>scripts/release.ps1</c>), never the
    /// <c>DictateFlowPortable-*.zip</c>. Returns <see langword="null"/> when no such asset
    /// exists so the caller falls back to linking the release page.
    /// </summary>
    private static GitHubAsset? SelectInstallerAsset(IReadOnlyList<GitHubAsset>? assets)
    {
        if (assets is null)
        {
            return null;
        }

        return assets.FirstOrDefault(asset =>
            !string.IsNullOrWhiteSpace(asset.Name)
            && !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl)
            && asset.Name.StartsWith("DictateFlowSetup", StringComparison.OrdinalIgnoreCase)
            && asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The subset of the GitHub release payload this service reads.</summary>
    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    /// <summary>The subset of a GitHub release asset this service reads.</summary>
    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}
