using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lassistant.Core.Services;

/// <summary>Личные пути и режим исполнителей: user/settings/user-settings.json.</summary>
public sealed class UserSettings
{
    public const string ModeMerge = "merge";
    public const string ModeUserOnly = "user_only";

    [JsonPropertyName("rolesFile")]
    public string RolesFile { get; set; } = AppPaths.DefaultNerUserRolesRelative;

    [JsonPropertyName("customerCardsDirectory")]
    public string CustomerCardsDirectory { get; set; } = AppPaths.DefaultCustomerCardsRelative;

    /// <summary>merge = поставка + user; user_only = только user/settings/executors.json.</summary>
    [JsonPropertyName("executorsMode")]
    public string ExecutorsMode { get; set; } = ModeMerge;

    public static string NormalizeMode(string? value) =>
        string.Equals(value?.Trim(), ModeUserOnly, StringComparison.OrdinalIgnoreCase)
            ? ModeUserOnly
            : ModeMerge;
}

public static class UserSettingsStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly object Gate = new();
    private static UserSettings? _current;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
    };

    public static UserSettings Current
    {
        get
        {
            lock (Gate)
            {
                _current ??= LoadUnlocked();
                return _current;
            }
        }
    }

    public static void Reload()
    {
        lock (Gate)
            _current = LoadUnlocked();
    }

    public static void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.ExecutorsMode = UserSettings.NormalizeMode(settings.ExecutorsMode);
        var path = AppPaths.UserSettingsFilePath;
        AppPaths.EnsureDirectory(path);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json + Environment.NewLine, Utf8NoBom);
        lock (Gate)
            _current = settings;
    }

    public static string ResolvePath(string? configured, string defaultRelative)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? defaultRelative : configured.Trim();
        if (Path.IsPathRooted(value))
            return Path.GetFullPath(value);
        return Path.GetFullPath(Path.Combine(AppPaths.BaseDirectory, value));
    }

    /// <summary>Если путь внутри папки программы — храним относительный (пережиезд вместе с exe).</summary>
    public static string ToStoredPath(string path)
    {
        var full = Path.GetFullPath((path ?? "").Trim());
        var root = Path.GetFullPath(AppPaths.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            return Path.GetRelativePath(root, full).Replace('\\', '/');
        return full;
    }

    private static UserSettings LoadUnlocked()
    {
        var path = AppPaths.UserSettingsFilePath;
        if (!File.Exists(path))
            return new UserSettings();

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var loaded = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
            if (string.IsNullOrWhiteSpace(loaded.RolesFile))
                loaded.RolesFile = AppPaths.DefaultNerUserRolesRelative;
            if (string.IsNullOrWhiteSpace(loaded.CustomerCardsDirectory))
                loaded.CustomerCardsDirectory = AppPaths.DefaultCustomerCardsRelative;
            loaded.ExecutorsMode = UserSettings.NormalizeMode(loaded.ExecutorsMode);
            return loaded;
        }
        catch
        {
            return new UserSettings();
        }
    }
}
