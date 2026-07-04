using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// Default <see cref="ICommandActionResolver"/> implementation over the
/// <see cref="CommandActionCatalog"/> and keyed DI, mirroring the provider registry.
/// </summary>
public sealed class CommandActionResolver : ICommandActionResolver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly CommandActionCatalog _catalog;

    /// <summary>Initializes a new instance of the <see cref="CommandActionResolver"/> class.</summary>
    /// <param name="serviceProvider">Resolves the keyed action registrations.</param>
    /// <param name="catalog">Enumerates what was registered.</param>
    public CommandActionResolver(IServiceProvider serviceProvider, CommandActionCatalog catalog)
    {
        _serviceProvider = serviceProvider;
        _catalog = catalog;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetActionTypes() => _catalog.GetNames();

    /// <inheritdoc />
    public bool TryResolve(string actionType, [NotNullWhen(true)] out ICommandAction? action)
    {
        if (!string.IsNullOrWhiteSpace(actionType)
            && _catalog.TryGetRegisteredName(actionType, out var registeredName))
        {
            action = _serviceProvider.GetRequiredKeyedService<ICommandAction>(registeredName);
            return true;
        }

        action = null;
        return false;
    }
}
