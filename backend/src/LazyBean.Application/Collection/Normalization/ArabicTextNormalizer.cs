using System.Text;
using System.Text.RegularExpressions;

namespace LazyBean.Application.Collection.Normalization;

// Pure functions, no I/O - safe to unit test exhaustively and call from anywhere.
public static partial class ArabicTextNormalizer
{
    [GeneratedRegex(@"[ً-ْٰۖ-ۭ]")]
    private static partial Regex TashkeelPattern();

    private static readonly Dictionary<char, char> LetterMap = new()
    {
        ['أ'] = 'ا',
        ['إ'] = 'ا',
        ['آ'] = 'ا',
        ['ى'] = 'ي',
        ['ة'] = 'ه'
    };

    // Collapses the common "same word, different spelling" variants so matching keys agree:
    // أرز/إرز/آرز -> ارز, قهوى -> قهوي, قهوة -> قهوه. Also strips diacritics (tashkeel).
    public static string NormalizeLetters(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var withoutTashkeel = TashkeelPattern().Replace(input, string.Empty);

        var builder = new StringBuilder(withoutTashkeel.Length);
        foreach (var ch in withoutTashkeel)
        {
            builder.Append(LetterMap.TryGetValue(ch, out var mapped) ? mapped : ch);
        }

        return builder.ToString();
    }

    // "١٠" -> "10" - Arabic-Indic digits appear interchangeably with ASCII ones in real titles.
    public static string ConvertArabicIndicDigitsToAscii(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            builder.Append(ch is >= '٠' and <= '٩' ? (char)('0' + (ch - '٠')) : ch);
        }

        return builder.ToString();
    }
}
