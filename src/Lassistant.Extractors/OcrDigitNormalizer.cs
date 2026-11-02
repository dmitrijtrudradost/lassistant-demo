using System.Text.RegularExpressions;

namespace Lassistant.Extractors;

internal static partial class OcrDigitNormalizer
{
    public static string FixCodes(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        text = DeptCodeFuzzyRegex().Replace(text, static m =>
            $"{FixDigit(m.Groups[1].Value)}-{FixDigit(m.Groups[2].Value)}");

        if (text.Contains("ОРЕНБУР", StringComparison.OrdinalIgnoreCase))
            text = text.Replace("660-", "560-", StringComparison.Ordinal);

        return text;
    }

    private static string FixDigit(string value)
    {
        if (value.Length == 0)
            return value;

        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            chars[i] = FixDigitChar(chars[i], i == chars.Length - 1);

        return new string(chars);
    }

    private static char FixDigitChar(char ch, bool isLast) =>
        ch switch
        {
            'О' or 'о' => '0',
            'Д' or 'д' => isLast ? '9' : '0',
            'З' or 'з' => '3',
            'В' or 'в' => '8',
            'Б' or 'б' => '8',
            'S' or 's' => '5',
            'I' or 'i' => '1',
            'l' => '1',
            'G' or 'g' => '9',
            'q' => '9',
            _ => ch
        };

    [GeneratedRegex(@"\b(\d{3})[-\s]([0-9ОоДдЗзВвБбSsIldD]{3})\b")]
    private static partial Regex DeptCodeFuzzyRegex();
}
