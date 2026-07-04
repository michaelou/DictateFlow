using System.Diagnostics.CodeAnalysis;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// Resolves an action type name from a command definition to its registered
/// <see cref="ICommandAction"/>. Only names in the <see cref="CommandActionCatalog"/>
/// resolve — an unknown name fails the lookup, so nothing outside the registered allowlist
/// can ever execute.
/// </summary>
public interface ICommandActionResolver
{
    /// <summary>Gets the registered action type names, in registration order.</summary>
    IReadOnlyList<string> GetActionTypes();

    /// <summary>Resolves the action registered under <paramref name="actionType"/> (case-insensitive).</summary>
    /// <param name="actionType">The action type name from the command definition.</param>
    /// <param name="action">The registered action, when found.</param>
    bool TryResolve(string actionType, [NotNullWhen(true)] out ICommandAction? action);
}
