using System.Text.Json.Nodes;

namespace DictateFlow.Core.Services;

/// <summary>
/// A settings-file migration applied by <see cref="SettingsService"/> at load time, before
/// deserialization. Migrations work on the raw JSON (so unknown fields survive) and must be
/// idempotent — re-running on already-migrated content must change nothing. Implementations
/// live in the App composition root, which is the one place allowed to know concrete
/// provider names.
/// </summary>
public interface ISettingsMigration
{
    /// <summary>
    /// Migrates <paramref name="settings"/> in place when it has an outdated shape.
    /// </summary>
    /// <param name="settings">The parsed <c>settings.json</c> root object.</param>
    /// <returns><see langword="true"/> when the content was modified and should be written back.</returns>
    bool TryMigrate(JsonObject settings);
}
