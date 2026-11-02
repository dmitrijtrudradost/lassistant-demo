using System.Text.RegularExpressions;
using Lassistant.Core.Config;
using Lassistant.Core.Models;

namespace Lassistant.Extractors;

public sealed partial class LabelValueParser
{
    private readonly FieldMappingConfig _config;

    public LabelValueParser(FieldMappingConfig config)
    {
        _config = config;
    }

    public ClientData ParseFromText(string text)
    {
        var data = new ClientData();
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var (fieldName, labels) in _config.Labels)
        {
            var value = ExtractByLabels(text, lines, labels);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            ApplyField(data, fieldName, value);
        }

        ExtractPatterns(data, text);
        return data;
    }

    private static string? ExtractByLabels(string fullText, string[] lines, string[] labels)
    {
        foreach (var label in labels)
        {
            foreach (var line in lines)
            {
                var pattern = $@"{Regex.Escape(label)}\s*[:\-—]?\s*(.+)$";
                var match = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                    return match.Groups[1].Value.Trim();
            }

            var inlinePattern = $@"{Regex.Escape(label)}\s*[:\-—]\s*([^\r\n]+)";
            var inlineMatch = Regex.Match(fullText, inlinePattern, RegexOptions.IgnoreCase);
            if (inlineMatch.Success)
                return inlineMatch.Groups[1].Value.Trim();
        }

        return null;
    }

    private static void ExtractPatterns(ClientData data, string text)
    {
        if (string.IsNullOrWhiteSpace(data.Inn))
        {
            var innMatch = InnRegex().Match(text);
            if (innMatch.Success)
                data.Inn = innMatch.Value;
        }

        if (string.IsNullOrWhiteSpace(data.Ogrnip))
        {
            var ogrnipMatch = OgrnipRegex().Match(text);
            if (ogrnipMatch.Success)
                data.Ogrnip = ogrnipMatch.Value;
        }

        if (string.IsNullOrWhiteSpace(data.Ogrn))
        {
            var ogrnMatch = OgrnRegex().Match(text);
            if (ogrnMatch.Success)
            {
                data.Ogrn = ogrnMatch.Value;
                data.ClientType = ClientType.LegalEntity;
            }
        }

        if (string.IsNullOrWhiteSpace(data.Kpp))
        {
            var kppMatch = KppRegex().Match(text);
            if (kppMatch.Success)
                data.Kpp = kppMatch.Value;
        }

        if (string.IsNullOrWhiteSpace(data.PassportSeries) || string.IsNullOrWhiteSpace(data.PassportNumber))
        {
            var passportMatch = PassportRegex().Match(text);
            if (passportMatch.Success)
            {
                if (passportMatch.Groups[4].Success)
                {
                    data.PassportSeries = passportMatch.Groups[4].Value;
                    data.PassportNumber = passportMatch.Groups[5].Value;
                }
                else
                {
                    data.PassportSeries = passportMatch.Groups[1].Value + passportMatch.Groups[2].Value;
                    data.PassportNumber = passportMatch.Groups[3].Value;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(data.PassportDeptCode))
        {
            var deptMatch = DeptCodeRegex().Match(text);
            if (deptMatch.Success)
                data.PassportDeptCode = deptMatch.Value;
        }

        if (string.IsNullOrWhiteSpace(data.BankBik))
        {
            var bikMatch = BikRegex().Match(text);
            if (bikMatch.Success)
                data.BankBik = bikMatch.Value;
        }

        if (string.IsNullOrWhiteSpace(data.BankAccount))
        {
            var accountMatch = BankAccountRegex().Match(text);
            if (accountMatch.Success)
                data.BankAccount = accountMatch.Value;
        }
    }

    private static void ApplyField(ClientData data, string fieldName, string value)
    {
        switch (fieldName)
        {
            case "FullName": data.FullName = value; break;
            case "PassportSeries": data.PassportSeries = NormalizeDigits(value, 4); break;
            case "PassportNumber": data.PassportNumber = NormalizeDigits(value, 6); break;
            case "PassportIssuedBy": data.PassportIssuedBy = value; break;
            case "PassportIssueDate": data.PassportIssueDate = value; break;
            case "PassportDeptCode": data.PassportDeptCode = value; break;
            case "RegistrationAddress": data.RegistrationAddress = value; break;
            case "Inn": data.Inn = NormalizeDigits(value); break;
            case "Ogrnip": data.Ogrnip = NormalizeDigits(value); break;
            case "Ogrn":
                data.Ogrn = NormalizeDigits(value);
                data.ClientType = ClientType.LegalEntity;
                break;
            case "Kpp": data.Kpp = NormalizeDigits(value, 9); break;
            case "IpName":
                data.IpName = value;
                data.ClientType = ClientType.IndividualEntrepreneur;
                break;
            case "CompanyName":
                data.CompanyName = value;
                data.ClientType = ClientType.LegalEntity;
                break;
            case "LegalAddress": data.LegalAddress = value; break;
            case "DirectorTitle": data.DirectorTitle = value; break;
            case "DirectorFullName": data.DirectorFullName = value; break;
            case "AuthorityBasis": data.AuthorityBasis = value; break;
            case "ContractNumber": data.ContractNumber = value; break;
            case "ContractDate": data.ContractDate = value; break;
            case "ObjectAddress": data.ObjectAddress = value; break;
            case "ContractAmount": data.ContractAmount = value; break;
            case "BankAccount": data.BankAccount = NormalizeDigits(value); break;
            case "BankBik": data.BankBik = NormalizeDigits(value, 9); break;
            case "BankName": data.BankName = value; break;
            case "CorrespondentAccount": data.CorrespondentAccount = NormalizeDigits(value); break;
            case "Phone": data.Phone = value; break;
            case "Email": data.Email = value; break;
        }

        if (fieldName is "Inn" or "Ogrnip" or "IpName" or "BankAccount")
            data.ClientType = ClientType.IndividualEntrepreneur;

        if (fieldName is "CompanyName" or "Ogrn" or "Kpp")
            data.ClientType = ClientType.LegalEntity;
    }

    private static string NormalizeDigits(string value, int? maxLength = null)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return maxLength.HasValue && digits.Length > maxLength.Value
            ? digits[..maxLength.Value]
            : digits;
    }

    [GeneratedRegex(@"\b(\d{12}|\d{10})\b")]
    private static partial Regex InnRegex();

    [GeneratedRegex(@"\b3\d{14}\b")]
    private static partial Regex OgrnipRegex();

    [GeneratedRegex(@"\b[15]\d{12}\b")]
    private static partial Regex OgrnRegex();

    [GeneratedRegex(@"\b\d{9}\b")]
    private static partial Regex KppRegex();

    [GeneratedRegex(@"\b(\d{2})\s?(\d{2})\s?(\d{6})\b|\b(\d{4})\s?(\d{6})\b")]
    private static partial Regex PassportRegex();

    [GeneratedRegex(@"\b\d{3}-\d{3}\b")]
    private static partial Regex DeptCodeRegex();

    [GeneratedRegex(@"\b\d{9}\b")]
    private static partial Regex BikRegex();

    [GeneratedRegex(@"\b\d{20}\b")]
    private static partial Regex BankAccountRegex();
}
