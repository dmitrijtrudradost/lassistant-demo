using System.Text.Json;
using System.Text.Json.Serialization;
using Lassistant.Core.Models;

namespace Lassistant.Core.Config;

public sealed class DocumentCatalogEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("documentKind")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DocumentKind DocumentKind { get; set; }

    [JsonPropertyName("clientType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ClientType ClientType { get; set; }

    [JsonPropertyName("serviceKind")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ServiceKind ServiceKind { get; set; }

    [JsonPropertyName("templateFile")]
    public string TemplateFile { get; set; } = string.Empty;

    [JsonPropertyName("fieldProfile")]
    public string FieldProfile { get; set; } = string.Empty;

    [JsonPropertyName("outputNamePattern")]
    public string OutputNamePattern { get; set; } = string.Empty;

    [JsonPropertyName("isStub")]
    public bool IsStub { get; set; }

    [JsonPropertyName("executorId")]
    public string? ExecutorId { get; set; }

    [JsonPropertyName("projectId")]
    public string? ProjectId { get; set; }
}

public sealed class DocumentCatalogConfig
{
    [JsonPropertyName("documents")]
    public List<DocumentCatalogEntry> Documents { get; set; } = [];

    public static DocumentCatalogConfig Load(string path)
    {
        if (!File.Exists(path))
            return new DocumentCatalogConfig();

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<DocumentCatalogConfig>(json) ?? new DocumentCatalogConfig();
    }
}
