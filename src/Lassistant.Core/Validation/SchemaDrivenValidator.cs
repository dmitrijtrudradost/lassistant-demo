using Lassistant.Core.Config;
using Lassistant.Core.Models;

namespace Lassistant.Core.Validation;

public static class SchemaDrivenValidator
{
    public static ValidationResult Validate(
        ClientData data,
        string fieldProfile,
        FieldDefinitionsConfig fieldDefinitions)
    {
        var result = new ValidationResult();
        if (!fieldDefinitions.Profiles.TryGetValue(fieldProfile, out var profile))
        {
            result.Errors.Add($"Профиль полей «{fieldProfile}» не найден.");
            return result;
        }

        var values = ClientDataMapper.ToFieldValues(data);

        foreach (var fieldKey in profile.Required)
        {
            if (!values.TryGetValue(fieldKey, out var value) || string.IsNullOrWhiteSpace(value))
            {
                var label = fieldDefinitions.Fields.TryGetValue(fieldKey, out var def)
                    ? def.Label
                    : fieldKey;
                result.Warnings.Add($"Не заполнено: {label}.");
            }
        }

        foreach (var fieldKey in profile.Required.Concat(GetAllProfileFields(profile, fieldDefinitions)))
        {
            if (!values.TryGetValue(fieldKey, out var value) || string.IsNullOrWhiteSpace(value))
                continue;

            if (!fieldDefinitions.Fields.TryGetValue(fieldKey, out var fieldDef))
                continue;

            var warning = GetFormatWarning(fieldDef.Validator, fieldDef.Label, value);
            if (warning is not null)
                result.Warnings.Add(warning);
        }

        return result;
    }

    private static IEnumerable<string> GetAllProfileFields(
        FieldProfileDefinition profile,
        FieldDefinitionsConfig fieldDefinitions)
    {
        foreach (var groupKey in profile.Groups)
        {
            if (fieldDefinitions.Groups.TryGetValue(groupKey, out var group))
            {
                foreach (var field in group.Fields)
                    yield return field;
            }
        }
    }

    private static string? GetFormatWarning(string validator, string label, string value)
    {
        if (FlexibleFieldValidator.IsPlausible(validator, value))
            return null;

        return validator switch
        {
            "inn" => $"Проверьте ИНН: {label}.",
            "ogrn" => $"Проверьте ОГРН: {label}.",
            "ogrnip" => $"Проверьте ОГРНИП: {label}.",
            "kpp" => $"Проверьте КПП: {label}.",
            "date" => $"Проверьте дату: {label}.",
            "documentId" => $"Проверьте номер документа: {label}.",
            _ => $"Проверьте значение: {label}."
        };
    }
}
