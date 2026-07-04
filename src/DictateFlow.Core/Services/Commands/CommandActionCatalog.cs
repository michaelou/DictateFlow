namespace DictateFlow.Core.Services.Commands;

/// <summary>One command action registration: its settings/JSON name and its implementation type.</summary>
/// <param name="ActionType">The name used in command definitions and as the keyed-DI service key.</param>
/// <param name="ImplementationType">The concrete action type.</param>
public sealed record CommandActionRegistration(string ActionType, Type ImplementationType);

/// <summary>
/// Records every command action registered through the
/// <see cref="CommandActionRegistrationExtensions"/> — the fixed allowlist of what a voice
/// command can execute. Populated at DI-registration time and immutable afterwards; action
/// type names are unique, case-insensitively. A definition naming an action type outside
/// this catalog is rejected and never executes.
/// </summary>
public sealed class CommandActionCatalog
{
    private readonly List<CommandActionRegistration> _registrations = [];

    /// <summary>Gets all registrations, in registration order.</summary>
    public IReadOnlyList<CommandActionRegistration> Registrations => _registrations;

    /// <summary>Records one registration.</summary>
    /// <param name="actionType">The action type name; must be unique (case-insensitive).</param>
    /// <param name="implementationType">The concrete action type.</param>
    /// <exception cref="InvalidOperationException"><paramref name="actionType"/> is already registered.</exception>
    public void Add(string actionType, Type implementationType)
    {
        if (TryGetRegisteredName(actionType, out _))
        {
            throw new InvalidOperationException(
                $"A command action named '{actionType}' is already registered.");
        }

        _registrations.Add(new CommandActionRegistration(actionType, implementationType));
    }

    /// <summary>Gets the registered action type names, in registration order.</summary>
    public IReadOnlyList<string> GetNames()
        => [.. _registrations.Select(r => r.ActionType)];

    /// <summary>
    /// Finds the registered spelling of <paramref name="actionType"/> (case-insensitive), so
    /// definition values can be canonicalized to the exact keyed-DI service key.
    /// </summary>
    /// <param name="actionType">The action type name as configured.</param>
    /// <param name="registeredName">The name as registered, when found.</param>
    public bool TryGetRegisteredName(string actionType, out string registeredName)
    {
        var match = _registrations.FirstOrDefault(
            r => string.Equals(r.ActionType, actionType, StringComparison.OrdinalIgnoreCase));
        registeredName = match?.ActionType ?? "";
        return match is not null;
    }
}
