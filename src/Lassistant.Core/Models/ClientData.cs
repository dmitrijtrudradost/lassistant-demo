using Lassistant.Core.Formatting;

namespace Lassistant.Core.Models;

public enum ClientType
{
    Individual,
    IndividualEntrepreneur,
    LegalEntity
}

public sealed class ClientData
{
    public ClientType ClientType { get; set; } = ClientType.Individual;

    public string FullName { get; set; } = string.Empty;
    public string PassportSeries { get; set; } = string.Empty;
    public string PassportNumber { get; set; } = string.Empty;
    public string PassportIssuedBy { get; set; } = string.Empty;
    public string PassportIssueDate { get; set; } = string.Empty;
    public string PassportDeptCode { get; set; } = string.Empty;
    public string RegistrationAddress { get; set; } = string.Empty;

    public string Inn { get; set; } = string.Empty;
    public string Ogrnip { get; set; } = string.Empty;
    public string IpName { get; set; } = string.Empty;

    public string CompanyName { get; set; } = string.Empty;
    public string Ogrn { get; set; } = string.Empty;
    public string Kpp { get; set; } = string.Empty;
    public string LegalAddress { get; set; } = string.Empty;
    public string DirectorTitle { get; set; } = string.Empty;
    public string DirectorFullName { get; set; } = string.Empty;
    public string AuthorityBasis { get; set; } = string.Empty;

