using System.Text.Json;
using Lassistant.Core.Services;

namespace Lassistant.Templates;

public static class MappingFileService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Save(AnonymizationMapping mapping, string path)
    {
        AppPaths.EnsureDirectory(path);
        var json = JsonSerializer.Serialize(mapping, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static AnonymizationMapping Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AnonymizationMapping>(json, JsonOptions)
               ?? throw new InvalidOperationException("Некорректный файл mapping.");
    }
}
