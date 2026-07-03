using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Output;
using DictateFlow.Core.Services.Transcription;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DictateFlow.Core.Services.Providers;

/// <summary>
/// Named provider registration — the single mechanism through which speech, LLM and output
/// providers become known to DictateFlow. Each call registers the implementation as a keyed
/// service under <c>name</c> and records it in the <see cref="ProviderCatalog"/> so
/// <see cref="IProviderRegistry"/> can enumerate and resolve it. Adding a provider to the
/// application is exactly one of these calls in the App DI bootstrap.
/// </summary>
public static class ProviderRegistrationExtensions
{
    /// <summary>
    /// Registers <typeparamref name="T"/> as the transcription provider named
    /// <paramref name="name"/>.
    /// </summary>
    /// <typeparam name="T">The provider implementation.</typeparam>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="name">The provider name used in settings; unique per kind, case-insensitive.</param>
    /// <param name="requiresConnection">
    /// Whether settings validation should require the remote-connection fields
    /// (<c>Endpoint</c>, <c>ApiKey</c>, <c>DeploymentName</c>) when the provider is active.
    /// Pass <see langword="false"/> for local and mock providers.
    /// </param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <remarks>
    /// <typeparamref name="T"/> itself is registered as a singleton unless it already has a
    /// registration — providers with special lifetimes (e.g. typed <c>HttpClient</c>s) keep
    /// theirs by registering it <b>before</b> this call.
    /// </remarks>
    public static IServiceCollection AddTranscriptionProvider<T>(
        this IServiceCollection services, string name, bool requiresConnection = true)
        where T : class, ITranscriptionProvider
        => AddProvider<ITranscriptionProvider, T>(services, ProviderKind.Transcription, name, requiresConnection);

    /// <summary>
    /// Registers <typeparamref name="T"/> as the LLM provider named <paramref name="name"/>.
    /// </summary>
    /// <typeparam name="T">The provider implementation.</typeparam>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="name">The provider name used in settings; unique per kind, case-insensitive.</param>
    /// <param name="requiresConnection"><inheritdoc cref="AddTranscriptionProvider{T}" path="/param[@name='requiresConnection']"/></param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <remarks><inheritdoc cref="AddTranscriptionProvider{T}" path="/remarks"/></remarks>
    public static IServiceCollection AddLLMProvider<T>(
        this IServiceCollection services, string name, bool requiresConnection = true)
        where T : class, ILLMProvider
        => AddProvider<ILLMProvider, T>(services, ProviderKind.Llm, name, requiresConnection);

    /// <summary>
    /// Registers <typeparamref name="T"/> as the output provider named <paramref name="name"/>.
    /// Output providers deliver text locally, so none of them require connection fields.
    /// </summary>
    /// <typeparam name="T">The provider implementation.</typeparam>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="name">The provider name used in settings; unique per kind, case-insensitive.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <remarks><inheritdoc cref="AddTranscriptionProvider{T}" path="/remarks"/></remarks>
    public static IServiceCollection AddOutputProvider<T>(this IServiceCollection services, string name)
        where T : class, IOutputProvider
        => AddProvider<IOutputProvider, T>(services, ProviderKind.Output, name, requiresConnection: false);

    /// <summary>
    /// The shared registration: catalog entry + keyed registration. The keyed factory
    /// resolves <typeparamref name="TImplementation"/> from the container per resolve, so the
    /// implementation's own lifetime (singleton mock, transient typed HTTP client, …) is
    /// preserved.
    /// </summary>
    private static IServiceCollection AddProvider<TService, TImplementation>(
        IServiceCollection services, ProviderKind kind, string name, bool requiresConnection)
        where TService : class
        where TImplementation : class, TService
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        GetOrCreateCatalog(services).Add(kind, name, typeof(TImplementation), requiresConnection);
        services.TryAddSingleton<TImplementation>();
        services.AddKeyedTransient<TService>(name, (sp, _) => sp.GetRequiredService<TImplementation>());
        return services;
    }

    /// <summary>
    /// Finds the catalog instance already registered on <paramref name="services"/>, or
    /// creates and registers one. Using an instance registration lets registration-time
    /// calls populate it before any container is built.
    /// </summary>
    private static ProviderCatalog GetOrCreateCatalog(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(ProviderCatalog))
            ?.ImplementationInstance as ProviderCatalog;
        if (existing is not null)
        {
            return existing;
        }

        var catalog = new ProviderCatalog();
        services.AddSingleton(catalog);
        return catalog;
    }
}
