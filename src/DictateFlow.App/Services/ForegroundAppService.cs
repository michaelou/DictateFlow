using System.Diagnostics;
using DictateFlow.App.Interop;
using DictateFlow.Core.Services;
using Microsoft.Extensions.Logging;

namespace DictateFlow.App.Services;

/// <summary>
/// Win32 <see cref="IForegroundAppService"/> implementation:
/// <c>GetForegroundWindow</c> → <c>GetWindowThreadProcessId</c> →
/// <see cref="Process.ProcessName"/>. Never throws — an unresolvable foreground window
/// (secure desktop, exited process, no interactive session) captures an empty string.
/// </summary>
public sealed class ForegroundAppService : IForegroundAppService
{
    private readonly ILogger<ForegroundAppService> _logger;
    private volatile string _lastCaptured = "";

    /// <summary>Initializes a new instance of the <see cref="ForegroundAppService"/> class.</summary>
    /// <param name="logger">Receives diagnostic output.</param>
    public ForegroundAppService(ILogger<ForegroundAppService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string LastCaptured => _lastCaptured;

    /// <inheritdoc />
    public string Capture()
    {
        try
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return _lastCaptured = "";
            }

            _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == 0)
            {
                return _lastCaptured = "";
            }

            using var process = Process.GetProcessById((int)processId);
            _lastCaptured = process.ProcessName;
            _logger.LogDebug("Foreground application captured: {ProcessName}", _lastCaptured);
        }
        catch (Exception ex)
        {
            // The process may have exited between the two calls, or access may be denied.
            _logger.LogDebug(ex, "Could not resolve the foreground application");
            _lastCaptured = "";
        }

        return _lastCaptured;
    }
}
