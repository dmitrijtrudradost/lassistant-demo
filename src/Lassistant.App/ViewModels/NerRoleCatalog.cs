using System.Windows;
using Lassistant.Core.Services;

namespace Lassistant.App.ViewModels;

/// <summary>
/// Каталог известных NER-ролей + подтверждение записи новой в user/settings/ner-user-roles.json.
/// </summary>
public static class NerRoleCatalog
{
    private static readonly HashSet<string> KnownRoles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    public static void SetKnownRoles(IEnumerable<string> roles)
    {
        lock (Gate)
        {
            KnownRoles.Clear();
            foreach (var role in roles)
            {
                if (string.IsNullOrWhiteSpace(role)) continue;
                var trimmed = role.Trim();
                if (string.Equals(trimmed, TemplateEditorViewModel.AddNewNerRoleOption, StringComparison.Ordinal))
                    continue;
                KnownRoles.Add(trimmed);
            }
        }
    }

    public static bool IsKnown(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return true;
        var trimmed = role.Trim();
        if (string.Equals(trimmed, TemplateEditorViewModel.AddNewNerRoleOption, StringComparison.Ordinal))
            return true;
        lock (Gate)
            return KnownRoles.Contains(trimmed);
    }

    /// <summary>
    /// Роль уже в строке. Спрашивает про запись в JSON.
    /// true — оставить роль (Да / уже известна); false — очистить поле (Нет).
    /// </summary>
    public static bool ConfirmPersistNewRole(string role)
    {
        var trimmed = (role ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return false;
        if (IsKnown(trimmed))
            return true;

        var result = MessageBox.Show(
            $"Добавить роль «{trimmed}» в личный список?\n\n" +
            $"Файл не затирается при обновлении:\n{AppPaths.NerUserRolesPath}\n\n" +
            "Его можно передать как обратную связь для обучения модели.",
            "Новая роль",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);

        if (result != MessageBoxResult.Yes)
            return false;

        if (PlaceholderRolesStore.TryAdd(trimmed, out var error))
        {
            lock (Gate)
                KnownRoles.Add(trimmed);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            MessageBox.Show(
                $"Не удалось сохранить роль: {error}",
                "Новая роль",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        // Уже была в файле (дедуп) — роль остаётся в строке.
        lock (Gate)
            KnownRoles.Add(trimmed);
        return true;
    }
}
