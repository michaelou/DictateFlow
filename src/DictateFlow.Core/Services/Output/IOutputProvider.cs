namespace DictateFlow.Core.Services.Output;

/// <summary>Delivers final text to the user's target application.</summary>
public interface IOutputProvider
{
    /// <summary>Gets the name this provider is registered and selected under in settings.</summary>
    string Name { get; }

    /// <summary>
    /// Delivers <paramref name="text"/> to the application that currently has focus.
    /// </summary>
    /// <param name="text">The text to deliver.</param>
    /// <exception cref="ProviderException">Delivery failed in a user-actionable way.</exception>
    Task OutputAsync(string text);
}
