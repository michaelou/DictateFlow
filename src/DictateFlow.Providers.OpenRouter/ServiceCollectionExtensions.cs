using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace DictateFlow.Providers.OpenRouter;

/// <summary>
/// DI registration for the OpenRouter providers.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="OpenRouterLLMProvider"/> as a typed HTTP client with the standard
    /// resilience pipeline: 3 retries with exponential backoff on 408/429/5xx and network
    /// errors (401/403 are not retried). The user-facing timeout (<c>TimeoutSeconds</c>) is
    /// enforced per call inside the provider so settings changes apply without a restart.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="configureResilience">Optional resilience override, used by tests to shrink retry delays.</param>
    /// <returns>The typed-client builder, for further customization (e.g. a fake primary handler in tests).</returns>
    public static IHttpClientBuilder AddOpenRouterLlm(
        this IServiceCollection services,
        Action<HttpStandardResilienceOptions>? configureResilience = null)
    {
        var builder = services
            // The per-call linked CancellationToken is the real timeout; disable the client-level one.
            .AddHttpClient<OpenRouterLLMProvider>(client => client.Timeout = Timeout.InfiniteTimeSpan);

        return builder.WithStandardResilience(configureResilience);
    }

    /// <summary>
    /// Registers <see cref="OpenRouterTranscriptionProvider"/> as a typed HTTP client with the
    /// same standard resilience pipeline as the LLM provider. The user-facing timeout
    /// (<c>TimeoutSeconds</c>) is enforced per call inside the provider so settings changes
    /// apply without a restart.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="configureResilience">Optional resilience override, used by tests to shrink retry delays.</param>
    /// <returns>The typed-client builder, for further customization (e.g. a fake primary handler in tests).</returns>
    public static IHttpClientBuilder AddOpenRouterTranscription(
        this IServiceCollection services,
        Action<HttpStandardResilienceOptions>? configureResilience = null)
    {
        var builder = services
            // The per-call linked CancellationToken is the real timeout; disable the client-level one.
            .AddHttpClient<OpenRouterTranscriptionProvider>(client => client.Timeout = Timeout.InfiniteTimeSpan);

        return builder.WithStandardResilience(configureResilience);
    }

    /// <summary>
    /// Applies the shared resilience pipeline. The pipeline timeouts are generous static
    /// ceilings; the user-facing timeouts are enforced per call inside the providers.
    /// </summary>
    private static IHttpClientBuilder WithStandardResilience(
        this IHttpClientBuilder builder,
        Action<HttpStandardResilienceOptions>? configureResilience)
    {
        builder.AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 3;
            options.Retry.BackoffType = DelayBackoffType.Exponential;
            options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(4);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(10);
            configureResilience?.Invoke(options);
        });

        return builder;
    }
}
