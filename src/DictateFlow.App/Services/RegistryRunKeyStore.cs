using DictateFlow.Core.Services.Startup;
using Microsoft.Win32;

namespace DictateFlow.App.Services;

/// <summary>
/// Production <see cref="IRunKeyStore"/> over
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>. Access failures propagate as
/// exceptions; <see cref="Core.Services.Startup.StartupRegistration"/> turns them into
/// <see langword="false"/> results.
/// </summary>
public sealed class RegistryRunKeyStore : IRunKeyStore
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <inheritdoc />
    public string? GetValue(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(valueName) as string;
    }

    /// <inheritdoc />
    public void SetValue(string valueName, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    /// <inheritdoc />
    public void DeleteValue(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
