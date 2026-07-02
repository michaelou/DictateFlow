using DictateFlow.App.Services.Output;

namespace DictateFlow.Tests;

/// <summary>
/// Tests for <see cref="KeyboardInputMapper"/> — the pure text → keyboard-event mapping
/// behind the simulated-keyboard output provider (the <c>SendInput</c> shell itself is
/// verified manually).
/// </summary>
public sealed class KeyboardInputMapperTests
{
    [Fact]
    public void MapText_PlainAscii_ProducesUnicodeDownUpPairPerCharacter()
    {
        var events = KeyboardInputMapper.MapText("abc");

        Assert.Equal(6, events.Count);
        Assert.All(events, e => Assert.True(e.IsUnicode));
        Assert.Equal((ushort)'a', events[0].UnicodeCodeUnit);
        Assert.False(events[0].IsKeyUp);
        Assert.Equal((ushort)'a', events[1].UnicodeCodeUnit);
        Assert.True(events[1].IsKeyUp);
        Assert.Equal((ushort)'c', events[4].UnicodeCodeUnit);
        Assert.Equal((ushort)'c', events[5].UnicodeCodeUnit);
    }

    [Fact]
    public void MapText_NonAsciiText_MapsEachCodeUnit()
    {
        // The Definition-of-Done sample: accented Latin plus CJK.
        var events = KeyboardInputMapper.MapText("café 你好");

        Assert.Equal(7 * 2, events.Count);
        Assert.Equal((ushort)0x00E9, events[6].UnicodeCodeUnit); // é
        Assert.Equal((ushort)0x4F60, events[10].UnicodeCodeUnit); // 你
        Assert.Equal((ushort)0x597D, events[12].UnicodeCodeUnit); // 好
        Assert.All(events, e => Assert.True(e.IsUnicode));
    }

    [Fact]
    public void MapText_SurrogatePair_SendsBothCodeUnitsIndividually()
    {
        var events = KeyboardInputMapper.MapText("😀"); // U+1F600 = 0xD83D 0xDE00

        Assert.Equal(4, events.Count);
        Assert.Equal((ushort)0xD83D, events[0].UnicodeCodeUnit);
        Assert.Equal((ushort)0xD83D, events[1].UnicodeCodeUnit);
        Assert.Equal((ushort)0xDE00, events[2].UnicodeCodeUnit);
        Assert.Equal((ushort)0xDE00, events[3].UnicodeCodeUnit);
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    public void MapText_LineBreak_BecomesSingleReturnPair(string lineBreak)
    {
        var events = KeyboardInputMapper.MapText(lineBreak);

        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.False(e.IsUnicode));
        Assert.Equal(KeyboardInputMapper.ReturnKey, events[0].VirtualKey);
        Assert.False(events[0].IsKeyUp);
        Assert.Equal(KeyboardInputMapper.ReturnKey, events[1].VirtualKey);
        Assert.True(events[1].IsKeyUp);
    }

    [Fact]
    public void MapText_MixedTextAndNewlines_KeepsOrder()
    {
        var events = KeyboardInputMapper.MapText("a\r\nb");

        Assert.Equal(6, events.Count);
        Assert.Equal((ushort)'a', events[0].UnicodeCodeUnit);
        Assert.Equal(KeyboardInputMapper.ReturnKey, events[2].VirtualKey);
        Assert.Equal(KeyboardInputMapper.ReturnKey, events[3].VirtualKey);
        Assert.Equal((ushort)'b', events[4].UnicodeCodeUnit);
    }

    [Fact]
    public void MapText_ConsecutiveLineBreaks_OneReturnPairEach()
    {
        var events = KeyboardInputMapper.MapText("\r\n\r\n\n");

        Assert.Equal(6, events.Count);
        Assert.All(events, e => Assert.Equal(KeyboardInputMapper.ReturnKey, e.VirtualKey));
    }

    [Fact]
    public void MapText_EmptyText_ProducesNoEvents()
    {
        Assert.Empty(KeyboardInputMapper.MapText(""));
    }

    [Fact]
    public void Chunk_SplitsIntoChunksOfRequestedSizeKeepingOrder()
    {
        var events = KeyboardInputMapper.MapText(new string('x', 22)); // 44 events
        var chunks = KeyboardInputMapper.Chunk(events, 20).ToList();

        Assert.Equal(3, chunks.Count);
        Assert.Equal(20, chunks[0].Count);
        Assert.Equal(20, chunks[1].Count);
        Assert.Equal(4, chunks[2].Count);
        Assert.Equal(events, chunks.SelectMany(c => c));
    }

    [Fact]
    public void Chunk_FewerEventsThanChunkSize_SingleChunk()
    {
        var events = KeyboardInputMapper.MapText("hi");

        var chunks = KeyboardInputMapper.Chunk(events, 20).ToList();

        Assert.Single(chunks);
        Assert.Equal(events, chunks[0]);
    }

    [Fact]
    public void Chunk_EmptyEvents_YieldsNothing()
    {
        Assert.Empty(KeyboardInputMapper.Chunk([], 20));
    }

    [Fact]
    public void Chunk_NonPositiveChunkSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => KeyboardInputMapper.Chunk(KeyboardInputMapper.MapText("x"), 0).ToList());
    }
}
