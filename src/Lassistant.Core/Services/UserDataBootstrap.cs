namespace Lassistant.Core.Services;

/// <summary>
/// Создаёт user/ при первом запуске и копирует старые карточки из data/заказчики.
/// </summary>
public static class UserDataBootstrap
{
    public static void Ensure()
    {
        Directory.CreateDirectory(AppPaths.UserSettingsDirectory);
        Directory.CreateDirectory(AppPaths.CustomerCardsDirectory);

        var rolesPath = AppPaths.NerUserRolesPath;
        AppPaths.EnsureDirectory(rolesPath);
        if (!File.Exists(rolesPath))
            PlaceholderRolesStore.WriteEmptyFile(rolesPath);

        MigrateLegacyCustomerCards();
    }

    private static void MigrateLegacyCustomerCards()
    {
        var source = Path.Combine(AppPaths.BaseDirectory, "data", "заказчики");
        var dest = AppPaths.CustomerCardsDirectory;
        if (!Directory.Exists(source))
            return;
        if (string.Equals(
                Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(dest).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            return;

        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source, "*.md"))
        {
            var target = Path.Combine(dest, Path.GetFileName(file));
            if (!File.Exists(target))
                File.Copy(file, target);
        }
    }
}
