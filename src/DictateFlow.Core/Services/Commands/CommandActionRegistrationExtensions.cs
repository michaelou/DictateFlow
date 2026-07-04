using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DictateFlow.Core.Services.Commands;

/// <summary>
/// Named command action registration — the single mechanism through which action types
/// become executable by voice commands, mirroring the provider registration extensions.
/// Each call registers the implementation as a keyed service under <c>actionType</c> and
/// records it in the <see cref="CommandActionCatalog"/> so the resolver (and settings UI)
/// can enumerate and resolve it. Adding an action type to the application is exactly one of
/// these calls in the App DI bootstrap.
/// </summary>
public static class CommandActionRegistrationExtensions
{
    /// <summary>
    /// Registers <typeparamref name="T"/> as the command action named
    /// <paramref name="actionType"/>.
    /// </summary>
    /// <typeparam name="T">The action implementation.</typeparam>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="actionType">The action type name used in command definitions; unique, case-insensitive.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <remarks>
    /// <typeparamref name="T"/> itself is registered as a singleton unless it already has a
    /// registration — actions with special lifetimes keep theirs by registering it
    /// <b>before</b> this call.
    /// </remarks>
    public static IServiceCollection AddCommandAction<T>(this IServiceCollection services, string actionType)
        where T : class, ICommandAction
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionType);

        GetOrCreateCatalog(services).Add(actionType, typeof(T));
        services.TryAddSingleton<T>();
        services.AddKeyedTransient<ICommandAction>(actionType, (sp, _) => sp.GetRequiredService<T>());
        return services;
    }

    /// <summary>
    /// Finds the catalog instance already registered on <paramref name="services"/>, or
    /// creates and registers one. Using an instance registration lets registration-time
    /// calls populate it before any container is built.
    /// </summary>
    private static CommandActionCatalog GetOrCreateCatalog(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(CommandActionCatalog))
            ?.ImplementationInstance as CommandActionCatalog;
        if (existing is not null)
        {
            return existing;
        }

        var catalog = new CommandActionCatalog();
        services.AddSingleton(catalog);
        return catalog;
    }
}
