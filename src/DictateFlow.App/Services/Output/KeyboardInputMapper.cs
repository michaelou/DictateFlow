namespace DictateFlow.App.Services.Output;

/// <summary>
/// Pure text → <see cref="KeyboardEvent"/> mapping for the simulated-keyboard output
/// provider. Every UTF-16 code unit becomes a Unicode down+up pair (surrogate halves are
/// sent individually — Windows recomposes them); line breaks (<c>\r\n</c>, <c>\n</c> or a
/// lone <c>\r</c>) become a single <c>VK_RETURN</c> down+up pair so target apps insert a
/// real newline. Kept free of P/Invoke so it is fully unit-testable.
/// </summary>
public static class KeyboardInputMapper
{
    /// <summary>Virtual-key code of the Return key (<c>VK_RETURN</c>).</summary>
    public const ushort ReturnKey = 0x0D;

    /// <summary>Maps <paramref name="text"/> to the keyboard events that type it.</summary>
    /// <param name="text">The text to type.</param>
    /// <returns>The down+up event pairs, in typing order.</returns>
    public static IReadOnlyList<KeyboardEvent> MapText(string text)
    {
        var events = new List<KeyboardEvent>(text.Length * 2);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++; // \r\n is one line break, not two.
                }

                events.Add(new KeyboardEvent(ReturnKey, 0, IsKeyUp: false));
                events.Add(new KeyboardEvent(ReturnKey, 0, IsKeyUp: true));
            }
            else
            {
                events.Add(new KeyboardEvent(0, c, IsKeyUp: false));
                events.Add(new KeyboardEvent(0, c, IsKeyUp: true));
            }
        }

        return events;
    }

    /// <summary>Splits <paramref name="events"/> into chunks of at most <paramref name="chunkSize"/> events.</summary>
    /// <param name="events">The events to split.</param>
    /// <param name="chunkSize">Maximum events per chunk; must be positive.</param>
    /// <returns>The chunks, in order; empty when <paramref name="events"/> is empty.</returns>
    public static IEnumerable<IReadOnlyList<KeyboardEvent>> Chunk(IReadOnlyList<KeyboardEvent> events, int chunkSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);

        for (var start = 0; start < events.Count; start += chunkSize)
        {
            var count = Math.Min(chunkSize, events.Count - start);
            var chunk = new KeyboardEvent[count];
            for (var i = 0; i < count; i++)
            {
                chunk[i] = events[start + i];
            }

            yield return chunk;
        }
    }
}
