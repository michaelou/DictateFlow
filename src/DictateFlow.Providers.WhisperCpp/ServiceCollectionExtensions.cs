using DictateFlow.Core.Services.Models;
using Microsoft.Extensions.DependencyInjection;

namespace DictateFlow.Providers.WhisperCpp;

/// <summary>
/// DI registration for the local whisper.cpp provider.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="WhisperCppModelManager"/> (as itself and as
    /// <see cref="IModelManager"/>) plus its download HTTP client. Downloads are
    /// gigabyte-scale, so the client timeout is disabled — the per-download
    /// <c>CancellationToken</c> (Cancel button) governs instead. The provider itself is
    /// registered by the App bootstrap via <c>AddTranscriptionProvider</c>.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddWhisperCppTranscription(this IServiceCollection services)
    {
        services.AddHttpClient(WhisperCppModelManager.HttpClientName, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DictateFlow-ModelDownload");
        });

        services.AddSingleton<WhisperCppModelManager>();
        services.AddSingleton<IModelManager>(sp => sp.GetRequiredService<WhisperCppModelManager>());
        return services;
    }
}
