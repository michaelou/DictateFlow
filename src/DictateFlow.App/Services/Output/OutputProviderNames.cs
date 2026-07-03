namespace DictateFlow.App.Services.Output;

/// <summary>The names the built-in output providers are registered under.</summary>
public static class OutputProviderNames
{
    /// <summary>Places the text on the clipboard and sends Ctrl+V to the target application.</summary>
    public const string ClipboardPaste = "ClipboardPaste";

    /// <summary>Types the text into the target application with simulated keystrokes.</summary>
    public const string SimulatedKeyboard = "SimulatedKeyboard";
}
