namespace Lassistant.Core.Models;

public static class DisplayNames
{
    public static string ForDocumentKind(DocumentKind kind) => kind switch
    {
        DocumentKind.Contract => "Договор",
        DocumentKind.Addendum => "Допсоглашение",
        DocumentKind.Termination => "Соглашение о расторжении",
        _ => kind.ToString()
    };

    public static string ForClientType(ClientType type) => type switch
    {
        ClientType.Individual => "Физическое лицо",
        ClientType.IndividualEntrepreneur => "Индивидуальный предприниматель (ИП)",
        ClientType.LegalEntity => "Юридическое лицо",
        _ => type.ToString()
    };

    public static string ForServiceKind(ServiceKind service) => service switch
    {
        ServiceKind.Montage => "Монтаж",
        ServiceKind.FireAlarm => "Пожарная сигнализация",
        ServiceKind.Gbr => "ГБР",
        ServiceKind.SecurityPost => "Пост охраны",
        _ => service.ToString()
    };
}
