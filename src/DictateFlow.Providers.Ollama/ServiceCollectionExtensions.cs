using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace DictateFlow.Providers.Ollama;

/// <summary>
/// DI registration for the Ollama LLM provider.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="OllamaLLMProvider"/> as a typed HTTP client with the standard
    /// resilience pipeline: 3 retries with exponential backoff on 408/429/5xx and network
    /// errors (401/403 are not retried). The user-facing timeout (<c>TimeoutSeconds</c>) is
    /// enforced per call inside the provider so settings changes apply without a restart.
    /// Local models can take minutes on modest hardware, so the pipeline ceilings are
    /// generous static limits only.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="configureResilience">Optional resilience override, used by tests to shrink retry delays.</param>
    /// <returns>The typed-client builder, for further customization (e.g. a fake primary handler in tests).</returns>
    public static IHttpClientBuilder AddOllamaLlm(
        this IServiceCollection services,
        Action<HttpStandardResilienceOptions>? configureResilience = null)
    {
        var builder = services
            // The per-call linked CancellationToken is the real timeout; disable the client-level one.
            .AddHttpClient<OllamaLLMProvider>(client => client.Timeout = Timeout.InfiniteTimeSpan);

        builder.AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 3;
            options.Retry.BackoffType = DelayBackoffType.Exponential;
            options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(5);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(10);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(20);
            configureResilience?.Invoke(options);
        });

        return builder;
    }
}
