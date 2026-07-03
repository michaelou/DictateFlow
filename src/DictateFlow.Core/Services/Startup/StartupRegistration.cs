using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Startup;

/// <summary>
/// Default <see cref="IStartupRegistration"/> implementation over an
/// <see cref="IRunKeyStore"/>. The stored data is the quoted path of the current executable,
/// so <see cref="Reconcile"/> can detect and repair an entry left behind by a moved
/// installation.
/// </summary>
public sealed class StartupRegistration : IStartupRegistration
{
    /// <summary>The registry value name the registration is stored under.</summary>
    public const string ValueName = "DictateFlow";

    private readonly IRunKeyStore _runKey;
    private readonly ILogger<StartupRegistration> _logger;
    private readonly string _executablePath;

    /// <summary>Initializes a new instance of the <see cref="StartupRegistration"/> class.</summary>
    /// <param name="runKey">The Run key value store (registry in production, fake in tests).</param>
    /// <param name="logger">Receives diagnostic output.</param>
    /// <param name="executablePath">
    /// Path of the executable to register; defaults to the current process image
    /// (<see cref="Environment.ProcessPath"/>). Tests pass an explicit path.
    /// </param>
    public StartupRegistration(IRunKeyStore runKey, ILogger<StartupRegistration> logger, string? executablePath = null)
    {
        _runKey = runKey;
        _logger = logger;
        _executablePath = executablePath ?? Environment.ProcessPath ?? "";
    }

    /// <summary>Gets the value the Run entry is expected to hold: the quoted executable path.</summary>
    private string ExpectedValue => $"\"{_executablePath}\"";

    /// <inheritdoc />
    public bool IsEnabled()
    {
        try
        {
            return _runKey.GetValue(ValueName) is not null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the startup registration");
            return false;
        }
    }

    /// <inheritdoc />
    public bool TrySetEnabled(bool enabled)
    {
        if (enabled && _executablePath.Length == 0)
        {
            _logger.LogWarning("Cannot register startup launch: the executable path is unknown");
            return false;
        }

        try
        {
            if (enabled)
            {
                _runKey.SetValue(ValueName, ExpectedValue);
                _logger.LogInformation("Startup registration created: {Value}", ExpectedValue);
            }
            else
            {
                _runKey.DeleteValue(ValueName);
                _logger.LogInformation("Startup registration removed");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not {Action} the startup registration", enabled ? "create" : "remove");
            return false;
        }
    }

    /// <inheritdoc />
    public bool Reconcile(bool shouldBeEnabled)
    {
        try
        {
            var current = _runKey.GetValue(ValueName);

            if (shouldBeEnabled)
            {
                if (_executablePath.Length == 0 || string.Equals(current, ExpectedValue, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                _runKey.SetValue(ValueName, ExpectedValue);
                _logger.LogInformation(
                    "Startup registration reconciled: {Old} → {New}", current ?? "(missing)", ExpectedValue);
                return true;
            }

            if (current is null)
            {
                return false;
            }

            _runKey.DeleteValue(ValueName);
            _logger.LogInformation("Stale startup registration removed (setting is off)");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not reconcile the startup registration");
            return false;
        }
    }
}
