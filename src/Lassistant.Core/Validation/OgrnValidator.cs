namespace Lassistant.Core.Validation;

public static class OgrnValidator
{
    public static bool IsValid(string? ogrn)
    {
        if (string.IsNullOrWhiteSpace(ogrn))
            return true;

        var digits = new string(ogrn.Where(char.IsDigit).ToArray());
        if (digits.Length != 13 || (digits[0] != '1' && digits[0] != '5'))
            return false;

        var numberPart = long.Parse(digits[..12]);
        var check = numberPart % 11 % 10;
        return check == digits[12] - '0';
    }
}
