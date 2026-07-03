using DictateFlow.Core.Services.Output;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Samples.NullOutput;

/// <summary>
/// The minimal <see cref="IOutputProvider"/>: logs the confirmed text and delivers it
/// nowhere. Exists to demonstrate (and continuously prove, via tests) the M7 extensibility
/// claim — adding a provider to DictateFlow is this class plus the single
/// <c>services.AddOutputProvider&lt;NullOutputProvider&gt;("Null")</c> line in the App
/// bootstrap; the settings dropdown, registry resolution and pipeline pick it up with no
/// other changes.
/// </summary>
public sealed class NullOutputProvider : IOutputProvider
{
    /// <summary>The name this provider is registered under.</summary>
    public const string RegistrationName = "Null";

    private readonly ILogger<NullOutputProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="NullOutputProvider"/> class.</summary>
    /// <param name="logger">Receives the discarded text.</param>
    public NullOutputProvider(ILogger<NullOutputProvider> logger)
    {
        _logger = logger;
    }

    /// <summary>Gets the text of the most recent <see cref="OutputAsync"/> call, for tests and demos.</summary>
    public string? LastText { get; private set; }

    /// <inheritdoc />
    public string Name => RegistrationName;

    /// <inheritdoc />
    public Task OutputAsync(string text)
    {
        LastText = text;
        _logger.LogInformation("Null output provider swallowed {CharCount} characters", text.Length);
        return Task.CompletedTask;
    }
}
