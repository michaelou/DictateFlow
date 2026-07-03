using DictateFlow.Core.Services.Providers;

namespace DictateFlow.Tests;

/// <summary>
/// In-memory <see cref="IProviderConfigReader"/> for tests: hands back the typed config
/// instances registered through <see cref="Set{T}"/> (so tests can mutate them between
/// calls) and defaults for everything else, with no JSON round-trip.
/// </summary>
public sealed class TestProviderConfigReader : IProviderConfigReader
{
    private readonly Dictionary<(ProviderKind Kind, string Name), object> _configs = [];

    /// <summary>Registers the instance returned by subsequent <see cref="GetConfig{T}"/> calls.</summary>
    public TestProviderConfigReader Set<T>(ProviderKind kind, string providerName, T config) where T : class
    {
        _configs[(kind, providerName.ToLowerInvariant())] = config;
        return this;
    }

    /// <inheritdoc />
    public T GetConfig<T>(ProviderKind kind, string providerName) where T : class, new()
        => _configs.TryGetValue((kind, providerName.ToLowerInvariant()), out var config) && config is T typed
            ? typed
            : new T();

    /// <inheritdoc />
    public void SetConfig<T>(ProviderKind kind, string providerName, T config) where T : class
        => Set(kind, providerName, config);
}
