namespace Lassistant.Templates;

/// <summary>
/// Сводит сырые сущности NER к удобному списку для ревью:
/// отбрасывает мусор по score/длине, при дублях одной роли оставляет лучший score
/// (кроме ролей объекта/повторяющихся адресов — сохраняются уникальные тексты).
/// </summary>
public static class NerEntityAggregator
{
    private static readonly HashSet<string> MultiValueRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "объект_адрес",
        "объект_наименование",
        "объект_тип", // deprecated alias until v1.2
    };

    public static IReadOnlyList<ContractNerEntity> Aggregate(
        IEnumerable<ContractNerEntity> raw,
        double minScore)
    {
        var cleaned = raw
            .Where(e => !string.IsNullOrWhiteSpace(e.Role) && !string.IsNullOrWhiteSpace(e.Text))
            .Select(Normalize)
            .Where(e => e.Score >= minScore)
            .Where(e => !IsJunk(e))
            .GroupBy(e => (e.Start, e.End, e.Role))
            .Select(group => group.OrderByDescending(e => e.Score).First())
            .ToList();

        var result = new List<ContractNerEntity>();
        foreach (var group in cleaned.GroupBy(e => e.Role, StringComparer.OrdinalIgnoreCase))
        {
            if (MultiValueRoles.Contains(group.Key))
            {
                // Для повторяющихся ролей сохраняем разные mentions, даже если
                // текст совпадает или отличается только регистром. Точные
                // дубли одного span уже удалены выше.
                result.AddRange(group);
            }
            else
            {
                result.Add(group.OrderByDescending(e => e.Score).First());
            }
        }

        return result
            .OrderBy(e => e.ParagraphIndex)
            .ThenBy(e => e.Start)
            .ToList();
    }

    private static ContractNerEntity Normalize(ContractNerEntity e) => new()
    {
        Role = e.Role.Trim(),
        // Python returns the exact source slice by Start/End. Preserve it:
        // changing whitespace here would break template replacement.
        Text = e.Text,
        Score = e.Score,
        Start = e.Start,
        End = e.End,
        ParagraphIndex = e.ParagraphIndex,
        PartyHint = e.PartyHint,
        Table = e.Table,
        Row = e.Row,
        Cell = e.Cell,
        Column = e.Column
    };

    private static bool IsJunk(ContractNerEntity e)
    {
        var t = e.Text.Trim();
        if (t.Length <= 1) return true;
        if (t is "." or "," or "/" or "-" or "»" or "«" or "(" or ")") return true;
        // Обрезанный хвост вроде «11» при роли ОГРН/ИНН без достаточной длины
        if (e.Role.Contains("инн", StringComparison.OrdinalIgnoreCase) && DigitsOnly(t).Length is < 10 and > 0)
            return DigitsOnly(t).Length < 10;
        if (e.Role.Contains("огрн", StringComparison.OrdinalIgnoreCase) && DigitsOnly(t).Length is > 0 and < 13)
            return DigitsOnly(t).Length < 13;
        return false;
    }

    private static string DigitsOnly(string s) =>
        new(s.Where(char.IsDigit).ToArray());
}
