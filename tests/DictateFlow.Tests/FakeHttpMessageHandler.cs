using System.Net;

namespace DictateFlow.Tests;

/// <summary>A recorded request: everything captured before the response was produced.</summary>
/// <param name="Uri">The request URI.</param>
/// <param name="ApiKeyHeader">Value of the <c>api-key</c> header, if present.</param>
/// <param name="Body">The full (multipart) body as a string.</param>
/// <param name="ContentType">The request content type, including the boundary.</param>
public sealed record RecordedRequest(Uri? Uri, string? ApiKeyHeader, string Body, string? ContentType);

/// <summary>
/// <see cref="HttpMessageHandler"/> test double that records every request (URI, headers,
/// body) and replies with a scripted response factory.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    /// <summary>Initializes a handler that runs <paramref name="responder"/> for every request.</summary>
    /// <param name="responder">Produces the response; may delay or throw.</param>
    public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    /// <summary>Initializes a handler that always returns <paramref name="statusCode"/> with <paramref name="jsonBody"/>.</summary>
    /// <param name="statusCode">Status code to return.</param>
    /// <param name="jsonBody">JSON response body.</param>
    public FakeHttpMessageHandler(HttpStatusCode statusCode, string jsonBody = "{}")
        : this((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json"),
        }))
    {
    }

    /// <summary>Gets the requests seen so far, in order.</summary>
    public List<RecordedRequest> Requests { get; } = [];

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? ""
            : await request.Content.ReadAsStringAsync(CancellationToken.None);
        request.Headers.TryGetValues("api-key", out var apiKeyValues);
        Requests.Add(new RecordedRequest(
            request.RequestUri,
            apiKeyValues?.FirstOrDefault(),
            body,
            request.Content?.Headers.ContentType?.ToString()));

        return await _responder(request, cancellationToken);
    }
}
