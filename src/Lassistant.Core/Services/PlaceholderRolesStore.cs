using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lassistant.Core.Services;

/// <summary>
/// Пользовательские NER-роли в user/settings/ner-user-roles.json (не затирается publish).
/// Канон поставки остаётся в config/placeholder-roles.json.
/// </summary>
public static class PlaceholderRolesStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
    };

    public static event EventHandler? Changed;

    public static IReadOnlyList<string> LoadCanonRoles() =>
        ReadRoleNamesFromFile(AppPaths.PlaceholderRolesPath);

    public static IReadOnlyList<string> LoadRoles()
    {
        lock (Gate)
            return ReadRoleNamesFromFile(AppPaths.NerUserRolesPath);
    }

    public static bool Contains(string role)
    {
        if (string.IsNullOrWhiteSpace(role)) return false;
        var needle = role.Trim();
        return LoadRoles().Any(r => string.Equals(r, needle, StringComparison.OrdinalIgnoreCase))
               || LoadCanonRoles().Any(r => string.Equals(r, needle, StringComparison.OrdinalIgnoreCase));
    }

    public static bool TryAdd(string role, out string? error) =>
        TryAdd(role, source: "templates", out error);

    public static bool TryAdd(string role, string source, out string? error)
    {
        error = null;
        var trimmed = (role ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            error = "Пустое имя роли.";
            return false;
        }

        lock (Gate)
        {
            var path = AppPaths.NerUserRolesPath;
            var entries = ReadEntriesFromFile(path);
            if (entries.Any(e => string.Equals(e.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                error = null;
                return false;
            }

            entries.Add(new UserRoleEntry
            {
                Name = trimmed,
                AddedAt = DateTimeOffset.Now.ToString("O"),
                Source = string.IsNullOrWhiteSpace(source) ? "templates" : source.Trim()
            });
            entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            try
            {
                WriteEntriesUnlocked(path, entries);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        Changed?.Invoke(null, EventArgs.Empty);
        return true;
    }

    public static void WriteEmptyFile(string path)
    {
        lock (Gate)
            WriteEntriesUnlocked(path, []);
    }

    private static List<string> ReadRoleNamesFromFile(string path) =>
        ReadEntriesFromFile(path)
            .Select(e => e.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<UserRoleEntry> ReadEntriesFromFile(string path)
    {
        if (!File.Exists(path))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            if (!doc.RootElement.TryGetProperty("roles", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
                return [];

            var list = new List<UserRoleEntry>();
            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var name = item.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                        list.Add(new UserRoleEntry { Name = name.Trim() });
                    continue;
                }

                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                var roleName = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(roleName))
                    continue;
                list.Add(new UserRoleEntry
                {
                    Name = roleName.Trim(),
                    AddedAt = item.TryGetProperty("added_at", out var at) ? at.GetString() ?? "" : "",
                    Source = item.TryGetProperty("source", out var src) ? src.GetString() ?? "" : ""
                });
            }

            return list;
        }
        catch
        {
            return [];
        }
    }

    private static void WriteEntriesUnlocked(string path, IReadOnlyList<UserRoleEntry> entries)
    {
        AppPaths.EnsureDirectory(path);
        var file = new UserRolesFile
        {
            Version = 1,
            Product = "Lassistant",
            UpdatedAt = DateTimeOffset.Now.ToString("O"),
            Roles = entries.Select(e => new UserRoleEntry
            {
                Name = e.Name,
                AddedAt = string.IsNullOrWhiteSpace(e.AddedAt) ? DateTimeOffset.Now.ToString("O") : e.AddedAt,
                Source = string.IsNullOrWhiteSpace(e.Source) ? "templates" : e.Source
            }).ToList()
        };
        var json = JsonSerializer.Serialize(file, JsonWriteOptions);
        File.WriteAllText(path, json + Environment.NewLine, Utf8NoBom);
    }

    private sealed class UserRolesFile
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("product")]
        public string Product { get; set; } = "Lassistant";

        [JsonPropertyName("updated_at")]
        public string UpdatedAt { get; set; } = "";

        [JsonPropertyName("roles")]
        public List<UserRoleEntry> Roles { get; set; } = [];
    }

    private sealed class UserRoleEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("added_at")]
        public string AddedAt { get; set; } = "";

        [JsonPropertyName("source")]
        public string Source { get; set; } = "";
    }
}
