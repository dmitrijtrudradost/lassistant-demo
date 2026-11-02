namespace Lassistant.Core.Validation;

public static class PassportValidator
{
    public static bool IsSeriesValid(string? series) =>
        FlexibleFieldValidator.IsPlausible("documentId", series);

    public static bool IsNumberValid(string? number) =>
        FlexibleFieldValidator.IsPlausible("documentId", number);

    public static bool IsDeptCodeValid(string? code) =>
        FlexibleFieldValidator.IsPlausible("passportDeptCode", code);

    public static bool IsIssueDateValid(string? date) =>
        FlexibleFieldValidator.IsPlausible("date", date);
}
