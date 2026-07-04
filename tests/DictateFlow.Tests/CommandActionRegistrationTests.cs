using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="CommandActionRegistrationExtensions"/>, the
/// <see cref="CommandActionCatalog"/> and <see cref="CommandActionResolver"/>: the keyed
/// registration round-trip, name uniqueness and the unknown-name rejection.
/// </summary>
public sealed class CommandActionRegistrationTests
{
    private static ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<ILogger<MockCommandAction>>());
        services.AddSingleton<ICommandActionResolver, CommandActionResolver>();
        services.AddCommandAction<MockCommandAction>(MockCommandAction.RegistrationName);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddCommandAction_RegistersInCatalogAndKeyedDi()
    {
        using var provider = BuildProvider();

        var catalog = provider.GetRequiredService<CommandActionCatalog>();
        Assert.Equal(["Mock"], catalog.GetNames());
        Assert.IsType<MockCommandAction>(provider.GetRequiredKeyedService<ICommandAction>("Mock"));
    }

    [Fact]
    public void AddCommandAction_DuplicateName_Throws()
    {
        var services = new ServiceCollection();
        services.AddCommandAction<MockCommandAction>("Mock");

        Assert.Throws<InvalidOperationException>(() => services.AddCommandAction<MockCommandAction>("mock"));
    }

    [Fact]
    public void AddCommandAction_BlankName_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddCommandAction<MockCommandAction>(" "));
    }

    [Theory]
    [InlineData("Mock")]
    [InlineData("mock")]
    [InlineData("MOCK")]
    public void Resolver_ResolvesCaseInsensitively(string actionType)
    {
        using var provider = BuildProvider();
        var resolver = provider.GetRequiredService<ICommandActionResolver>();

        Assert.True(resolver.TryResolve(actionType, out var action));
        Assert.IsType<MockCommandAction>(action);
    }

    [Theory]
    [InlineData("PowerShellScript")]
    [InlineData("")]
    [InlineData("  ")]
    public void Resolver_UnknownOrBlankName_FailsTheLookup(string actionType)
    {
        using var provider = BuildProvider();
        var resolver = provider.GetRequiredService<ICommandActionResolver>();

        Assert.False(resolver.TryResolve(actionType, out var action));
        Assert.Null(action);
    }

    [Fact]
    public async Task MockCommandAction_ReportsSuccess()
    {
        var action = new MockCommandAction(Mock.Of<ILogger<MockCommandAction>>());
        var context = new CommandContext("Test Command", "Mock", "", "", "", "test command", DateTime.UtcNow);

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Test Command", result.Message);
    }
}
