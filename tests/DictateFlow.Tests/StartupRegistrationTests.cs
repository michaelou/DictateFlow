using DictateFlow.Core.Services.Startup;
using Microsoft.Extensions.Logging.Abstractions;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="StartupRegistration"/> over a fake <see cref="IRunKeyStore"/>:
/// set/remove, honest failure reporting and the startup reconciliation of stale entries.
/// </summary>
public sealed class StartupRegistrationTests
{
    private const string ExePath = @"C:\Apps\DictateFlow\DictateFlow.App.exe";

    /// <summary>In-memory <see cref="IRunKeyStore"/>; optionally throws to simulate denied registry access.</summary>
    private sealed class FakeRunKeyStore : IRunKeyStore
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool ThrowOnWrite { get; set; }

        public string? GetValue(string valueName)
            => Values.TryGetValue(valueName, out var value) ? value : null;

        public void SetValue(string valueName, string value)
        {
            if (ThrowOnWrite)
            {
                throw new UnauthorizedAccessException("Registry access is denied.");
            }

            Values[valueName] = value;
        }

        public void DeleteValue(string valueName)
        {
            if (ThrowOnWrite)
            {
                throw new UnauthorizedAccessException("Registry access is denied.");
            }

            Values.Remove(valueName);
        }
    }

    private static StartupRegistration CreateRegistration(FakeRunKeyStore store)
        => new(store, NullLogger<StartupRegistration>.Instance, ExePath);

    [Fact]
    public void IsEnabled_ReflectsTheStoredValue()
    {
        var store = new FakeRunKeyStore();
        var registration = CreateRegistration(store);

        Assert.False(registration.IsEnabled());

        store.Values[StartupRegistration.ValueName] = "whatever";
        Assert.True(registration.IsEnabled());
    }

    [Fact]
    public void TrySetEnabled_True_WritesTheQuotedExecutablePath()
    {
        var store = new FakeRunKeyStore();

        Assert.True(CreateRegistration(store).TrySetEnabled(true));

        Assert.Equal($"\"{ExePath}\"", store.Values[StartupRegistration.ValueName]);
    }

    [Fact]
    public void TrySetEnabled_False_RemovesTheValue()
    {
        var store = new FakeRunKeyStore();
        store.Values[StartupRegistration.ValueName] = $"\"{ExePath}\"";

        Assert.True(CreateRegistration(store).TrySetEnabled(false));

        Assert.Empty(store.Values);
    }

    [Fact]
    public void TrySetEnabled_DeniedWrite_ReturnsFalse()
    {
        var store = new FakeRunKeyStore { ThrowOnWrite = true };

        Assert.False(CreateRegistration(store).TrySetEnabled(true));
        Assert.Empty(store.Values);
    }

    [Fact]
    public void Reconcile_SettingOnAndValueMissing_RewritesIt()
    {
        var store = new FakeRunKeyStore();

        Assert.True(CreateRegistration(store).Reconcile(shouldBeEnabled: true));

        Assert.Equal($"\"{ExePath}\"", store.Values[StartupRegistration.ValueName]);
    }

    [Fact]
    public void Reconcile_SettingOnAndValueStale_RewritesIt()
    {
        // The exe was moved: the Run entry still points at the old location.
        var store = new FakeRunKeyStore();
        store.Values[StartupRegistration.ValueName] = "\"D:\\OldPlace\\DictateFlow.App.exe\"";

        Assert.True(CreateRegistration(store).Reconcile(shouldBeEnabled: true));

        Assert.Equal($"\"{ExePath}\"", store.Values[StartupRegistration.ValueName]);
    }

    [Fact]
    public void Reconcile_SettingOnAndValueCorrect_ChangesNothing()
    {
        var store = new FakeRunKeyStore();
        store.Values[StartupRegistration.ValueName] = $"\"{ExePath}\"";

        Assert.False(CreateRegistration(store).Reconcile(shouldBeEnabled: true));
    }

    [Fact]
    public void Reconcile_SettingOffAndValueLeftBehind_RemovesIt()
    {
        var store = new FakeRunKeyStore();
        store.Values[StartupRegistration.ValueName] = $"\"{ExePath}\"";

        Assert.True(CreateRegistration(store).Reconcile(shouldBeEnabled: false));

        Assert.Empty(store.Values);
    }

    [Fact]
    public void Reconcile_SettingOffAndNoValue_ChangesNothing()
    {
        Assert.False(CreateRegistration(new FakeRunKeyStore()).Reconcile(shouldBeEnabled: false));
    }

    [Fact]
    public void Reconcile_DeniedWrite_ReturnsFalseInsteadOfThrowing()
    {
        var store = new FakeRunKeyStore { ThrowOnWrite = true };

        Assert.False(CreateRegistration(store).Reconcile(shouldBeEnabled: true));
    }
}