    public string BankAccount { get; set; } = string.Empty;
    public string BankBik { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string CorrespondentAccount { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public string ContractNumber { get; set; } = string.Empty;
    public string ContractDate { get; set; } = string.Empty;
    public string ContractReference { get; set; } = string.Empty;
    public string AgreementDate { get; set; } = string.Empty;
    public string TerminationDate { get; set; } = string.Empty;
    public string ContractLastDay { get; set; } = string.Empty;
    public string ContractClause { get; set; } = string.Empty;
    public string DebtAmount { get; set; } = string.Empty;
    public string ObjectAddress { get; set; } = string.Empty;
    public string ContractAmount { get; set; } = string.Empty;

    public Dictionary<string, string> ExtraFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> ToPlaceholderMap()
    {
        var directorFullNameFormatted = PersonNameFormatter.FormatSignerName(DirectorFullName);
        var contractRef = BuildContractReference();
        var directorRepr = BuildDirectorRepresentation();

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // ── Физическое лицо ──────────────────────────────────────────────
            ["client_full_name"] = FullName,
            ["фио"] = FullName,

            ["passport_series"] = PassportSeries,
            ["серия_документа"] = PassportSeries,

            ["passport_number"] = PassportNumber,
            ["номер_документа"] = PassportNumber,

            ["passport_issued_by"] = PassportIssuedBy,
            ["кем_выдан"] = PassportIssuedBy,

            ["passport_issue_date"] = PassportIssueDate,
            ["дата_выдачи"] = PassportIssueDate,

            ["passport_dept_code"] = PassportDeptCode,
            ["код_документа"] = PassportDeptCode,

            ["registration_address"] = RegistrationAddress,
            ["адрес_регистрации"] = RegistrationAddress,

            // ── Реквизиты ────────────────────────────────────────────────────
            ["inn"] = Inn,
            ["инн"] = Inn,

            ["ogrnip"] = Ogrnip,
            ["огрнип"] = Ogrnip,

            ["ip_name"] = IpName,
            ["наименование_ип"] = IpName,

            ["company_name"] = CompanyName,
            ["наименование_орг"] = CompanyName,

            ["ogrn"] = Ogrn,
            ["огрн"] = Ogrn,

            ["kpp"] = Kpp,
            ["кпп"] = Kpp,

            ["legal_address"] = GetAddressLine(0),
            ["юр_адрес"] = GetAddressLine(0),

            ["legal_address_line2"] = GetAddressLine(1),
            ["юр_адрес_2"] = GetAddressLine(1),

            ["legal_address_line3"] = GetAddressLine(2),
            ["юр_адрес_3"] = GetAddressLine(2),

            ["legal_address_line4"] = GetAddressLine(3),
            ["юр_адрес_4"] = GetAddressLine(3),

            // ── Подписант ────────────────────────────────────────────────────
            ["director_title"] = DirectorTitle,
            ["должность"] = DirectorTitle,

            ["director_full_name"] = directorFullNameFormatted,
            ["фио_подписанта"] = directorFullNameFormatted,

            ["director_representation"] = directorRepr,
            ["должность_и_фио"] = directorRepr,

            ["authority_basis"] = AuthorityBasis,
            ["основание"] = AuthorityBasis,

            // ── Банк ─────────────────────────────────────────────────────────
            ["bank_account"] = BankAccount,
            ["расчетный_счет"] = BankAccount,

            ["bank_bik"] = BankBik,
            ["бик"] = BankBik,

            ["bank_name"] = BankName,
            ["банк"] = BankName,

            ["correspondent_account"] = CorrespondentAccount,
            ["корр_счет"] = CorrespondentAccount,

            // ── Контакты ─────────────────────────────────────────────────────
            ["phone"] = Phone,
            ["телефон"] = Phone,

            ["email"] = Email,
            ["эл_почта"] = Email,

            // ── Договор ──────────────────────────────────────────────────────
            ["contract_number"] = ContractNumber,
            ["номер_договора"] = ContractNumber,

            ["contract_date"] = ContractDate,
            ["дата_договора"] = ContractDate,

            ["contract_reference"] = contractRef,
            ["номер_и_дата_договора"] = contractRef,

            // ── Расторжение ──────────────────────────────────────────────────
            ["agreement_date"] = AgreementDate,
            ["дата_соглашения"] = AgreementDate,

            ["termination_date"] = TerminationDate,
            ["дата_расторжения"] = TerminationDate,

            ["contract_last_day"] = ContractLastDay,
            ["последний_день"] = ContractLastDay,

            ["contract_clause"] = ContractClause,
            ["пункт_договора"] = ContractClause,

            ["debt_amount"] = DebtAmount,
            ["сумма_задолженности"] = DebtAmount,

            // ── Прочее ───────────────────────────────────────────────────────
            ["object_address"] = ObjectAddress,
            ["адрес_объекта"] = ObjectAddress,

            ["contract_amount"] = ContractAmount,
            ["сумма_договора"] = ContractAmount,

            ["client_type"] = ClientType switch
            {
                ClientType.IndividualEntrepreneur => "ИП",
                ClientType.LegalEntity => "юридическое лицо",
                _ => "физическое лицо"
            },
            ["тип_клиента"] = ClientType switch
            {
                ClientType.IndividualEntrepreneur => "ИП",
                ClientType.LegalEntity => "юридическое лицо",
                _ => "физическое лицо"
            }
        };

        foreach (var (key, value) in ExtraFields)
        {
            if (!string.IsNullOrWhiteSpace(value))
                map[key] = value;
        }

        return map;
    }

    private string GetAddressLine(int index)
    {
        if (string.IsNullOrWhiteSpace(LegalAddress))
            return string.Empty;

        var lines = LegalAddress
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return index < lines.Length ? lines[index] : string.Empty;
    }

    private string BuildContractReference()
    {
        if (!string.IsNullOrWhiteSpace(ContractReference))
            return ContractReference;

        if (string.IsNullOrWhiteSpace(ContractNumber) && string.IsNullOrWhiteSpace(ContractDate))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(ContractDate))
            return ContractNumber;

        if (string.IsNullOrWhiteSpace(ContractNumber))
            return ContractDate;

        return $"№ {ContractNumber} от {ContractDate}";
    }

    private string BuildDirectorRepresentation()
    {
        var parts = new[] { DirectorTitle, DirectorFullName }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();

        return parts.Length == 0 ? string.Empty : string.Join(' ', parts);
    }

