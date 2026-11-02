using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Lassistant.Templates;

/// <summary>
/// Извлекает уникальные ключи плейсхолдеров <c>{{ключ}}</c> из .docx
/// (тело, колонтитулы), с учётом разбиения текста по нескольким <c>w:t</c>.
/// </summary>
public static class TemplatePlaceholderScanner
{
    private static readonly Regex PlaceholderRegex = new(@"\{\{([^}]+)\}\}", RegexOptions.Compiled);

    private static readonly HashSet<string> ProfileCustomerGroupKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "passport", "ip", "legal", "bank", "contacts"
    };

    private static readonly HashSet<string> ProfileContractGroupKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "contract_meta", "termination"
    };

    public static IReadOnlyList<string> Extract(string templatePath)
    {
        if (!File.Exists(templatePath))
            return [];

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // FileShare.ReadWrite — тот же режим, в котором Word держит открытый файл,
        // чтобы сканирование шаблона не падало, если он в этот момент открыт для правки.
        using var stream = new FileStream(templatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var doc = WordprocessingDocument.Open(stream, false);
        var main = doc.MainDocumentPart;
        if (main is null)
            return [];

        CollectFromPart(main, keys);
        foreach (var header in main.HeaderParts)
            CollectFromPart(header, keys);
        foreach (var footer in main.FooterParts)
            CollectFromPart(footer, keys);

        return keys
            .OrderBy(k => GroupOrder(k))
            .ThenBy(k => SubGroupOrder(k))
            .ThenBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void CollectFromPart(OpenXmlPart part, HashSet<string> keys)
    {
        if (part.RootElement is null)
            return;

        foreach (var paragraph in part.RootElement.Descendants<Paragraph>())
        {
            var combined = string.Concat(paragraph.Descendants<Text>().Select(t => t.Text ?? string.Empty));
            foreach (Match match in PlaceholderRegex.Matches(combined))
            {
                var key = match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(key))
                    keys.Add(key);
            }
        }
    }

    public static string GroupLabel(string key)
    {
        if (key.StartsWith("исполнитель_", StringComparison.OrdinalIgnoreCase))
            return "Исполнитель";
        if (key.StartsWith("заказчик_", StringComparison.OrdinalIgnoreCase))
            return "Заказчик";
        if (key.StartsWith("объект_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("цена_", StringComparison.OrdinalIgnoreCase)
            || key.Contains("договор", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("идентификационный_код", StringComparison.OrdinalIgnoreCase))
            return "Договор и объект";

        if (TryProfileCategoryByFieldKey(key, out var category))
            return category;

        return "Прочие поля";
    }

    /// <summary>
    /// Категория для поля из field-definitions profile (без лейблов «Данные юрлица» и т.п.).
    /// </summary>
    public static string ProfileCategoryLabel(string definitionGroupKey, string fieldKey)
    {
        if (ProfileCustomerGroupKeys.Contains(definitionGroupKey))
            return "Заказчик";
        if (ProfileContractGroupKeys.Contains(definitionGroupKey))
            return "Договор и объект";
        return GroupLabel(fieldKey);
    }

    public static string SubGroupLabel(string key)
    {
        if (key.StartsWith("исполнитель_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("заказчик_", StringComparison.OrdinalIgnoreCase))
            return PartySubGroupLabel(key);

        if (TryProfileSubGroup(key, out var profileSub))
            return profileSub;

        if (key.StartsWith("цена_", StringComparison.OrdinalIgnoreCase)
            || key.Equals("ContractAmount", StringComparison.OrdinalIgnoreCase))
            return "Цена";
        if (key.StartsWith("объект_", StringComparison.OrdinalIgnoreCase)
            || key.Equals("ObjectAddress", StringComparison.OrdinalIgnoreCase))
            return "Объект";
        if (key.Equals("номер_договора", StringComparison.OrdinalIgnoreCase)
            || key.Equals("дата_договора", StringComparison.OrdinalIgnoreCase)
            || key.Contains("договор", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("идентификационный_код", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("Contract", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("Agreement", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("Termination", StringComparison.OrdinalIgnoreCase)
            || key.Equals("DebtAmount", StringComparison.OrdinalIgnoreCase))
            return "Договор";

        return "Прочее";
    }

    public static int SubGroupOrder(string key) => SubGroupLabel(key) switch
    {
        "Название" => 0,
        "ФИО и должность" => 1,
        "Реквизиты" => 2,
        "Банк" => 3,
        "Лицензия" => 4,
        "Контакты" => 5,
        "Договор" => 0,
        "Объект" => 1,
        "Цена" => 2,
        _ => 9,
    };

    public static string DisplayLabel(string key)
    {
        var label = key.Replace('_', ' ');
        if (label.Length == 0)
            return key;
        return char.ToUpper(label[0], System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) + label[1..];
    }

    private static bool TryProfileCategoryByFieldKey(string key, out string category)
    {
        if (TryProfileSubGroup(key, out var sub))
        {
            category = sub is "Договор" or "Объект" or "Цена"
                ? "Договор и объект"
                : "Заказчик";
            return true;
        }

        category = string.Empty;
        return false;
    }

    private static bool TryProfileSubGroup(string key, out string label)
    {
        switch (key)
        {
            case "CompanyName":
            case "IpName":
                label = "Название";
                return true;
            case "FullName":
            case "DirectorTitle":
            case "DirectorFullName":
            case "AuthorityBasis":
            case "PassportSeries":
            case "PassportNumber":
            case "PassportIssuedBy":
            case "PassportIssueDate":
            case "PassportDeptCode":
                label = "ФИО и должность";
                return true;
            case "Inn":
            case "Ogrn":
            case "Ogrnip":
            case "Kpp":
            case "LegalAddress":
            case "RegistrationAddress":
                label = "Реквизиты";
                return true;
            case "BankAccount":
            case "BankBik":
            case "BankName":
            case "CorrespondentAccount":
                label = "Банк";
                return true;
            case "Phone":
            case "Email":
                label = "Контакты";
                return true;
            case "ContractNumber":
            case "ContractDate":
            case "ContractReference":
            case "AgreementDate":
            case "TerminationDate":
            case "ContractLastDay":
            case "ContractClause":
            case "DebtAmount":
                label = "Договор";
                return true;
            case "ObjectAddress":
                label = "Объект";
                return true;
            case "ContractAmount":
                label = "Цена";
                return true;
            default:
                label = string.Empty;
                return false;
        }
    }

    private static string PartySubGroupLabel(string key)
    {
        var local = StripPartyPrefix(key);

        if (local.Contains("наименование", StringComparison.OrdinalIgnoreCase))
            return "Название";

        if (local.Contains("фио", StringComparison.OrdinalIgnoreCase)
            || local.Contains("должность", StringComparison.OrdinalIgnoreCase)
            || local.Contains("паспорт", StringComparison.OrdinalIgnoreCase)
            || local.Contains("дата_рождения", StringComparison.OrdinalIgnoreCase))
            return "ФИО и должность";

        if (ContainsAny(local, "лицензи", "номер_лицензии", "старый_номер", "дата_лицензии",
                "срок_действия", "регион_лицензии"))
            return "Лицензия";

        if (ContainsAny(local, "банк", "бик", "расчетный_счет", "корр_счет", "город_банка"))
            return "Банк";

        if (ContainsAny(local, "инн", "кпп", "огрн", "окпо", "адрес"))
            return "Реквизиты";

        if (ContainsAny(local, "телефон", "email"))
            return "Контакты";

        // заказчик_источник_финансирования и т.п.
        if (ContainsAny(local, "источник_финансирования"))
            return "Прочее";

        return "Прочее";
    }

    private static string StripPartyPrefix(string key)
    {
        if (key.StartsWith("исполнитель_", StringComparison.OrdinalIgnoreCase))
            return key["исполнитель_".Length..];
        if (key.StartsWith("заказчик_", StringComparison.OrdinalIgnoreCase))
            return key["заказчик_".Length..];
        return key;
    }

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(n => value.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static int GroupOrder(string key)
    {
        if (key.StartsWith("исполнитель_", StringComparison.OrdinalIgnoreCase)) return 0;
        if (key.StartsWith("заказчик_", StringComparison.OrdinalIgnoreCase)) return 1;
        if (key.StartsWith("объект_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("цена_", StringComparison.OrdinalIgnoreCase)
            || key.Contains("договор", StringComparison.OrdinalIgnoreCase))
            return 2;
        return 3;
    }
}
