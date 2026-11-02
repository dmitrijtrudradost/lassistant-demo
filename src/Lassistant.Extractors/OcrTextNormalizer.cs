using System.Text;
using System.Text.RegularExpressions;

namespace Lassistant.Extractors;

internal static partial class OcrTextNormalizer
{
    private static readonly Dictionary<char, char> LatinToCyrillic = new()
    {
        ['A'] = 'А', ['B'] = 'В', ['C'] = 'С', ['D'] = 'Д', ['E'] = 'Е',
        ['F'] = 'Ф', ['G'] = 'Г', ['H'] = 'Н', ['I'] = 'И', ['J'] = 'Л',
        ['K'] = 'К', ['L'] = 'Л', ['M'] = 'М', ['N'] = 'Н', ['O'] = 'О',
        ['P'] = 'Р', ['Q'] = 'Ю', ['R'] = 'Р', ['S'] = 'С', ['T'] = 'Т',
        ['U'] = 'У', ['V'] = 'В', ['W'] = 'В', ['X'] = 'Х', ['Y'] = 'У',
        ['Z'] = 'З',
    };

    private static readonly (string From, string To)[] CommonFixes =
    [
        ("ОТАЕДОМ", "ОТДЕЛОМ"),
        ("OTAEDOM", "ОТДЕЛОМ"),
        ("УЮМС", "УФМС"),
        ("YQMC", "УФМС"),
        ("РОССКШ", "РОССИИ"),
        ("РОССКВ", "РОССИИ"),
        ("РОССИШ", "РОССИИ"),
        ("POCCK", "РОССИ"),
        ("ОРЕНВУРРСКОВИ", "ОРЕНБУРГСКОЙ"),
        ("ОРЕНВУРГСКОЙ", "ОРЕНБУРГСКОЙ"),
        ("ОРЕНЬУРГСК", "ОРЕНБУРГСК"),
        ("ОЬЛ", "ОБЛ"),
        ("ОЬЛДСТИ", "ОБЛАСТИ"),
        ("ОБЛдсти", "ОБЛАСТИ"),
        ("ОБЛДСТИ", "ОБЛАСТИ"),
        ("ОЬЛАСТИ", "ОБЛАСТИ"),
        ("ДЛЕКСДНДРОВНД", "АЛЕКСАНДРОВНА"),
        ("ДЛЕКСАНДРОВНА", "АЛЕКСАНДРОВНА"),
        ("ЛЕПТ", "ЛИЛИЯ"),
        ("ЛИЕ-ИЛИТ", "ЛИЛИЯ"),
        ("ЛИЕИЛИТ", "ЛИЛИЯ"),
        ("OPEHBYPrCK", "ОРЕНБУРГСК"),
        ("ОВФИАСТВЛ", "ОБЛАСТИ"),
        ("OBFIACTV", "ОБЛАСТИ"),
        ("НОВОТРОНУКЕ", "НОВОТРОИЦКЕ"),
        ("НОВОТРОН:", "НОВОТРОИЦКЕ"),
        ("HOBOTPO", "НОВОТРО"),
        ("АЛИЕКСАНАРОВНА", "АЛЕКСАНДРОВНА"),
        ("АЛЕКСАНАРОВНА", "АЛЕКСАНДРОВНА"),
        (" no ", " по "),
        (" NO ", " ПО "),
    ];

    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var mapped = LatinWordRegex().Replace(text, match => MapWord(match.Value));
        foreach (var (from, to) in CommonFixes)
            mapped = mapped.Replace(from, to, StringComparison.OrdinalIgnoreCase);

        // Схлопываем только горизонтальные пробелы — переносы строк нужны для парсинга ФИО.
        return Regex.Replace(mapped, @"[ \t]{2,}", " ");
    }

    private static string MapWord(string word)
    {
        if (!word.Any(static ch => ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
            return word;

        var sb = new StringBuilder(word.Length);
        foreach (var ch in word)
        {
            var upper = char.ToUpperInvariant(ch);
            if (LatinToCyrillic.TryGetValue(upper, out var mapped))
                sb.Append(mapped);
            else
                sb.Append(ch);
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"\b[A-Za-z][A-Za-z'\-0-9]{1,}\b")]
    private static partial Regex LatinWordRegex();
}
