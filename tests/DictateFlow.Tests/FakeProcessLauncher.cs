using System.Diagnostics;
using DictateFlow.Core.Services.Commands;

namespace DictateFlow.Tests;

/// <summary>
/// Test <see cref="IProcessLauncher"/> that records every <see cref="ProcessStartInfo"/> it is
/// asked to start instead of spawning a real process, and can be told to throw to simulate a
/// launch failure (missing executable, unreachable target).
/// </summary>
public sealed class FakeProcessLauncher : IProcessLauncher
{
    /// <summary>Gets the start infos captured, in call order.</summary>
    public List<ProcessStartInfo> Started { get; } = [];

    /// <summary>Gets or sets an exception to throw on the next <see cref="Start"/>, simulating a launch failure.</summary>
    public Exception? ThrowOnStart { get; set; }

    /// <summary>Gets the single captured start info, asserting exactly one launch happened.</summary>
    public ProcessStartInfo Single => Started.Count == 1
        ? Started[0]
        : throw new InvalidOperationException($"Expected exactly one launch, saw {Started.Count}.");

    /// <inheritdoc />
    public void Start(ProcessStartInfo startInfo)
    {
        if (ThrowOnStart is { } ex)
        {
            throw ex;
        }

        Started.Add(startInfo);
    }
}
