using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// Optional companion to <see cref="ICommandAction"/> for action types that impose load-time
/// constraints on their configuration (e.g. the <c>{{Argument}}</c> placeholder rules, or an
/// http/https-only URL). The user command store calls <see cref="Validate"/> on every parsed
/// definition and skips — never executing — those an action rejects, so a rule-violating file
/// can never reach <see cref="ICommandAction.ExecuteAsync"/>.
/// </summary>
/// <remarks>
/// Keeping the rules on the action, rather than in the store, preserves the extensibility
/// contract: a new action type declares its own configuration constraints without the loader
/// knowing anything about it. Actions still re-check the invariants they depend on at execution
/// time (defense in depth for built-in definitions, which bypass the store).
/// </remarks>
public interface ICommandActionValidator
{
    /// <summary>Validates one command definition against this action type's configuration rules.</summary>
    /// <param name="definition">The definition whose action type resolves to this action.</param>
    /// <returns><see langword="null"/> when the definition is acceptable; otherwise a user-facing reason it is rejected.</returns>
    string? Validate(CommandDefinition definition);
}
