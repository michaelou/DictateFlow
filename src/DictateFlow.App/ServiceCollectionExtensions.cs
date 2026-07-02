using DictateFlow.App.Services;
using DictateFlow.App.ViewModels;
using DictateFlow.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DictateFlow.App;

/// <summary>
/// Registers all DictateFlow services so the application and tests can build the same container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all DictateFlow services to <paramref name="services"/>.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    /// <param name="appPaths">
    /// Optional path provider override; tests pass an instance rooted in a temporary
    /// directory. When omitted, the <c>%APPDATA%\DictateFlow\</c> paths are used.
    /// </param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddDictateFlow(this IServiceCollection services, IAppPaths? appPaths = null)
    {
        if (appPaths is null)
        {
            services.AddSingleton<IAppPaths, AppPaths>();
        }
        else
        {
            services.AddSingleton(appPaths);
        }

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();

        services.AddSingleton<IWindowService, WindowService>();
        services.AddSingleton<IShutdownService, ShutdownService>();
        services.AddSingleton<ITrayIconService, TrayIconService>();

        services.AddSingleton<TrayViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services;
    }
}
