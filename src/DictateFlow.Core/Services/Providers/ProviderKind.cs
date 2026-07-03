namespace DictateFlow.Core.Services.Providers;

/// <summary>The three replaceable provider slots of the dictation pipeline.</summary>
public enum ProviderKind
{
    /// <summary>Speech-to-text providers (<see cref="Transcription.ITranscriptionProvider"/>).</summary>
    Transcription,

    /// <summary>Text-enhancement providers (<see cref="Llm.ILLMProvider"/>).</summary>
    Llm,

    /// <summary>Text-delivery providers (<see cref="Output.IOutputProvider"/>).</summary>
    Output,
}
