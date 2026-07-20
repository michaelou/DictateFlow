using DictateFlow.Core.Services.CloudRecordings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DictateFlow.Providers.AzureBlobStorage;

/// <summary>
/// DI registration for the Azure Blob Storage cloud recording source.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AzureBlobRecordingSource"/> as the <see cref="ICloudRecordingSource"/>.
    /// The source reads its connection string and container from settings on every call.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddAzureBlobRecordings(this IServiceCollection services)
    {
        services.TryAddSingleton<ICloudRecordingSource, AzureBlobRecordingSource>();
        return services;
    }
}
