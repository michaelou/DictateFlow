using System.Text.Json;
using DictateFlow.Core.Services;

namespace DictateFlow.Providers.OpenRouter;

/// <summary>Builds the OpenRouter chat-completions request URI from a configured endpoint.</summary>
internal static class OpenRouterEndpoint
{
    /// <summary>
    /// Accepts either the API base (<c>https://openrouter.ai/api/v1</c>), to which the
    /// <c>/chat/completions</c> route is appended, or a complete <c>…/chat/completions</c> URL,
    /// used as-is.
    /// </summary>
    /// <param name="endpoint">The configured endpoint value.</param>
    /// <param name="providerName">The provider name used in any thrown <see cref="ProviderException"/>.</param>
    /// <exception cref="ProviderException">The endpoint is not a valid http(s) URL.</exception>
    public static Uri BuildChatCompletionsUri(string endpoint, string providerName)
    {
        var endpointText = endpoint.Trim();
        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ProviderException(
                providerName,
                $"'{endpoint}' is not a valid http(s) URL. Check the endpoint in Settings.",
                isConfigurationError: true);
        }

        return uri.AbsolutePath.TrimEnd('/').EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? uri
            : new Uri($"{endpointText.TrimEnd('/')}/chat/completions");
    }
}

/// <summary>Extracts a short, human-readable detail from an OpenRouter error response body.</summary>
internal static class OpenRouterError
{
    /// <summary>
    /// Returns the service's <c>error.message</c> when present, otherwise a trimmed snippet,
    /// prefixed with a space so it reads cleanly appended to a status line (empty when there is
    /// nothing useful).
    /// </summary>
    public static string Describe(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "";
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                if (error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                {
                    return $" {message.GetString()}";
                }
            }
            else if (error.ValueKind == JsonValueKind.String)
            {
                return $" {error.GetString()}";
            }
        }
        catch (JsonException)
        {
            // Not JSON — fall through to the raw snippet.
        }

        var trimmed = body.Trim();
        return trimmed.Length > 300 ? $" {trimmed[..300]}…" : $" {trimmed}";
    }
}
