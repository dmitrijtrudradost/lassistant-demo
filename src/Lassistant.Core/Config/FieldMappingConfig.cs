using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lassistant.Core.Config;

public sealed class FieldMappingConfig
{
    [JsonPropertyName("labels")]
    public Dictionary<string, string[]> Labels { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static FieldMappingConfig Load(string path)
    {
        if (!File.Exists(path))
            return CreateDefault();

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<FieldMappingConfig>(json) ?? CreateDefault();
    }

    public static FieldMappingConfig CreateDefault() => new()
    {
        Labels = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["FullName"] = ["ФИО", "Ф.И.О.", "Полное наименование"],
            ["PassportSeries"] = ["Серия паспорта", "Серия"],
            ["PassportNumber"] = ["Номер паспорта", "Номер"],
            ["PassportIssuedBy"] = ["Кем выдан", "Выдан"],
            ["PassportIssueDate"] = ["Дата выдачи"],
            ["PassportDeptCode"] = ["Код подразделения"],
            ["RegistrationAddress"] = ["Адрес регистрации", "Адрес", "Место жительства"],
            ["Inn"] = ["ИНН"],
            ["Ogrnip"] = ["ОГРНИП"],
            ["Ogrn"] = ["ОГРН"],
            ["Kpp"] = ["КПП"],
            ["IpName"] = ["Наименование ИП", "ИП"],
            ["CompanyName"] = ["Наименование организации", "Организация", "Наименование"],
            ["LegalAddress"] = ["Юридический адрес", "Юр. адрес"],
            ["DirectorTitle"] = ["Должность подписанта", "Должность"],
            ["DirectorFullName"] = ["ФИО подписанта", "Подписант"],
            ["AuthorityBasis"] = ["Основание полномочий", "Действует на основании"],
            ["BankAccount"] = ["Р/с", "Расчётный счёт", "Расчетный счет", "Р/сч"],
            ["BankBik"] = ["БИК"],
            ["BankName"] = ["Банк", "Наименование банка"],
            ["CorrespondentAccount"] = ["К/с", "Корр. счёт", "Корр. счет", "Корреспондентский счёт"],
            ["Phone"] = ["Телефон", "Тел.", "Моб."],
            ["Email"] = ["E-mail", "Email", "Эл. почта"],
            ["ContractNumber"] = ["Номер договора", "Договор №"],
            ["ContractDate"] = ["Дата договора"],
            ["ObjectAddress"] = ["Адрес объекта", "Объект"],
            ["ContractAmount"] = ["Сумма договора", "Сумма"]
        }
    };
}
