namespace Lassistant.Core.Services;

public static class AppPaths
{
    public const string DefaultNerUserRolesRelative = "user/settings/ner-user-roles.json";
    public const string DefaultCustomerCardsRelative = "user/data/заказчики";

    public static string BaseDirectory =>
        AppContext.BaseDirectory;

    public static string ConfigDirectory =>
        Path.Combine(BaseDirectory, "config");

    public static string TemplatesDirectory =>
        Path.Combine(BaseDirectory, "templates");

    /// <summary>Личные данные рядом с exe; publish.bat эту папку не копирует.</summary>
    public static string UserRootDirectory =>
        Path.Combine(BaseDirectory, "user");

    public static string UserSettingsDirectory =>
        Path.Combine(UserRootDirectory, "settings");

    public static string UserSettingsFilePath =>
        Path.Combine(UserSettingsDirectory, "user-settings.json");

    public static string UserExecutorsPath =>
        Path.Combine(UserSettingsDirectory, "executors.json");

    public static string DataDirectory =>
        Path.Combine(UserRootDirectory, "data");

    public static string CustomerCardsDirectory =>
        UserSettingsStore.ResolvePath(
            UserSettingsStore.Current.CustomerCardsDirectory,
            DefaultCustomerCardsRelative);

    public static string FieldMappingPath =>
        Path.Combine(ConfigDirectory, "field-mapping.json");

    public static string DocumentCatalogPath =>
        Path.Combine(ConfigDirectory, "document-catalog.json");

    public static string FieldDefinitionsPath =>
        Path.Combine(ConfigDirectory, "field-definitions.json");

    public static string ExecutorsPath =>
        Path.Combine(ConfigDirectory, "executors.json");

    public static string PlaceholderRolesPath =>
        Path.Combine(ConfigDirectory, "placeholder-roles.json");

    public static string NerUserRolesPath =>
        UserSettingsStore.ResolvePath(
            UserSettingsStore.Current.RolesFile,
            DefaultNerUserRolesRelative);

    public static string LabelPrefixMapPath =>
        Path.Combine(ConfigDirectory, "label-prefix-map.json");

    public static string ContractNerSettingsPath =>
        Path.Combine(ConfigDirectory, "contract-ner.json");

    public static string ContractNerDiagnosticsDirectory =>
        Path.Combine(BaseDirectory, "diagnostics", "contract-ner");

    public static string EnsureDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        return path;
    }
}
