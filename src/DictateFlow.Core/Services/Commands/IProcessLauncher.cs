using System.Diagnostics;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// Thin seam over <see cref="Process.Start(ProcessStartInfo)"/> so the launch actions
/// (<c>ProcessStart</c>, <c>OpenUrl</c>, <c>OpenFolder</c>) can be unit-tested without spawning
/// real processes or opening browsers/Explorer. The production implementation is a direct
/// pass-through; tests substitute a fake that records what it was asked to start.
/// </summary>
public interface IProcessLauncher
{
    /// <summary>Starts the process described by <paramref name="startInfo"/>.</summary>
    /// <param name="startInfo">The fully-prepared start info; actions always set <c>UseShellExecute = true</c>.</param>
    /// <exception cref="System.Exception">Propagates the launch failure; callers catch and report it.</exception>
    void Start(ProcessStartInfo startInfo);
}

/// <summary>Production <see cref="IProcessLauncher"/> that starts the process directly.</summary>
public sealed class ProcessLauncher : IProcessLauncher
{
    /// <inheritdoc />
    public void Start(ProcessStartInfo startInfo) => Process.Start(startInfo);
}
