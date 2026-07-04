using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Commands;
using Microsoft.Extensions.Logging.Abstractions;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for the built-in launch actions — <see cref="ProcessStartAction"/>,
/// <see cref="OpenUrlAction"/> and <see cref="OpenFolderAction"/> — covering the safety
/// contract: spoken arguments never change the executable, URL scheme/host or folder target;
/// placeholder-without-argument fails safely; failures return a curated result, never a throw.
/// </summary>
public sealed class LaunchActionTests
{
    private readonly FakeProcessLauncher _launcher = new();

    private static CommandContext Context(
        string actionType, string value, string arguments = "", string argument = "", string name = "Test")
        => new(name, actionType, value, arguments, argument, $"hey john {name}", DateTime.UtcNow);

    // ---- ProcessStartAction ----

    private ProcessStartAction NewProcessStart() => new(_launcher, NullLogger<ProcessStartAction>.Instance);

    [Fact]
    public async Task ProcessStart_LaunchesExecutableWithShellExecute()
    {
        var result = await NewProcessStart().ExecuteAsync(
            Context(ProcessStartAction.RegistrationName, "notepad.exe"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("notepad.exe", _launcher.Single.FileName);
        Assert.True(_launcher.Single.UseShellExecute);
        Assert.Equal("", _launcher.Single.Arguments);
    }

    [Fact]
    public async Task ProcessStart_ExpandsEnvironmentVariablesInTheExecutablePath()
    {
        var result = await NewProcessStart().ExecuteAsync(
            Context(ProcessStartAction.RegistrationName, @"%WINDIR%\notepad.exe"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(Environment.ExpandEnvironmentVariables(@"%WINDIR%\notepad.exe"), _launcher.Single.FileName);
    }

    [Fact]
    public async Task ProcessStart_SubstitutesSpokenArgumentAsOneQuotedArgument()
    {
        var result = await NewProcessStart().ExecuteAsync(
            Context(ProcessStartAction.RegistrationName, "notepad.exe", "\"{{Argument}}\"", "meeting notes.txt"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("\"meeting notes.txt\"", _launcher.Single.Arguments);
    }

    [Fact]
    public async Task ProcessStart_EscapesEmbeddedQuotesSoTheArgumentCannotBreakOut()
    {
        var result = await NewProcessStart().ExecuteAsync(
            Context(ProcessStartAction.RegistrationName, "app.exe", "\"{{Argument}}\"", "a\" --evil"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("\"a\\\" --evil\"", _launcher.Single.Arguments);
    }

    [Fact]
    public async Task ProcessStart_PlaceholderButNoSpokenArgument_FailsSafely()
    {
        var result = await NewProcessStart().ExecuteAsync(
            Context(ProcessStartAction.RegistrationName, "notepad.exe", "\"{{Argument}}\"", argument: ""),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("spoken argument", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_launcher.Started);
    }

    [Fact]
    public async Task ProcessStart_NoPlaceholder_IgnoresSpokenArgument()
    {
        var result = await NewProcessStart().ExecuteAsync(
            Context(ProcessStartAction.RegistrationName, "notepad.exe", "/A", "some spoken words"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("/A", _launcher.Single.Arguments);
    }

    [Fact]
    public async Task ProcessStart_LaunchFailure_ReturnsFailedResultWithoutThrowing()
    {
        _launcher.ThrowOnStart = new InvalidOperationException("no such file");

        var result = await NewProcessStart().ExecuteAsync(
            Context(ProcessStartAction.RegistrationName, "does-not-exist.exe"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("does-not-exist.exe", result.Message);
    }

    [Fact]
    public async Task ProcessStart_PlaceholderInExecutable_NeverExecutes()
    {
        var result = await NewProcessStart().ExecuteAsync(
            Context(ProcessStartAction.RegistrationName, "{{Argument}}.exe", argument: "calc"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(_launcher.Started);
    }

    [Theory]
    [InlineData("", "ProcessStart needs")]
    [InlineData("{{Argument}}.exe", "must not contain")]
    public void ProcessStart_Validate_RejectsBadDefinitions(string value, string expectedFragment)
    {
        var error = NewProcessStart().Validate(
            new CommandDefinition { ActionType = ProcessStartAction.RegistrationName, ActionValue = value });

        Assert.NotNull(error);
        Assert.Contains(expectedFragment, error);
    }

    [Fact]
    public void ProcessStart_Validate_AcceptsPlaceholderInArgumentsOnly()
    {
        var error = NewProcessStart().Validate(new CommandDefinition
        {
            ActionType = ProcessStartAction.RegistrationName,
            ActionValue = "notepad.exe",
            ActionArguments = "\"{{Argument}}\"",
        });

        Assert.Null(error);
    }

    // ---- OpenUrlAction ----

    private OpenUrlAction NewOpenUrl() => new(_launcher, NullLogger<OpenUrlAction>.Instance);

    [Fact]
    public async Task OpenUrl_OpensConfiguredUrl()
    {
        var result = await NewOpenUrl().ExecuteAsync(
            Context(OpenUrlAction.RegistrationName, "https://example.com/"), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("https://example.com/", _launcher.Single.FileName);
        Assert.True(_launcher.Single.UseShellExecute);
    }

    [Fact]
    public async Task OpenUrl_UrlEncodesTheSpokenArgument()
    {
        var result = await NewOpenUrl().ExecuteAsync(
            Context(OpenUrlAction.RegistrationName, "https://www.google.com/search?q={{Argument}}", argument: "whisper streaming"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("https://www.google.com/search?q=whisper%20streaming", _launcher.Single.FileName);
    }

    [Fact]
    public async Task OpenUrl_EncodedArgumentCannotInjectQueryOrHost()
    {
        var result = await NewOpenUrl().ExecuteAsync(
            Context(OpenUrlAction.RegistrationName, "https://www.google.com/search?q={{Argument}}", argument: "a&b=c#@evil.com"),
            CancellationToken.None);

        Assert.True(result.Success);
        var url = new Uri(_launcher.Single.FileName);
        Assert.Equal("www.google.com", url.Host);
        Assert.Equal("q=a%26b%3Dc%23%40evil.com", url.Query.TrimStart('?'));
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/file")]
    [InlineData("notaurl")]
    public async Task OpenUrl_NonHttpScheme_IsRefused(string url)
    {
        var result = await NewOpenUrl().ExecuteAsync(
            Context(OpenUrlAction.RegistrationName, url), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(_launcher.Started);
    }

    [Fact]
    public async Task OpenUrl_MaliciousTemplateWithPlaceholder_IsRefused()
    {
        // Even carrying the placeholder, a non-http(s) template never opens.
        var result = await NewOpenUrl().ExecuteAsync(
            Context(OpenUrlAction.RegistrationName, "javascript:alert('{{Argument}}')", argument: "hi"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(_launcher.Started);
    }

    [Fact]
    public async Task OpenUrl_PlaceholderButNoSpokenArgument_FailsSafely()
    {
        var result = await NewOpenUrl().ExecuteAsync(
            Context(OpenUrlAction.RegistrationName, "https://www.google.com/search?q={{Argument}}", argument: ""),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("spoken argument", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_launcher.Started);
    }

    [Theory]
    [InlineData("https://example.com/", null)]
    [InlineData("http://example.com/search?q={{Argument}}", null)]
    [InlineData("file:///etc/passwd", "http")]
    [InlineData("", "http")]
    public void OpenUrl_Validate_EnforcesHttpOnly(string value, string? expectedFragment)
    {
        var error = NewOpenUrl().Validate(
            new CommandDefinition { ActionType = OpenUrlAction.RegistrationName, ActionValue = value });

        if (expectedFragment is null)
        {
            Assert.Null(error);
        }
        else
        {
            Assert.NotNull(error);
        }
    }

    // ---- OpenFolderAction ----

    private OpenFolderAction NewOpenFolder() => new(_launcher, NullLogger<OpenFolderAction>.Instance);

    [Fact]
    public async Task OpenFolder_OpensExistingDirectory_ViaEnvironmentVariable()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DictateFlowFolderTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("DICTATEFLOW_TEST_DIR", dir);
        try
        {
            var result = await NewOpenFolder().ExecuteAsync(
                Context(OpenFolderAction.RegistrationName, "%DICTATEFLOW_TEST_DIR%"), CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal(dir, _launcher.Single.FileName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DICTATEFLOW_TEST_DIR", null);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task OpenFolder_ResolvesWellKnownName()
    {
        var expected = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        var result = await NewOpenFolder().ExecuteAsync(
            Context(OpenFolderAction.RegistrationName, "Documents"), CancellationToken.None);

        if (!string.IsNullOrEmpty(expected) && Directory.Exists(expected))
        {
            Assert.True(result.Success);
            Assert.Equal(expected, _launcher.Single.FileName);
        }
        else
        {
            Assert.False(result.Success);
        }
    }

    [Fact]
    public async Task OpenFolder_NonExistentDirectory_Fails()
    {
        var missing = Path.Combine(Path.GetTempPath(), "DictateFlowMissing", Guid.NewGuid().ToString("N"));

        var result = await NewOpenFolder().ExecuteAsync(
            Context(OpenFolderAction.RegistrationName, missing), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(_launcher.Started);
    }

    [Fact]
    public async Task OpenFolder_PlaceholderInValue_NeverExecutes()
    {
        var result = await NewOpenFolder().ExecuteAsync(
            Context(OpenFolderAction.RegistrationName, @"C:\{{Argument}}", argument: "Windows"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(_launcher.Started);
    }

    [Theory]
    [InlineData("", "needs a folder")]
    [InlineData(@"C:\{{Argument}}", "does not support")]
    public void OpenFolder_Validate_RejectsBadDefinitions(string value, string expectedFragment)
    {
        var error = NewOpenFolder().Validate(
            new CommandDefinition { ActionType = OpenFolderAction.RegistrationName, ActionValue = value });

        Assert.NotNull(error);
        Assert.Contains(expectedFragment, error);
    }
}
