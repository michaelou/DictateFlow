using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// One executable action type of the voice command framework (e.g. <c>ProcessStart</c>,
/// <c>Reminder</c>). Implementations register through the command action registration
/// extensions, mirroring the provider pattern — adding an action type never modifies the
/// command pipeline.
/// </summary>
/// <remarks>
/// Safety contract: the action executes exactly what its <see cref="CommandContext.ActionValue"/>
/// configuration says. <see cref="CommandContext.Argument"/> is user speech and is only ever
/// data (a note body, a time phrase) — never something to execute. Implementations should
/// return <see cref="CommandResult.Fail"/> with a curated message rather than throw; anything
/// thrown is caught, logged and reported generically.
/// </remarks>
public interface ICommandAction
{
    /// <summary>Executes the action for one matched command.</summary>
    /// <param name="context">The matched command plus the spoken argument.</param>
    /// <param name="cancellationToken">Cancels the execution; includes the command timeout.</param>
    /// <returns>The user-presentable outcome.</returns>
    Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken);
}
