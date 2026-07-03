using System.Text.Json;
using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Transfer;

/// <summary>
/// Serializes settings for export and parses settings files for import. Export can redact
/// secrets (API keys become empty strings — the default); import runs the same schema
/// migrations as the settings service, so pre-M7 files import cleanly.
/// </summary>
public interface ISettingsTransfer
{
    /// <summary>Serializes <paramref name="settings"/> as indented JSON.</summary>
    /// <param name="settings">The settings to export.</param>
    /// <param name="includeSecrets">
    /// When <see langword="false"/> (the safe default), every <c>ApiKey</c> property in the
    /// output is replaced with an empty string.
    /// </param>
    string ExportJson(AppSettings settings, bool includeSecrets);

    /// <summary>
    /// Parses an exported (or hand-written) settings file, applying the registered schema
    /// migrations first.
    /// </summary>
    /// <param name="json">The file content.</param>
    /// <exception cref="JsonException"><paramref name="json"/> is not a valid settings document.</exception>
    AppSettings ParseImport(string json);
}
