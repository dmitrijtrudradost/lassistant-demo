using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lassistant.Core.Services;

namespace Lassistant.Core.Config;

public sealed class ExecutorCard
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Реквизиты для подстановки в {{исполнитель_*}}.</summary>
    [JsonPropertyName("fields")]
    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public ExecutorCard Clone()
    {
        var copy = new ExecutorCard
        {
            Id = Id,
            DisplayName = DisplayName,
            Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
        foreach (var kv in Fields)
            copy.Fields[kv.Key] = kv.Value;
        return copy;
    }
}

public sealed class ExecutorsConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    [JsonPropertyName("defaultExecutorId")]
    public string DefaultExecutorId { get; set; } = string.Empty;

    [JsonPropertyName("executors")]
    public List<ExecutorCard> Executors { get; set; } = [];

    /// <summary>Пустой шаблон карточки: ключи поставки, без реквизитов.</summary>
    public static readonly string[] StandardFieldKeys =
    [
        "исполнитель_наименование",
        "исполнитель_наименование_кратко",
        "исполнитель_инн",
        "исполнитель_кпп",
        "исполнитель_огрн",
        "исполнитель_окпо",
        "исполнитель_адрес_юридический",
        "исполнитель_адрес_почтовый",
        "исполнитель_банк",
        "исполнитель_город_банка",
        "исполнитель_бик",
        "исполнитель_расчетный_счет",
        "исполнитель_корр_счет",
        "исполнитель_телефон",
        "исполнитель_телефон_дежурного",
        "исполнитель_email",
        "исполнитель_номер_лицензии",
        "исполнитель_старый_номер_лицензии",
        "исполнитель_дата_лицензии",
        "исполнитель_лицензия_кем_выдана",
        "исполнитель_срок_действия_лицензии",
        "исполнитель_регион_лицензии",
        "исполнитель_директор_фио_падеж",
        "исполнитель_директор_фио_кратко",
        "исполнитель_фио_кратко"
    ];

    public static Dictionary<string, string> CreateEmptyStandardFields()
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in StandardFieldKeys)
            fields[key] = "";
        return fields;
    }

    public static ExecutorsConfig LoadFactory() => LoadFile(AppPaths.ExecutorsPath);

    public static ExecutorsConfig LoadUser() => LoadFile(AppPaths.UserExecutorsPath);

    public static ExecutorsConfig Load(string? path = null)
    {
        if (path is not null)
            return LoadFile(path);

        var factory = LoadFactory();
        var user = LoadUser();
        var mode = UserSettings.NormalizeMode(UserSettingsStore.Current.ExecutorsMode);

        if (mode == UserSettings.ModeUserOnly)
        {
            if (user.Executors.Count == 0)
                return user;
            return user;
        }

        return Merge(factory, user);
    }

    public static void SaveUser(ExecutorsConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var path = AppPaths.UserExecutorsPath;
        AppPaths.EnsureDirectory(path);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(path, json + Environment.NewLine, Utf8NoBom);
    }

    public static void DeleteUserFile()
    {
        var path = AppPaths.UserExecutorsPath;
        if (File.Exists(path))
            File.Delete(path);
    }

    public ExecutorCard? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        return Executors.FirstOrDefault(e =>
            string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public ExecutorCard? GetDefault(string? preferredId = null)
    {
        return Find(preferredId)
               ?? Find(DefaultExecutorId)
               ?? Executors.FirstOrDefault();
    }

    private static ExecutorsConfig LoadFile(string path)
    {
        if (!File.Exists(path))
            return new ExecutorsConfig();

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<ExecutorsConfig>(json, JsonOptions) ?? new ExecutorsConfig();
        }
        catch
        {
            return new ExecutorsConfig();
        }
    }

    private static ExecutorsConfig Merge(ExecutorsConfig factory, ExecutorsConfig user)
    {
        var map = new Dictionary<string, ExecutorCard>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in factory.Executors)
        {
            if (string.IsNullOrWhiteSpace(card.Id)) continue;
            map[card.Id] = card.Clone();
        }

        foreach (var card in user.Executors)
        {
            if (string.IsNullOrWhiteSpace(card.Id)) continue;
            map[card.Id] = card.Clone();
        }

        return new ExecutorsConfig
        {
            DefaultExecutorId = string.IsNullOrWhiteSpace(user.DefaultExecutorId)
                ? factory.DefaultExecutorId
                : user.DefaultExecutorId,
            Executors = map.Values.ToList()
        };
    }
}
