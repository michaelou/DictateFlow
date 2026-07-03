using DictateFlow.Core.Services.Updates;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="ReleaseVersion"/>: tolerant parsing of tags/versions and the
/// strictly-newer comparison that drives the "update available" decision.
/// </summary>
public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("0.1.0", 0, 1, 0)]
    [InlineData("v0.1.0", 0, 1, 0)]
    [InlineData("V2.3.4", 2, 3, 4)]
    [InlineData("0.2.0-rc.1", 0, 2, 0)]
    [InlineData("1.0.0+build.7", 1, 0, 0)]
    [InlineData("  v1.2  ", 1, 2, 0)]
    [InlineData("3", 3, 0, 0)]
    public void TryParse_ExtractsNumericCore(string raw, int major, int minor, int build)
    {
        Assert.True(ReleaseVersion.TryParse(raw, out var version));
        Assert.Equal(new Version(major, minor, build), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    [InlineData("v")]
    public void TryParse_ReturnsFalseForUnparseable(string? raw)
    {
        Assert.False(ReleaseVersion.TryParse(raw, out _));
    }

    [Theory]
    [InlineData("v0.2.0", "0.1.0")]
    [InlineData("0.1.1", "0.1.0")]
    [InlineData("1.0.0", "0.9.9")]
    public void IsNewer_TrueWhenLatestGreater(string latest, string current)
    {
        Assert.True(ReleaseVersion.IsNewer(latest, current));
    }

    [Theory]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData("v0.1.0", "0.2.0")]
    [InlineData("0.9.0", "1.0.0")]
    [InlineData("v0.2.0", "0.2.0-beta")] // numeric cores are equal → not newer
    public void IsNewer_FalseWhenNotStrictlyGreater(string latest, string current)
    {
        Assert.False(ReleaseVersion.IsNewer(latest, current));
    }

    [Fact]
    public void IsNewer_FalseWhenCurrentUnparseable()
    {
        // An unreadable installed version must never trigger a false "update available".
        Assert.False(ReleaseVersion.IsNewer("1.0.0", "unknown"));
    }
}
