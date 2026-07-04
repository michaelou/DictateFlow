using DictateFlow.Core.Models;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// The on-disk JSON shape of a user command file, with the action nested under an
/// <c>action</c> object (<c>{ "type": …, "value": …, "arguments": … }</c>). Kept separate from
/// <see cref="CommandDefinition"/> so the stored schema can stay friendly and stable while the
/// flat runtime model is what the matcher and actions consume. Every property is nullable so a
/// missing field is a validation decision, not a deserialization surprise.
/// </summary>
public sealed class CommandDefinitionFile
{
    /// <summary>Gets or sets the display name (JSON <c>name</c>).</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets whether the command participates in matching (JSON <c>enabled</c>); defaults to <see langword="true"/> when absent.</summary>
    public bool? Enabled { get; set; }

    /// <summary>Gets or sets the trigger phrases (JSON <c>phrases</c>).</summary>
    public List<string>? Phrases { get; set; }

    /// <summary>Gets or sets the nested action configuration (JSON <c>action</c>).</summary>
    public CommandActionFile? Action { get; set; }

    /// <summary>Gets or sets whether the command requires confirmation (JSON <c>requiresConfirmation</c>).</summary>
    public bool? RequiresConfirmation { get; set; }

    /// <summary>
    /// Projects the file into the flat runtime <see cref="CommandDefinition"/>. Trims and drops
    /// blank phrases; falls back to the first phrase for a missing name so a definition always
    /// has something to display.
    /// </summary>
    public CommandDefinition ToDefinition()
    {
        var phrases = (Phrases ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToList();
        return new CommandDefinition
        {
            Name = string.IsNullOrWhiteSpace(Name) ? phrases.FirstOrDefault() ?? "" : Name.Trim(),
            Enabled = Enabled ?? true,
            Phrases = phrases,
            ActionType = Action?.Type?.Trim() ?? "",
            ActionValue = Action?.Value ?? "",
            ActionArguments = Action?.Arguments ?? "",
            RequiresConfirmation = RequiresConfirmation ?? false,
        };
    }
}

/// <summary>The nested <c>action</c> object of a <see cref="CommandDefinitionFile"/>.</summary>
public sealed class CommandActionFile
{
    /// <summary>Gets or sets the action type name (JSON <c>type</c>, e.g. <c>OpenUrl</c>).</summary>
    public string? Type { get; set; }

    /// <summary>Gets or sets the configured payload (JSON <c>value</c>, e.g. an executable, URL or folder).</summary>
    public string? Value { get; set; }

    /// <summary>Gets or sets the optional arguments template (JSON <c>arguments</c>).</summary>
    public string? Arguments { get; set; }
}
