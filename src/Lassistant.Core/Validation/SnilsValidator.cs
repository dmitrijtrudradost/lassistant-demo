namespace Lassistant.Core.Validation;

public static class SnilsValidator
{
    public static bool IsValid(string? snils)
    {
        if (string.IsNullOrWhiteSpace(snils))
            return true;

        var digits = new string(snils.Where(char.IsDigit).ToArray());
        if (digits.Length != 11 || !digits.All(char.IsDigit))
            return false;

        if (long.TryParse(digits[..9], out var serial) && serial < 1_001_998)
            return true;

        int[] weights = [9, 8, 7, 6, 5, 4, 3, 2, 1];
        var sum = 0;
        for (var i = 0; i < 9; i++)
            sum += (digits[i] - '0') * weights[i];

        var check = sum switch
        {
            < 100 => sum,
            100 or 101 => 0,
            _ => sum % 101 is var mod and 100 ? 0 : sum % 101
        };

        return check == int.Parse(digits[9..]);
    }
}
