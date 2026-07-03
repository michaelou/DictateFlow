using System.Text.Json;
using System.Text.Json.Nodes;
using DictateFlow.Core.Models;
using Microsoft.Extensions.Logging;

namespace DictateFlow.Core.Services.Transfer;

/// <summary>
/// Default <see cref="ISettingsTransfer"/> implementation. Redaction walks the whole JSON
/// tree and blanks every property named <c>ApiKey</c> (case-insensitive), so provider config
/// sections Core does not know about are covered too.
/// </summary>
public sealed class SettingsTransfer : ISettingsTransfer
{
    private readonly IEnumerable<ISettingsMigration> _migrations;
    private readonly ILogger<SettingsTransfer> _logger;

    /// <summary>Initializes a new instance of the <see cref="SettingsTransfer"/> class.</summary>
    /// <param name="migrations">Schema migrations applied to imported files, same as at load time.</param>
    /// <param name="logger">Receives diagnostic output.</param>
    public SettingsTransfer(IEnumerable<ISettingsMigration> migrations, ILogger<SettingsTransfer> logger)
    {
        _migrations = migrations;
        _logger = logger;
    }

    /// <inheritdoc />
    public string ExportJson(AppSettings settings, bool includeSecrets)
    {
        var root = JsonSerializer.SerializeToNode(settings, SettingsService.SerializerOptions)!.AsObject();
        if (!includeSecrets)
        {
            RedactSecrets(root);
        }

        return root.ToJsonString(SettingsService.SerializerOptions);
    }

    /// <inheritdoc />
    public AppSettings ParseImport(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject root)
        {
            throw new JsonException("The file does not contain a JSON settings object.");
        }

        foreach (var migration in _migrations)
        {
            if (migration.TryMigrate(root))
            {
                _logger.LogInformation("Imported settings were migrated to the current schema");
            }
        }

        return root.Deserialize<AppSettings>(SettingsService.SerializerOptions)
            ?? throw new JsonException("The file does not contain a JSON settings object.");
    }

    /// <summary>Replaces every <c>ApiKey</c> string property in the tree with an empty string.</summary>
    /// <param name="node">The JSON (sub)tree to redact in place.</param>
    public static void RedactSecrets(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var propertyName in obj.Select(p => p.Key).ToList())
                {
                    if (string.Equals(propertyName, "ApiKey", StringComparison.OrdinalIgnoreCase))
                    {
                        obj[propertyName] = "";
                    }
                    else
                    {
                        RedactSecrets(obj[propertyName]);
                    }
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    RedactSecrets(item);
                }

                break;
        }
    }
}
