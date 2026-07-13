namespace DictateFlow.Core.Text;

/// <summary>
/// Counts words in dictated text for the usage metrics. Counting is whitespace-delimited:
/// runs of spaces, tabs and newlines separate words and collapse to a single boundary. Like
/// the "words dictated" figures other dictation apps advertise, this does not segment
/// scriptio-continua languages (e.g. Chinese/Japanese) that lack inter-word spacing.
/// </summary>
public static class WordCounter
{
    /// <summary>Counts the whitespace-delimited words in <paramref name="text"/>.</summary>
    /// <param name="text">The text to measure; <see langword="null"/>, empty or whitespace yields zero.</param>
    /// <returns>The number of words.</returns>
    public static int CountWords(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
