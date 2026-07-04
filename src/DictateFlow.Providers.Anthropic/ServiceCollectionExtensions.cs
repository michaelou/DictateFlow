using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace DictateFlow.Providers.Anthropic;

/// <summary>
/// DI registration for the Anthropic LLM provider.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AnthropicLLMProvider"/> as a typed HTTP client with the standard
    /// resilience pipeline: 3 retries with exponential backoff on 408/429/5xx and network
    /// errors (401/403 are not retried). The user-facing timeout (<c>TimeoutSeconds</c>) is
    /// enforced per call inside the provider so settings changes apply without a restart.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="configureResilience">Optional resilience override, used by tests to shrink retry delays.</param>
    /// <returns>The typed-client builder, for further customization (e.g. a fake primary handler in tests).</returns>
    public static IHttpClientBuilder AddAnthropicLlm(
        this IServiceCollection services,
        Action<HttpStandardResilienceOptions>? configureResilience = null)
    {
        var builder = services
            // The per-call linked CancellationToken is the real timeout; disable the client-level one.
            .AddHttpClient<AnthropicLLMProvider>(client => client.Timeout = Timeout.InfiniteTimeSpan);

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
