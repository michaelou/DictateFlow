namespace DictateFlow.Core.Models;

/// <summary>Well-known values for <see cref="OutputSettings.Mode"/>.</summary>
public static class OutputModes
{
    /// <summary>The final text is delivered to the target application without confirmation.</summary>
    public const string Automatic = "Automatic";

    /// <summary>A preview dialog lets the user edit, confirm or cancel before delivery.</summary>
    public const string Preview = "Preview";
}
