namespace DictateFlow.App.Services.Commands;

/// <summary>
/// The fixed set of internal DictateFlow operations a <c>DictateFlowAction</c> command can run.
/// The value in a command definition must name one of these members (case-insensitive); an
/// unknown value is rejected at load and never executes, so a voice command can only ever reach
/// one of these built-in operations — never arbitrary code.
/// </summary>
public enum DictateFlowOperation
{
    /// <summary>Opens the Settings window.</summary>
    OpenSettings,

    /// <summary>Opens the dictation History window.</summary>
    ShowHistory,

    /// <summary>Opens the Cost Dashboard window.</summary>
    OpenCostDashboard,

    /// <summary>Checks GitHub for a newer release and shows the result.</summary>
    CheckForUpdates,

    /// <summary>
    /// Switches the active prompt mode to the mode named by the spoken argument (e.g.
    /// <c>switch to Email mode</c>). The only operation that consumes
    /// <see cref="Core.Models.CommandContext.Argument"/>.
    /// </summary>
    SwitchPromptMode,
}
