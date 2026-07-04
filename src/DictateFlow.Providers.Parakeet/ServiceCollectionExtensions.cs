using DictateFlow.Core.Services.Models;
using Microsoft.Extensions.DependencyInjection;

namespace DictateFlow.Providers.Parakeet;

/// <summary>
/// DI registration for the local Parakeet provider.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="ParakeetModelManager"/> (as itself and as
    /// <see cref="IModelManager"/>) plus its download HTTP client. Downloads are
    /// hundreds of megabytes, so the client timeout is disabled — the per-download
    /// <c>CancellationToken</c> (Cancel button) governs instead. The provider itself is
    /// registered by the App bootstrap via <c>AddTranscriptionProvider</c>.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddParakeetTranscription(this IServiceCollection services)
    {
        services.AddHttpClient(ParakeetModelManager.HttpClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DictateFlow-ModelDownload");
        });

        services.AddSingleton<ParakeetModelManager>();
        services.AddSingleton<IModelManager>(sp => sp.GetRequiredService<ParakeetModelManager>());
        return services;
    }
}
