using Lassistant.Core.Models;
using Lassistant.Core.Validation;

namespace Lassistant.Core.Validation;

public sealed class ValidationResult
{
    public bool HasWarnings => Warnings.Count > 0;
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];

    public string FormatWarnings() =>
        Warnings.Count == 0
            ? string.Empty
            : "Предупреждения:" + Environment.NewLine + string.Join(Environment.NewLine, Warnings);
}

public static class ClientDataValidator
{
    public static ValidationResult Validate(ClientData data)
    {
        var result = new ValidationResult();

        if (string.IsNullOrWhiteSpace(data.FullName))
            result.Warnings.Add("Не заполнено: ФИО.");

        if (!PassportValidator.IsSeriesValid(data.PassportSeries))
            result.Warnings.Add("Проверьте серию документа.");

        if (!PassportValidator.IsNumberValid(data.PassportNumber))
            result.Warnings.Add("Проверьте номер документа.");

        if (!PassportValidator.IsDeptCodeValid(data.PassportDeptCode))
            result.Warnings.Add("Проверьте код документа.");

        if (!PassportValidator.IsIssueDateValid(data.PassportIssueDate))
            result.Warnings.Add("Проверьте дату выдачи документа.");

        if (!FlexibleFieldValidator.IsPlausible("inn", data.Inn))
            result.Warnings.Add("Проверьте ИНН.");

        if (data.ClientType == ClientType.IndividualEntrepreneur)
        {
            if (string.IsNullOrWhiteSpace(data.Inn))
                result.Warnings.Add("Не заполнено: ИНН.");

            if (!FlexibleFieldValidator.IsPlausible("ogrnip", data.Ogrnip))
                result.Warnings.Add("Проверьте ОГРНИП.");
        }

        if (data.ClientType == ClientType.LegalEntity)
        {
            if (string.IsNullOrWhiteSpace(data.CompanyName))
                result.Warnings.Add("Не заполнено: наименование организации.");

            if (!FlexibleFieldValidator.IsPlausible("inn", data.Inn))
                result.Warnings.Add("Проверьте ИНН организации.");

            if (!FlexibleFieldValidator.IsPlausible("ogrn", data.Ogrn))
                result.Warnings.Add("Проверьте ОГРН.");

            if (!FlexibleFieldValidator.IsPlausible("kpp", data.Kpp))
                result.Warnings.Add("Проверьте КПП.");
        }

        return result;
    }
}
