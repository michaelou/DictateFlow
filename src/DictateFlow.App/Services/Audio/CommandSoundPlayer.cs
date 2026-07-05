using System.IO;
using DictateFlow.App.Services.Commands;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace DictateFlow.App.Services.Audio;

/// <summary>
/// Plays the short bundled voice-command cues (recognized / success / failure) through NAudio.
/// Each cue is a small <c>.wav</c> shipped in <c>Resources\Sounds\</c>; playback is fire-and-forget
/// and every failure is swallowed — an unavailable audio device must never affect command handling.
/// </summary>
public sealed class CommandSoundPlayer : ICommandSoundPlayer
{
    private static readonly IReadOnlyDictionary<CommandSound, string> Files = new Dictionary<CommandSound, string>
    {
        [CommandSound.Recognized] = "command-recognized.wav",
        [CommandSound.Success] = "command-success.wav",
        [CommandSound.Failure] = "command-failure.wav",
    };

    private readonly ILogger<CommandSoundPlayer> _logger;
    private readonly Dictionary<CommandSound, byte[]> _cache = [];
    private readonly object _gate = new();

    /// <summary>Initializes a new instance of the <see cref="CommandSoundPlayer"/> class.</summary>
    /// <param name="logger">Receives diagnostic output.</param>
    public CommandSoundPlayer(ILogger<CommandSoundPlayer> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void Play(CommandSound sound)
    {
        try
        {
            var bytes = Load(sound);
            if (bytes is null)
            {
                return;
            }

            // Own the reader and device for the lifetime of one playback, disposing both when it
            // stops. WaveOutEvent raises PlaybackStopped on a pool thread, so nothing blocks here.
            var reader = new WaveFileReader(new MemoryStream(bytes));
            var device = new WaveOutEvent();
            device.PlaybackStopped += (_, _) =>
            {
                device.Dispose();
                reader.Dispose();
            };
            device.Init(reader);
            device.Play();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not play the {Sound} command sound", sound);
        }
    }

    /// <summary>Reads and caches the cue's bytes, or returns <see langword="null"/> when the file is missing.</summary>
    private byte[]? Load(CommandSound sound)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(sound, out var cached))
            {
                return cached;
            }

            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "Sounds", Files[sound]);
            if (!File.Exists(path))
            {
                _logger.LogDebug("Command sound file not found: {Path}", path);
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            _cache[sound] = bytes;
            return bytes;
        }
    }
}
