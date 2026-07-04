using DictateFlow.Core.Models;
using DictateFlow.Core.Services.Commands;
using Microsoft.Extensions.Logging;
using Moq;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="WakePhraseDetector"/>: transcription noise (casing, punctuation),
/// the disabled states, and verbatim command-text extraction.
/// </summary>
public sealed class WakePhraseDetectorTests
{
    private readonly WakePhraseDetector _detector = new(Mock.Of<ILogger<WakePhraseDetector>>());

    private static VoiceCommandSettings Settings(string wakePhrase = "Hey John", bool wakePhraseEnabled = true)
        => new() { Enabled = true, WakePhrase = wakePhrase, WakePhraseEnabled = wakePhraseEnabled };

    [Theory]
    [InlineData("Hey John open Notepad")]
    [InlineData("hey john open Notepad")]
    [InlineData("HEY JOHN open Notepad")]
    [InlineData("Hey John, open Notepad")]
    [InlineData("Hey, John! open Notepad")]
    [InlineData("  Hey John   open Notepad")]
    public void Detect_WakePhraseVariants_AllMatch(string transcript)
    {
        var detection = _detector.Detect(transcript, Settings());

        Assert.NotNull(detection);
        Assert.True(detection.WakePhraseMatched);
        Assert.Equal("open Notepad", detection.CommandText);
    }

    [Fact]
    public void Detect_CommandTextIsVerbatim_PunctuationAndCasingSurvive()
    {
        var detection = _detector.Detect("Hey John, note check Azure pricing (tomorrow)!", Settings());

        Assert.Equal("note check Azure pricing (tomorrow)!", detection?.CommandText);
    }

    [Theory]
    [InlineData("open Notepad")]
    [InlineData("Hey Jim open Notepad")]
    [InlineData("So I said hey John and left")]
    public void Detect_NoWakePhraseAtTheStart_IsNormalDictation(string transcript)
    {
        Assert.Null(_detector.Detect(transcript, Settings()));
    }

    [Fact]
    public void Detect_WakePhraseAlone_MatchesWithEmptyCommandText()
    {
        var detection = _detector.Detect("Hey John.", Settings());

        Assert.NotNull(detection);
        Assert.True(detection.WakePhraseMatched);
        Assert.Equal("", detection.CommandText);
    }

    [Fact]
    public void Detect_WakePhraseDisabled_WholeTranscriptIsAnImplicitCandidate()
    {
        var detection = _detector.Detect("open Notepad", Settings(wakePhraseEnabled: false));

        Assert.NotNull(detection);
        Assert.False(detection.WakePhraseMatched);
        Assert.Equal("open Notepad", detection.CommandText);
    }

    [Fact]
    public void Detect_EmptyWakePhraseWhileEnabled_NeverMatches()
    {
        // Fail closed: a blank wake phrase must not turn every utterance into a command.
        Assert.Null(_detector.Detect("open Notepad", Settings(wakePhrase: "  ")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Detect_BlankTranscript_IsNormalDictation(string transcript)
    {
        Assert.Null(_detector.Detect(transcript, Settings()));
        Assert.Null(_detector.Detect(transcript, Settings(wakePhraseEnabled: false)));
    }

    [Fact]
    public void Detect_MultiWordCustomWakePhrase_Works()
    {
        var detection = _detector.Detect(
            "Okay dictate flow, open Notepad", Settings(wakePhrase: "Okay Dictate Flow"));

        Assert.NotNull(detection);
        Assert.Equal("open Notepad", detection.CommandText);
    }
}
