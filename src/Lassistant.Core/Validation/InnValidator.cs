namespace Lassistant.Core.Validation;

public static class InnValidator
{
    public static bool IsValid(string? inn)
    {
        if (string.IsNullOrWhiteSpace(inn))
            return true;

        var digits = new string(inn.Where(char.IsDigit).ToArray());
        return digits.Length switch
        {
            10 => ValidateLegalEntityInn(digits),
            12 => ValidateIndividualInn(digits),
            _ => false
        };
    }

    private static bool ValidateLegalEntityInn(string digits)
    {
        int[] coefficients = [2, 4, 10, 3, 5, 9, 4, 6, 8];
        var sum = 0;
        for (var i = 0; i < 9; i++)
            sum += (digits[i] - '0') * coefficients[i];
        var check = sum % 11 % 10;
        return check == digits[9] - '0';
    }

    private static bool ValidateIndividualInn(string digits)
    {
        int[] coeff11 = [7, 2, 4, 10, 3, 5, 9, 4, 6, 8];
        int[] coeff12 = [3, 7, 2, 4, 10, 3, 5, 9, 4, 6, 8];

        var sum11 = 0;
        for (var i = 0; i < 10; i++)
            sum11 += (digits[i] - '0') * coeff11[i];
        var check11 = sum11 % 11 % 10;
        if (check11 != digits[10] - '0')
            return false;

        var sum12 = 0;
        for (var i = 0; i < 11; i++)
            sum12 += (digits[i] - '0') * coeff12[i];
        var check12 = sum12 % 11 % 10;
        return check12 == digits[11] - '0';
    }
}
