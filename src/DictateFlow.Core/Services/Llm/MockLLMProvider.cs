using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Providers;

namespace DictateFlow.Core.Services.Llm;

/// <summary>Configuration section (<c>Providers.Llm.Mock</c>) of <see cref="MockLLMProvider"/>.</summary>
public sealed class MockLlmConfig
{
    /// <summary>Gets or sets the artificial processing delay in milliseconds.</summary>
    public int DelayMs { get; set; } = 300;
}

/// <summary>
/// Fake <see cref="ILLMProvider"/> that prefixes the transcript after an optional delay, so
/// the whole dictation flow is demoable without any cloud service. Reads its
/// <see cref="MockLlmConfig"/> section on every call, so edits apply live.
/// </summary>
public sealed class MockLLMProvider : ILLMProvider
{
    /// <summary>The name this provider is registered and configured under.</summary>
    public const string RegistrationName = "Mock";

    /// <summary>The prefix prepended to the transcript, marking the mock enhancement.</summary>
    public const string Prefix = "[enhanced] ";

    private readonly IProviderConfigReader _configReader;

    /// <summary>Initializes a new instance of the <see cref="MockLLMProvider"/> class.</summary>
    /// <param name="configReader">Supplies the <c>Providers.Llm.Mock</c> section, read per call.</param>
    public MockLLMProvider(IProviderConfigReader configReader)
    {
        _configReader = configReader;
    }

    /// <inheritdoc />
    public async Task<string> ProcessAsync(PromptContext context, CancellationToken cancellationToken)
    {
        var config = _configReader.GetConfig<MockLlmConfig>(ProviderKind.Llm, RegistrationName);
        if (config.DelayMs > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(config.DelayMs), cancellationToken).ConfigureAwait(false);
        }

        return Prefix + context.Transcript;
    }
}
