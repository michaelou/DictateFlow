using DictateFlow.Core.Services.Llm;
using DictateFlow.Core.Services.Output;
using DictateFlow.Core.Services.Transcription;

namespace DictateFlow.Core.Services.Providers;

/// <summary>
/// Looks up the providers registered through the
/// <see cref="ProviderRegistrationExtensions"/>. The <c>Resolve*()</c> overloads read the
/// active provider name from <c>ActiveProviders</c> in settings on every call, so a settings
/// change applies to the very next resolve — no restart required.
/// </summary>
public interface IProviderRegistry
{
    /// <summary>Gets the registered provider names of one kind, in registration order.</summary>
    /// <param name="kind">The provider slot to enumerate.</param>
    IReadOnlyList<string> GetNames(ProviderKind kind);

    /// <summary>
    /// Resolves a provider by its registered name (case-insensitive).
    /// <typeparamref name="T"/> must be the provider interface matching
    /// <paramref name="kind"/> (e.g. <see cref="ITranscriptionProvider"/> for
    /// <see cref="ProviderKind.Transcription"/>).
    /// </summary>
    /// <typeparam name="T">The provider interface of <paramref name="kind"/>.</typeparam>
    /// <param name="kind">The provider slot to resolve from.</param>
    /// <param name="name">The registered provider name.</param>
    /// <exception cref="ProviderException">
    /// No provider named <paramref name="name"/> is registered for <paramref name="kind"/>;
    /// the message lists the valid names.
    /// </exception>
    T Resolve<T>(ProviderKind kind, string name) where T : class;

    /// <summary>Resolves the transcription provider named in <c>ActiveProviders.Transcription</c>.</summary>
    /// <exception cref="ProviderException">The configured name is not registered; the message lists the valid names.</exception>
    ITranscriptionProvider ResolveTranscription();

    /// <summary>Resolves the LLM provider named in <c>ActiveProviders.Llm</c>.</summary>
    /// <exception cref="ProviderException">The configured name is not registered; the message lists the valid names.</exception>
    ILLMProvider ResolveLLM();

    /// <summary>Resolves the output provider named in <c>ActiveProviders.Output</c>.</summary>
    /// <exception cref="ProviderException">The configured name is not registered; the message lists the valid names.</exception>
    IOutputProvider ResolveOutput();
}
