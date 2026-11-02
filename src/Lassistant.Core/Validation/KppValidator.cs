using System.Text.RegularExpressions;

namespace Lassistant.Core.Validation;

public static partial class KppValidator
{
    [GeneratedRegex(@"^\d{9}$")]
    private static partial Regex KppRegex();

    public static bool IsValid(string? kpp) =>
        string.IsNullOrWhiteSpace(kpp) || KppRegex().IsMatch(new string(kpp.Where(char.IsDigit).ToArray()));
}
