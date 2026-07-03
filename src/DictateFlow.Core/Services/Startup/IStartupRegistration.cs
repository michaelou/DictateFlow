namespace DictateFlow.Core.Services.Startup;

/// <summary>
/// Manages the launch-with-Windows registration (the <c>DictateFlow</c> value under the
/// per-user Run registry key). All members are honest about failure: a denied registry
/// write returns <see langword="false"/> instead of throwing, so the UI can uncheck the
/// option and tell the user.
/// </summary>
public interface IStartupRegistration
{
    /// <summary>Gets a value indicating whether a Run entry for DictateFlow currently exists.</summary>
    bool IsEnabled();

    /// <summary>
    /// Creates (or removes) the Run entry pointing at the current executable.
    /// </summary>
    /// <param name="enabled"><see langword="true"/> registers, <see langword="false"/> unregisters.</param>
    /// <returns><see langword="false"/> when the registry write failed (e.g. access denied).</returns>
    bool TrySetEnabled(bool enabled);

    /// <summary>
    /// Brings the Run entry in line with the setting: when <paramref name="shouldBeEnabled"/>
    /// is <see langword="true"/> and the entry is missing or points at a stale executable
    /// path (the app was moved), it is rewritten; when <see langword="false"/>, a leftover
    /// entry is removed.
    /// </summary>
    /// <param name="shouldBeEnabled">The persisted <c>General.LaunchAtStartup</c> value.</param>
    /// <returns><see langword="true"/> when the registry was changed.</returns>
    bool Reconcile(bool shouldBeEnabled);
}

/// <summary>
/// Raw access to the values of the per-user Run registry key
/// (<c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>), abstracted so the
/// registration logic is testable without touching the real registry. Implementations may
/// throw on access failures; <see cref="IStartupRegistration"/> translates those into
/// <see langword="false"/> results.
/// </summary>
public interface IRunKeyStore
{
    /// <summary>Gets a string value, or <see langword="null"/> when the value (or the key) does not exist.</summary>
    /// <param name="valueName">The registry value name.</param>
    string? GetValue(string valueName);

    /// <summary>Creates or replaces a string value.</summary>
    /// <param name="valueName">The registry value name.</param>
    /// <param name="value">The command line to store.</param>
    void SetValue(string valueName, string value);

    /// <summary>Removes a value; removing a missing value is not an error.</summary>
    /// <param name="valueName">The registry value name.</param>
    void DeleteValue(string valueName);
}
