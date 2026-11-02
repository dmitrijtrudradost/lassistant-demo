using System.Text.RegularExpressions;

namespace Lassistant.Core.Formatting;

public static partial class PersonNameFormatter
{
    [GeneratedRegex(@"^[А-ЯЁа-яё\-]+\s+[А-ЯЁ]\.[А-ЯЁ]\.?$", RegexOptions.Compiled)]
    private static partial Regex ShortNameRegex();

    /// <summary>Фамилия И.О. для строки подписи.</summary>
    public static string FormatSignerName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return string.Empty;

        var trimmed = Regex.Replace(fullName.Trim(), @"\s+", " ");
        if (ShortNameRegex().IsMatch(trimmed))
            return trimmed;

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            >= 3 => $"{parts[0]} {parts[1][0]}.{parts[2][0]}.",
            2 when parts[1].Contains('.') => trimmed,
            2 => $"{parts[0]} {parts[1][0]}.",
            _ => trimmed
        };
    }
}
