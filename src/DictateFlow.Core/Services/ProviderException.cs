namespace DictateFlow.Core.Services;

/// <summary>
/// Thrown by providers (speech, LLM, …) for user-actionable failures such as a bad API key,
/// an unreachable endpoint or a timeout. The message is safe to show to the user; providers
/// never let raw transport exceptions escape.
/// </summary>
public class ProviderException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ProviderException"/> class.</summary>
    /// <param name="providerName">Name of the provider that failed.</param>
    /// <param name="message">User-presentable description of the failure.</param>
    /// <param name="isConfigurationError">Whether the user should be pointed to Settings.</param>
    public ProviderException(string providerName, string message, bool isConfigurationError = false)
        : base(message)
    {
        ProviderName = providerName;
        IsConfigurationError = isConfigurationError;
    }

    /// <summary>Initializes a new instance of the <see cref="ProviderException"/> class.</summary>
    /// <param name="providerName">Name of the provider that failed.</param>
    /// <param name="message">User-presentable description of the failure.</param>
    /// <param name="innerException">The underlying transport or parsing exception.</param>
    /// <param name="isConfigurationError">Whether the user should be pointed to Settings.</param>
    public ProviderException(string providerName, string message, Exception innerException, bool isConfigurationError = false)
        : base(message, innerException)
    {
        ProviderName = providerName;
        IsConfigurationError = isConfigurationError;
    }

    /// <summary>Gets the name of the provider that raised the failure.</summary>
    public string ProviderName { get; }

    /// <summary>
    /// Gets a value indicating whether the failure is caused by configuration (bad key,
    /// bad endpoint) — <see langword="true"/> means the UI should point the user to Settings.
    /// </summary>
    public bool IsConfigurationError { get; }
}
