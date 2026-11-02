using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lassistant.Core.Config;

public sealed class FieldDefinition
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("placeholder")]
    public string Placeholder { get; set; } = string.Empty;

    [JsonPropertyName("validator")]
    public string Validator { get; set; } = "none";
}

public sealed class FieldGroupDefinition
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("fields")]
    public string[] Fields { get; set; } = [];
}

public sealed class FieldProfileDefinition
{
    [JsonPropertyName("groups")]
    public string[] Groups { get; set; } = [];

    [JsonPropertyName("required")]
    public string[] Required { get; set; } = [];
}

public sealed class FieldDefinitionsConfig
{
    [JsonPropertyName("fields")]
    public Dictionary<string, FieldDefinition> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("groups")]
    public Dictionary<string, FieldGroupDefinition> Groups { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("profiles")]
    public Dictionary<string, FieldProfileDefinition> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static FieldDefinitionsConfig Load(string path)
    {
        if (!File.Exists(path))
            return new FieldDefinitionsConfig();

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<FieldDefinitionsConfig>(json) ?? new FieldDefinitionsConfig();
    }

    public IEnumerable<(string GroupKey, FieldGroupDefinition Group, string[] FieldKeys)> GetProfileGroups(string profileName)
    {
        if (!Profiles.TryGetValue(profileName, out var profile))
            yield break;

        foreach (var groupKey in profile.Groups)
        {
            if (Groups.TryGetValue(groupKey, out var group))
                yield return (groupKey, group, group.Fields);
        }
    }

    public bool IsRequired(string profileName, string fieldKey) =>
        Profiles.TryGetValue(profileName, out var profile)
        && profile.Required.Contains(fieldKey, StringComparer.OrdinalIgnoreCase);
}