    public void MergeFrom(ClientData other)
    {
        if (!string.IsNullOrWhiteSpace(other.FullName)) FullName = other.FullName;
        if (!string.IsNullOrWhiteSpace(other.PassportSeries)) PassportSeries = other.PassportSeries;
        if (!string.IsNullOrWhiteSpace(other.PassportNumber)) PassportNumber = other.PassportNumber;
        if (!string.IsNullOrWhiteSpace(other.PassportIssuedBy)) PassportIssuedBy = other.PassportIssuedBy;
        if (!string.IsNullOrWhiteSpace(other.PassportIssueDate)) PassportIssueDate = other.PassportIssueDate;
        if (!string.IsNullOrWhiteSpace(other.PassportDeptCode)) PassportDeptCode = other.PassportDeptCode;
        if (!string.IsNullOrWhiteSpace(other.RegistrationAddress)) RegistrationAddress = other.RegistrationAddress;
        if (!string.IsNullOrWhiteSpace(other.Inn)) Inn = other.Inn;
        if (!string.IsNullOrWhiteSpace(other.Ogrnip)) Ogrnip = other.Ogrnip;
        if (!string.IsNullOrWhiteSpace(other.IpName)) IpName = other.IpName;
        if (!string.IsNullOrWhiteSpace(other.CompanyName)) CompanyName = other.CompanyName;
        if (!string.IsNullOrWhiteSpace(other.Ogrn)) Ogrn = other.Ogrn;
        if (!string.IsNullOrWhiteSpace(other.Kpp)) Kpp = other.Kpp;
        if (!string.IsNullOrWhiteSpace(other.LegalAddress)) LegalAddress = other.LegalAddress;
        if (!string.IsNullOrWhiteSpace(other.DirectorTitle)) DirectorTitle = other.DirectorTitle;
        if (!string.IsNullOrWhiteSpace(other.DirectorFullName)) DirectorFullName = other.DirectorFullName;
        if (!string.IsNullOrWhiteSpace(other.AuthorityBasis)) AuthorityBasis = other.AuthorityBasis;
        if (!string.IsNullOrWhiteSpace(other.BankAccount)) BankAccount = other.BankAccount;
        if (!string.IsNullOrWhiteSpace(other.BankBik)) BankBik = other.BankBik;
        if (!string.IsNullOrWhiteSpace(other.BankName)) BankName = other.BankName;
        if (!string.IsNullOrWhiteSpace(other.CorrespondentAccount)) CorrespondentAccount = other.CorrespondentAccount;
        if (!string.IsNullOrWhiteSpace(other.Phone)) Phone = other.Phone;
        if (!string.IsNullOrWhiteSpace(other.Email)) Email = other.Email;
        if (!string.IsNullOrWhiteSpace(other.ContractNumber)) ContractNumber = other.ContractNumber;
        if (!string.IsNullOrWhiteSpace(other.ContractDate)) ContractDate = other.ContractDate;
        if (!string.IsNullOrWhiteSpace(other.ContractReference)) ContractReference = other.ContractReference;
        if (!string.IsNullOrWhiteSpace(other.AgreementDate)) AgreementDate = other.AgreementDate;
        if (!string.IsNullOrWhiteSpace(other.TerminationDate)) TerminationDate = other.TerminationDate;
        if (!string.IsNullOrWhiteSpace(other.ContractLastDay)) ContractLastDay = other.ContractLastDay;
        if (!string.IsNullOrWhiteSpace(other.ContractClause)) ContractClause = other.ContractClause;
        if (!string.IsNullOrWhiteSpace(other.DebtAmount)) DebtAmount = other.DebtAmount;
        if (!string.IsNullOrWhiteSpace(other.ObjectAddress)) ObjectAddress = other.ObjectAddress;
        if (!string.IsNullOrWhiteSpace(other.ContractAmount)) ContractAmount = other.ContractAmount;

        foreach (var (key, value) in other.ExtraFields)
        {
            if (!string.IsNullOrWhiteSpace(value))
                ExtraFields[key] = value;
        }

        if (other.ClientType != ClientType.Individual)
            ClientType = other.ClientType;
    }
}
