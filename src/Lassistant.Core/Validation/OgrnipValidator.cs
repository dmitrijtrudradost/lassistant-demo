namespace Lassistant.Core.Validation;

public static class OgrnipValidator
{
    public static bool IsValid(string? ogrnip)
    {
        if (string.IsNullOrWhiteSpace(ogrnip))
            return true;

        var digits = new string(ogrnip.Where(char.IsDigit).ToArray());
        if (digits.Length != 15 || digits[0] != '3')
            return false;

        var numberPart = long.Parse(digits[..14]);
        var check = numberPart % 13 % 10;
        return check == digits[14] - '0';
    }
}
