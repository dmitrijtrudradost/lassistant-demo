using Lassistant.Core.Models;
using Lassistant.Core.Services;

namespace Lassistant.Core.Config;

public sealed class DocumentTemplateResolver
{
    private readonly DocumentCatalogConfig _catalog;

    public DocumentTemplateResolver(DocumentCatalogConfig catalog)
    {
        _catalog = catalog;
    }

    public IReadOnlyList<DocumentCatalogEntry> GetAll() => _catalog.Documents;

    public IEnumerable<DocumentKind> GetDocumentKinds() =>
        _catalog.Documents.Select(d => d.DocumentKind).Distinct();

    public IEnumerable<ClientType> GetClientTypes(DocumentKind kind) =>
        _catalog.Documents.Where(d => d.DocumentKind == kind).Select(d => d.ClientType).Distinct();

    public IEnumerable<ServiceKind> GetServiceKinds(DocumentKind kind, ClientType clientType) =>
        _catalog.Documents
            .Where(d => d.DocumentKind == kind && d.ClientType == clientType)
            .Select(d => d.ServiceKind)
            .Distinct();

    public DocumentCatalogEntry? Resolve(DocumentKind kind, ClientType clientType, ServiceKind service) =>
        _catalog.Documents.FirstOrDefault(d =>
            d.DocumentKind == kind
            && d.ClientType == clientType
            && d.ServiceKind == service);

    public DocumentCatalogEntry? ResolveById(string id) =>
        _catalog.Documents.FirstOrDefault(d =>
            string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

    public string GetTemplatePath(DocumentCatalogEntry entry) =>
        Path.Combine(AppPaths.TemplatesDirectory, entry.TemplateFile);

    /// <summary>
    /// Стандарт: типдокумента_заказчик_кратко_типуслуги_yyyyMMdd_HHmmss.docx
    /// </summary>
    public string BuildOutputFileName(DocumentCatalogEntry entry, ClientData data)
    {
        var map = data.ToPlaceholderMap();
        var docKind = SanitizeFileNameSegment(
            DisplayNames.ForDocumentKind(entry.DocumentKind).ToLowerInvariant().Replace(' ', '_'));
        var customer = SanitizeFileNameSegment(ResolveCustomerShort(map, data));
        var service = SanitizeFileNameSegment(ServiceKindFileToken(entry.ServiceKind));
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        return SanitizeFileName($"{docKind}_{customer}_{service}_{stamp}.docx");
    }

    private static string ResolveCustomerShort(IReadOnlyDictionary<string, string> map, ClientData data)
    {
        foreach (var key in new[]
                 {
                     "заказчик_наименование_кратко",
                     "заказчик_фио_кратко",
                     "заказчик_наименование",
                     "заказчик_фио",
                     "company_name",
                     "client_full_name"
                 })
        {
            if (TryGetNonEmpty(map, key, out var fromMap))
                return fromMap;
        }

        if (!string.IsNullOrWhiteSpace(data.CompanyName))
            return data.CompanyName.Trim();
        if (!string.IsNullOrWhiteSpace(data.FullName))
            return data.FullName.Trim();

        return "без_заказчика";
    }

    private static bool TryGetNonEmpty(
        IReadOnlyDictionary<string, string> map,
        string key,
        out string value)
    {
        if (map.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            value = raw.Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string ServiceKindFileToken(ServiceKind service) => service switch
    {
        ServiceKind.Montage => "монтаж",
        ServiceKind.FireAlarm => "пс",
        ServiceKind.Gbr => "гбр",
        ServiceKind.SecurityPost => "пост_охраны",
        _ => service.ToString().ToLowerInvariant()
    };

    private static string SanitizeFileNameSegment(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) || ch is ' ' or '/' or '\\' ? '_' : ch).ToArray())
            .Trim('_', ' ', '.');
        while (cleaned.Contains("__", StringComparison.Ordinal))
            cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(cleaned) ? "без_имени" : cleaned;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "документ.docx" : cleaned;
    }
}
