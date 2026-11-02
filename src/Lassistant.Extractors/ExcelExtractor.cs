using ClosedXML.Excel;
using Lassistant.Core.Config;
using Lassistant.Core.Models;

namespace Lassistant.Extractors;

public sealed record ExcelRowInfo(int RowIndex, string Label);

public sealed class ExcelExtractor : IFileExtractor
{
    private readonly FieldMappingConfig _config;

    public ExcelExtractor(FieldMappingConfig config)
    {
        _config = config;
    }

    public bool CanHandle(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".xlsm", StringComparison.OrdinalIgnoreCase);
    }

    public ClientData Extract(string filePath) => ExtractRow(filePath, _config, 2);

    public static List<ExcelRowInfo> ListDataRows(string filePath, FieldMappingConfig config)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.First();
        var usedRange = worksheet.RangeUsed();
        if (usedRange is null)
            return [];

        var rows = usedRange.RowsUsed().Skip(1).ToList();
        var result = new List<ExcelRowInfo>();

        foreach (var row in rows)
        {
            var label = BuildRowLabel(row, config);
            result.Add(new ExcelRowInfo(row.RowNumber(), label));
        }

        return result;
    }

    public static ClientData ExtractRow(string filePath, FieldMappingConfig config, int rowIndex)
    {
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.First();
        var usedRange = worksheet.RangeUsed();
        if (usedRange is null)
            return new ClientData();

        var headerRow = usedRange.FirstRow();
        var columnMap = BuildColumnMap(headerRow);
        var dataRow = usedRange.RowsUsed().FirstOrDefault(r => r.RowNumber() == rowIndex)
            ?? usedRange.RowsUsed().Skip(1).FirstOrDefault();

        if (dataRow is null)
            return new ClientData();

        var data = ExtractFromRow(dataRow, columnMap, config);
        MergeLegalAddressLines(data, dataRow, columnMap, config);
        return data;
    }

    private static Dictionary<string, int> BuildColumnMap(IXLRangeRow headerRow)
    {
        var columnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            var header = cell.GetString().Trim();
            if (!string.IsNullOrEmpty(header))
                columnMap[header] = cell.Address.ColumnNumber;
        }
        return columnMap;
    }

    private static ClientData ExtractFromRow(
        IXLRangeRow dataRow,
        Dictionary<string, int> columnMap,
        FieldMappingConfig config)
    {
        var data = new ClientData();

        foreach (var (fieldName, labels) in config.Labels)
        {
            if (fieldName is "LegalAddressLine2" or "LegalAddressLine3")
                continue;

            foreach (var label in labels)
            {
                if (!columnMap.TryGetValue(label, out var col))
                    continue;

                var value = dataRow.Cell(col).GetString().Trim();
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                ApplyField(data, fieldName, value);
                break;
            }
        }

        return data;
    }

    private static string BuildRowLabel(IXLRangeRow row, FieldMappingConfig config)
    {
        var headerRow = row.Worksheet.RangeUsed()?.FirstRow();
        if (headerRow is null)
            return "(без названия)";

        var columnMap = BuildColumnMap(headerRow);
        foreach (var key in new[] { "FullName", "CompanyName", "IpName" })
        {
            if (!config.Labels.TryGetValue(key, out var labels))
                continue;

            foreach (var label in labels)
            {
                if (!columnMap.TryGetValue(label, out var col))
                    continue;

                var value = row.Cell(col).GetString().Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return "(без названия)";
    }

    private static void ApplyField(ClientData data, string fieldName, string value)
    {
        switch (fieldName)
        {
            case "FullName": data.FullName = value; break;
            case "PassportSeries": data.PassportSeries = DigitsOnly(value, 4); break;
            case "PassportNumber": data.PassportNumber = DigitsOnly(value, 6); break;
            case "PassportIssuedBy": data.PassportIssuedBy = value; break;
            case "PassportIssueDate": data.PassportIssueDate = value; break;
            case "PassportDeptCode": data.PassportDeptCode = value; break;
            case "RegistrationAddress": data.RegistrationAddress = value; break;
            case "Inn": data.Inn = DigitsOnly(value); break;
            case "Ogrnip": data.Ogrnip = DigitsOnly(value); break;
            case "Ogrn": data.Ogrn = DigitsOnly(value); break;
            case "Kpp": data.Kpp = DigitsOnly(value, 9); break;
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
            case "BankAccount": data.BankAccount = DigitsOnly(value); break;
            case "BankBik": data.BankBik = DigitsOnly(value, 9); break;
            case "BankName": data.BankName = value; break;
            case "CorrespondentAccount": data.CorrespondentAccount = DigitsOnly(value); break;
            case "Phone": data.Phone = value; break;
            case "Email": data.Email = value; break;
            case "ContractNumber": data.ContractNumber = value; break;
            case "ContractDate": data.ContractDate = value; break;
            case "ContractReference": data.ContractReference = value; break;
            case "AgreementDate": data.AgreementDate = value; break;
            case "TerminationDate": data.TerminationDate = value; break;
            case "ContractLastDay": data.ContractLastDay = value; break;
            case "ContractClause": data.ContractClause = value; break;
            case "DebtAmount": data.DebtAmount = value; break;
            case "ObjectAddress": data.ObjectAddress = value; break;
            case "ContractAmount": data.ContractAmount = value; break;
        }

        if (fieldName is "Inn" or "Ogrnip" or "IpName")
            data.ClientType = ClientType.IndividualEntrepreneur;

        if (fieldName is "CompanyName" or "Ogrn" or "Kpp")
            data.ClientType = ClientType.LegalEntity;
    }

    private static void MergeLegalAddressLines(
        ClientData data,
        IXLRangeRow dataRow,
        Dictionary<string, int> columnMap,
        FieldMappingConfig config)
    {
        foreach (var fieldName in new[] { "LegalAddressLine2", "LegalAddressLine3" })
        {
            if (!config.Labels.TryGetValue(fieldName, out var labels))
                continue;

            foreach (var label in labels)
            {
                if (!columnMap.TryGetValue(label, out var col))
                    continue;

                AppendLegalAddressLine(data, dataRow.Cell(col).GetString().Trim());
                break;
            }
        }
    }

    private static void AppendLegalAddressLine(ClientData data, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        data.LegalAddress = string.IsNullOrWhiteSpace(data.LegalAddress)
            ? value
            : $"{data.LegalAddress}\n{value}";
    }

    private static string DigitsOnly(string value, int? max = null)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return max.HasValue && digits.Length > max.Value ? digits[..max.Value] : digits;
    }
}
