using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services;

/// <summary>
/// Loads and persists <see cref="AppSettings"/> as JSON in the application data directory.
/// </summary>
public interface ISettingsService
{
    /// <summary>Gets the settings currently in effect. Never <see langword="null"/>; defaults are used until <see cref="LoadAsync"/> completes.</summary>
    AppSettings Current { get; }

    /// <summary>Raised after settings have been loaded from or saved to disk.</summary>
    event EventHandler<AppSettings>? SettingsChanged;

    /// <summary>
    /// Loads settings from disk. Falls back to defaults (without throwing) when the file
    /// is missing or unreadable.
    /// </summary>
    /// <param name="cancellationToken">Cancels the pending I/O.</param>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists <see cref="Current"/> to disk as indented JSON.</summary>
    /// <param name="cancellationToken">Cancels the pending I/O.</param>
    Task SaveAsync(CancellationToken cancellationToken = default);
}
