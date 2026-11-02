using System.Text;
using System.Text.RegularExpressions;
using Lassistant.Core.Models;

namespace Lassistant.Core.Services;

public sealed class CustomerCardMergeResult
{
    public string FilePath { get; init; } = string.Empty;
    public int Added { get; init; }
    public int Updated { get; init; }
    public int Skipped { get; init; }
    public bool CreatedNew { get; init; }
}

/// <summary>
/// Локальные карточки заказчика в data/заказчики/{тип}_{название}.md
/// </summary>
public static class CustomerCardStore
{
    /// <summary>Поля договора/объекта, сохраняемые в карточке вместе с заказчик_*.</summary>
    private static readonly HashSet<string> ContractObjectKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "дата_договора",
        "номер_договора",
        "объект_адрес",
        "объект_дата_отключения",
        "объект_дата_подключения",
        "цена_прописью",
        "цена_числом"
    };

    public static bool IsCardField(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;
        if (key.StartsWith("заказчик_", StringComparison.OrdinalIgnoreCase))
            return true;
        if (key.StartsWith("объект_", StringComparison.OrdinalIgnoreCase))
            return true;
        if (key.StartsWith("цена_", StringComparison.OrdinalIgnoreCase))
            return true;
        return ContractObjectKeys.Contains(key);
    }

    public static Dictionary<string, string> CollectCardFields(IReadOnlyDictionary<string, string> formValues) =>
        formValues
            .Where(kv => IsCardField(kv.Key))
            .ToDictionary(
                kv => kv.Key,
                kv => kv.Value?.Trim() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

    public static string GetTypeLabel(ClientType clientType) => clientType switch
    {
        ClientType.IndividualEntrepreneur => "ИП",
        ClientType.LegalEntity => "Юрлицо",
        _ => "Физлицо"
    };

    /// <summary>
    /// Имя для файла карточки — тот же приоритет, что у выходного DOCX:
    /// наименование_кратко → фио_кратко → наименование → фио.
    /// </summary>
    public static string ResolveCustomerNameForFile(IReadOnlyDictionary<string, string> formValues) =>
        Get(formValues, "заказчик_наименование_кратко")
        ?? Get(formValues, "заказчик_фио_кратко")
        ?? Get(formValues, "заказчик_наименование")
        ?? Get(formValues, "заказчик_фио")
        ?? Get(formValues, "FullName")
        ?? Get(formValues, "IpName")
        ?? Get(formValues, "CompanyName")
        ?? "без_названия";

    public static string BuildFileName(ClientType clientType, IReadOnlyDictionary<string, string> formValues)
    {
        var type = GetTypeLabel(clientType);
        var name = ResolveCustomerNameForFile(formValues);
        if (clientType == ClientType.IndividualEntrepreneur)
            name = StripLeadingIpPrefix(name);
        return $"{Sanitize(type)}_{Sanitize(name)}.md";
    }

    /// <summary>Убирает ведущее «ИП» из краткого имени, чтобы не было ИП_ИП_…</summary>
    private static string StripLeadingIpPrefix(string name)
    {
        var trimmed = name.Trim();
        if (!trimmed.StartsWith("ИП", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        var rest = trimmed[2..].TrimStart(' ', '.', ',', '-', '—', '«', '»', '"', '\'');
        return string.IsNullOrWhiteSpace(rest) ? trimmed : rest;
    }

    public static string GetCardPath(ClientType clientType, IReadOnlyDictionary<string, string> formValues)
    {
        Directory.CreateDirectory(AppPaths.CustomerCardsDirectory);
        return Path.Combine(AppPaths.CustomerCardsDirectory, BuildFileName(clientType, formValues));
    }

    public static Dictionary<string, string> LoadFields(string path)
    {
        LoadCard(path, out _, out var fields);
        return fields;
    }

    /// <summary>Читает YAML front matter (type) и поля карточки.</summary>
    public static bool LoadCard(
        string path,
        out string? typeLabel,
        out Dictionary<string, string> fields)
    {
        typeLabel = null;
        fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
            return false;

        var inFrontMatter = false;
        var frontMatterSeen = false;

        foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
        {
            var line = raw.Trim();
            if (line is "---")
            {
                if (!frontMatterSeen)
                {
                    inFrontMatter = true;
                    frontMatterSeen = true;
                }
                else if (inFrontMatter)
                {
                    inFrontMatter = false;
                }

                continue;
            }

            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var idx = line.IndexOf(':');
            if (idx <= 0)
                continue;

            var key = line[..idx].Trim();
            var value = Unquote(line[(idx + 1)..].Trim());

            if (inFrontMatter)
            {
                if (key.Equals("type", StringComparison.OrdinalIgnoreCase))
                    typeLabel = value;
                continue;
            }

            fields[key] = value;
        }

        return true;
    }

    public static bool TryParseClientType(string? typeLabel, out ClientType clientType)
    {
        clientType = ClientType.Individual;
        if (string.IsNullOrWhiteSpace(typeLabel))
            return false;

        var t = typeLabel.Trim();
        if (t.Equals("ИП", StringComparison.OrdinalIgnoreCase)
            || t.Contains("предприниматель", StringComparison.OrdinalIgnoreCase))
        {
            clientType = ClientType.IndividualEntrepreneur;
            return true;
        }

        if (t.Equals("ООО", StringComparison.OrdinalIgnoreCase)
            || t.Contains("юр", StringComparison.OrdinalIgnoreCase)
            || t.Contains("ооо", StringComparison.OrdinalIgnoreCase))
        {
            clientType = ClientType.LegalEntity;
            return true;
        }

        if (t.Contains("физ", StringComparison.OrdinalIgnoreCase)
            || t.Equals("ФЛ", StringComparison.OrdinalIgnoreCase))
        {
            clientType = ClientType.Individual;
            return true;
        }

        return false;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
        {
            return value[1..^1]
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        return value;
    }

    public static void Save(
        string path,
        ClientType clientType,
        IReadOnlyDictionary<string, string> fields)
    {
        AppPaths.EnsureDirectory(path);
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"type: {GetTypeLabel(clientType)}");
        sb.AppendLine($"updated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("---");
        sb.AppendLine();

        foreach (var key in fields.Keys
                     .OrderBy(CardFieldSortOrder)
                     .ThenBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var value = fields[key] ?? string.Empty;
            if (value.Contains('\n') || value.Contains(':') || value.Contains('"'))
            {
                var escaped = value
                    .Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("\"", "\\\"", StringComparison.Ordinal)
                    .Replace("\n", "\\n", StringComparison.Ordinal);
                sb.AppendLine($"{key}: \"{escaped}\"");
            }
            else
            {
                sb.AppendLine($"{key}: {value}");
            }
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// Merge: empty card + non-empty form → fill;
    /// both filled (and different) → ask via <paramref name="confirmReplace"/>;
    /// empty form → keep card.
    /// </summary>
    public static CustomerCardMergeResult MergeAndSave(
        ClientType clientType,
        IReadOnlyDictionary<string, string> formValues,
        Func<string, string, string, bool> confirmReplace)
    {
        var customerFields = CollectCardFields(formValues);

        var path = GetCardPath(clientType, formValues);
        var existing = LoadFields(path);
        var createdNew = !File.Exists(path);

        var merged = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var (key, formValue) in customerFields)
        {
            existing.TryGetValue(key, out var cardValue);
            cardValue ??= string.Empty;
            var formEmpty = string.IsNullOrWhiteSpace(formValue);
            var cardEmpty = string.IsNullOrWhiteSpace(cardValue);

            if (cardEmpty && !formEmpty)
            {
                merged[key] = formValue;
                added++;
                continue;
            }

            if (!cardEmpty && formEmpty)
            {
                skipped++;
                continue;
            }

            if (cardEmpty && formEmpty)
            {
                if (!merged.ContainsKey(key))
                    merged[key] = string.Empty;
                continue;
            }

            if (string.Equals(cardValue, formValue, StringComparison.Ordinal))
            {
                skipped++;
                continue;
            }

            if (confirmReplace(key, cardValue, formValue))
            {
                merged[key] = formValue;
                updated++;
            }
            else
            {
                skipped++;
            }
        }

        foreach (var (key, value) in existing)
        {
            if (!merged.ContainsKey(key) && IsCardField(key))
                merged[key] = value;
        }

        // Drop non-card keys if any leaked into older files — keep only card fields + leftovers that are card fields
        var toSave = merged
            .Where(kv => IsCardField(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        Save(path, clientType, toSave);
        return new CustomerCardMergeResult
        {
            FilePath = path,
            Added = added,
            Updated = updated,
            Skipped = skipped,
            CreatedNew = createdNew
        };
    }

    private static int CardFieldSortOrder(string key)
    {
        if (key.StartsWith("заказчик_", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (ContractObjectKeys.Contains(key)
            || key.StartsWith("объект_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("цена_", StringComparison.OrdinalIgnoreCase)
            || key.Contains("договор", StringComparison.OrdinalIgnoreCase))
            return 1;
        return 2;
    }

    private static string? Get(IReadOnlyDictionary<string, string> map, string key) =>
        map.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch =>
        {
            if (ch is '"' or '«' or '»' or '\'')
                return ' ';
            if (invalid.Contains(ch))
                return '_';
            return ch;
        }).ToArray());

        cleaned = string.Join("_", cleaned.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
        cleaned = Regex.Replace(cleaned, "_+", "_").Trim('_', '.');
        return string.IsNullOrWhiteSpace(cleaned) ? "без_названия" : cleaned;
    }
}
