namespace DictateFlow.Core.Services.Providers;

/// <summary>One provider registration: what kind it is, its settings name and its implementation type.</summary>
/// <param name="Kind">The provider slot the registration fills.</param>
/// <param name="Name">The name used in settings and as the keyed-DI service key.</param>
/// <param name="ImplementationType">The concrete provider type.</param>
/// <param name="RequiresConnection">
/// Whether the provider talks to a remote service and therefore needs the connection fields
/// (<c>Endpoint</c>, <c>ApiKey</c>, <c>DeploymentName</c>) in its config section. Local and
/// mock providers register with <see langword="false"/> so settings validation does not
/// demand credentials they don't use.
/// </param>
public sealed record ProviderRegistration(
    ProviderKind Kind, string Name, Type ImplementationType, bool RequiresConnection);

/// <summary>
/// Records every provider registered through the
/// <see cref="ProviderRegistrationExtensions"/> so the registry (and the settings UI) can
/// enumerate what exists without building any provider. Populated at DI-registration time
/// and immutable afterwards; names are unique per kind, case-insensitively.
/// </summary>
public sealed class ProviderCatalog
{
    private readonly List<ProviderRegistration> _registrations = [];

    /// <summary>Gets all registrations, in registration order.</summary>
    public IReadOnlyList<ProviderRegistration> Registrations => _registrations;

    /// <summary>Records one registration.</summary>
    /// <param name="kind">The provider slot being filled.</param>
    /// <param name="name">The settings name; must be unique per kind (case-insensitive).</param>
    /// <param name="implementationType">The concrete provider type.</param>
    /// <param name="requiresConnection">Whether validation should demand the remote-connection config fields.</param>
    /// <exception cref="InvalidOperationException"><paramref name="name"/> is already registered for <paramref name="kind"/>.</exception>
    public void Add(ProviderKind kind, string name, Type implementationType, bool requiresConnection = true)
    {
        if (TryGetRegisteredName(kind, name, out _))
        {
            throw new InvalidOperationException(
                $"A {kind} provider named '{name}' is already registered.");
        }

        _registrations.Add(new ProviderRegistration(kind, name, implementationType, requiresConnection));
    }

    /// <summary>
    /// Whether the named provider registered as needing remote-connection config fields;
    /// unknown names return <see langword="false"/> (they fail name validation instead).
    /// </summary>
    /// <param name="kind">The provider slot to search.</param>
    /// <param name="name">The name as configured (case-insensitive).</param>
    public bool RequiresConnection(ProviderKind kind, string name)
        => _registrations.FirstOrDefault(
            r => r.Kind == kind && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.RequiresConnection ?? false;

    /// <summary>Gets the registered provider names of one kind, in registration order.</summary>
    /// <param name="kind">The provider slot to enumerate.</param>
    public IReadOnlyList<string> GetNames(ProviderKind kind)
        => [.. _registrations.Where(r => r.Kind == kind).Select(r => r.Name)];

    /// <summary>
    /// Finds the registered spelling of <paramref name="name"/> (case-insensitive), so
    /// settings values can be canonicalized to the exact keyed-DI service key.
    /// </summary>
    /// <param name="kind">The provider slot to search.</param>
    /// <param name="name">The name as configured.</param>
    /// <param name="registeredName">The name as registered, when found.</param>
    public bool TryGetRegisteredName(ProviderKind kind, string name, out string registeredName)
    {
        var match = _registrations.FirstOrDefault(
            r => r.Kind == kind && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        registeredName = match?.Name ?? "";
        return match is not null;
    }
}
