using System.Globalization;

namespace AutoTyper;

public enum TypingMode { Letters, Words }

public static class TextChunker
{
    public static IReadOnlyList<string> Chunk(string text, TypingMode mode)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return mode == TypingMode.Letters ? SplitLetters(normalized) : SplitWords(normalized);
    }

    static List<string> SplitLetters(string text)
    {
        var units = new List<string>();
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
            units.Add((string)elements.Current);
        return units;
    }

    // A unit is a run of non-whitespace plus the whitespace after it. Whitespace at the
    // start of the text has no word to attach to, so it becomes a unit of its own.
    static List<string> SplitWords(string text)
    {
        var units = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            units.Add(text.Substring(start, i - start));
        }
        return units;
    }
}
