namespace Lassistant.Core.Validation;

public static class FlexibleFieldValidator
{
    private const int MaxShortTextLength = 64;
    private const int MaxDocumentIdLength = 40;
    private const int MaxDateTextLength = 64;

    public static bool IsPlausible(string validator, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        return validator switch
        {
            "documentId" => IsDocumentId(value),
            "date" => IsDateText(value),
            "inn" => IsInnPlausible(value),
            "ogrn" => IsDigitsPlausible(value, expectedLength: 13),
            "ogrnip" => IsDigitsPlausible(value, expectedLength: 15),
            "kpp" => IsDigitsPlausible(value, expectedLength: 9),
            "passportSeries" => IsDocumentId(value),
            "passportNumber" => IsDocumentId(value),
            "passportDeptCode" => IsShortText(value),
            "passportIssueDate" => IsDateText(value),
            "none" => true,
            _ => IsShortText(value)
        };
    }

    private static bool IsShortText(string value) =>
        value.Trim().Length <= MaxShortTextLength;

    private static bool IsDocumentId(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length > 0 && trimmed.Length <= MaxDocumentIdLength;
    }

    private static bool IsDateText(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 4
               && trimmed.Length <= MaxDateTextLength
               && trimmed.Any(char.IsDigit);
    }

    private static bool IsInnPlausible(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
            return IsShortText(value);

        return digits.Length is >= 8 and <= 12;
    }

    private static bool IsDigitsPlausible(string value, int expectedLength)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
            return IsShortText(value);

        return digits.Length == expectedLength;
    }
}
