using System.Globalization;
using System.Text;

namespace Weed.Core;

public static class TextNormalizer
{
    private static readonly Dictionary<char, char> SymbolMap = new()
    {
        ['（'] = '(',
        ['）'] = ')',
        ['［'] = '[',
        ['］'] = ']',
        ['｛'] = '{',
        ['｝'] = '}',
        ['，'] = ',',
        ['。'] = '.',
        ['；'] = ';',
        ['：'] = ':',
        ['＋'] = '+',
        ['－'] = '-',
        ['＊'] = '*',
        ['／'] = '/',
        ['×'] = '*',
        ['÷'] = '/',
        ['％'] = '%',
        ['　'] = ' '
    };

    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var lastWasSpace = true;

        foreach (var raw in text.Trim())
        {
            var ch = raw;
            if (ch is >= '！' and <= '～')
            {
                ch = (char)(ch - 0xFEE0);
            }
            else if (SymbolMap.TryGetValue(ch, out var mapped))
            {
                ch = mapped;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            builder.Append(char.ToLower(ch, CultureInfo.InvariantCulture));
            lastWasSpace = false;
        }

        return builder.ToString();
    }

    public static string ToIdFragment(string text)
    {
        var normalized = Normalize(text);
        var builder = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '-');
        }

        return builder.ToString().Trim('-');
    }
}
