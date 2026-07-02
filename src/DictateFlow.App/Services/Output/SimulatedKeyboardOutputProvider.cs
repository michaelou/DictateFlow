using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Output;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.Services.Output;

/// <summary>
/// Delivers text by typing it into the foreground window with simulated
/// <c>KEYEVENTF_UNICODE</c> keystrokes. Slower than clipboard paste but works in
/// applications that block programmatic paste, and never touches the user's clipboard.
/// The injection loop runs off the UI thread.
/// </summary>
public sealed class SimulatedKeyboardOutputProvider : IOutputProvider
{
    /// <summary>Events per <c>SendInput</c> call; small chunks keep slow target apps from dropping input.</summary>
    private const int EventsPerChunk = 20;

    /// <summary>Pause between chunks so the target application's message queue keeps up.</summary>
    private static readonly TimeSpan InterChunkDelay = TimeSpan.FromMilliseconds(8);

    private readonly ILogger<SimulatedKeyboardOutputProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="SimulatedKeyboardOutputProvider"/> class.</summary>
    /// <param name="logger">Receives diagnostic output.</param>
    public SimulatedKeyboardOutputProvider(ILogger<SimulatedKeyboardOutputProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => OutputProviderNames.SimulatedKeyboard;

    /// <inheritdoc />
    public async Task OutputAsync(string text)
    {
        var events = KeyboardInputMapper.MapText(text);
        if (events.Count == 0)
        {
            return;
        }

        await Task.Run(async () =>
        {
            uint accepted = 0;
            foreach (var chunk in KeyboardInputMapper.Chunk(events, EventsPerChunk))
            {
                accepted += KeyboardInjector.Send(chunk);
                await Task.Delay(InterChunkDelay).ConfigureAwait(false);
            }

            _logger.LogDebug(
                "Simulated typing finished: {AcceptedEvents}/{TotalEvents} events accepted", accepted, events.Count);
        }).ConfigureAwait(false);
    }
}
