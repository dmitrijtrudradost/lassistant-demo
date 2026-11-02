using System.Text.Json;
using System.Text.RegularExpressions;
using Lassistant.Core.Services;

namespace Lassistant.Templates;

/// <summary>
/// Предлагает ключ плейсхолдера для цветного выделения.
///
/// Алгоритм (четыре уровня):
///   1. Label-First (EndsWith) — текст ПЕРЕД выделением (LabelPrefix) в словаре.
///   2. Label-First (StartsWith) — когда выделен весь абзац целиком и ярлык
///      включён в начало текста: «паспорт серия 11 44», «выдан ГУ МВД...».
///   3. Regex — анализирует сам текст выделения (ИНН по длине, email, дата,
///      сумма, ФИО по шаблону трёх слов с заглавными).
///   4. Fallback — наименование_орг / адрес_объекта.
///
/// Словарь label-prefix-map.json редактируется без перекомпиляции.
/// </summary>
public static class HighlightPlaceholderMapper
{
    // ─── Загрузка словаря ─────────────────────────────────────────────────────

    private static readonly IReadOnlyList<(string Pattern, string Key)> LabelMap = LoadLabelMap();

    private static IReadOnlyList<(string, string)> LoadLabelMap()
    {
        try
        {
            var path = AppPaths.LabelPrefixMapPath;
            if (!File.Exists(path)) return [];

            using var stream = File.OpenRead(path);
            var doc = JsonDocument.Parse(stream);

            return doc.RootElement.EnumerateObject()
                .Where(p => !p.Name.StartsWith('_'))
                .Select(p => (p.Name.Trim().ToLowerInvariant(), p.Value.GetString() ?? ""))
                .Where(t => !string.IsNullOrEmpty(t.Item2))
                .OrderByDescending(t => t.Item1.Length)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    // ─── Regex-паттерны ───────────────────────────────────────────────────────

    private static readonly Regex MoneyAmount =
        new(@"^\d[\d\s]*[,\.]\d{2}$", RegexOptions.Compiled);

    private static readonly Regex OnlyDigits =
        new(@"^\d+$", RegexOptions.Compiled);

    // ФИО: три слова с заглавных букв (Фамилия Имя Отчество[,])
    private static readonly Regex FullName =
        new(@"^[А-ЯЁ][а-яё]+(?:-[А-ЯЁ][а-яё]+)?\s+[А-ЯЁ][а-яё]+\s+[А-ЯЁ][а-яё]+,?$",
            RegexOptions.Compiled);

    // Краткое ФИО: Фамилия И.О. или Фамилия И.О./
    private static readonly Regex ShortName =
        new(@"^[А-ЯЁ][а-яё]+-?[А-ЯЁ]?[а-яё]*\s+[А-ЯЁ]\.[А-ЯЁ]\.?/?$",
            RegexOptions.Compiled);

    // Дата русскими словами: «21» сентября 2022 г. / 18 января 2024 года
    private static readonly Regex RussianDate =
        new(@"«?\d{1,2}»?\s*(января|февраля|марта|апреля|мая|июня|июля|августа|сентября|октября|ноября|декабря)\s+\d{4}",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Дата «от» или числовая: от «21» сентября / 01.01.2000
    private static readonly Regex NumericDate =
        new(@"^\d{2}\.\d{2}\.\d{4}$", RegexOptions.Compiled);

    // ─── Публичный API ────────────────────────────────────────────────────────

    public static string Suggest(ColoredEntity entity)
    {
        // ── Шаг 1: Label-First (EndsWith) — текст ДО выделения ───────────────
        var byPrefix = SuggestByLabel(entity.LabelPrefix, endsWith: true);
        if (!string.IsNullOrEmpty(byPrefix))
            return byPrefix;

        // ── Шаг 2: Label-First (StartsWith) — выделен весь абзац с ярлыком ──
        var byTextStart = SuggestByLabel(entity.Text, endsWith: false);
        if (!string.IsNullOrEmpty(byTextStart))
            return byTextStart;

        // ── Шаги 3-4: Regex + Fallback ────────────────────────────────────────
        return entity.Color switch
        {
            HighlightColor.Cyan   => SuggestCyan(entity),
            HighlightColor.Yellow => SuggestYellow(entity),
            HighlightColor.Green  => SuggestGreen(entity),
            _                     => string.Empty
        };
    }

    // ─── Шаги 1-2: Label-First ────────────────────────────────────────────────

    /// <summary>
    /// Проверяет строку по словарю.
    /// <paramref name="endsWith"/>=true: norm.EndsWith(pattern) — для LabelPrefix.
    /// <paramref name="endsWith"/>=false: norm.StartsWith(pattern) — для текста выделения.
    /// </summary>
    private static string? SuggestByLabel(string source, bool endsWith)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;

        var norm = source.Trim().ToLowerInvariant();
        foreach (var (pattern, key) in LabelMap)
        {
            var match = endsWith
                ? norm.EndsWith(pattern, StringComparison.Ordinal)
                : norm.StartsWith(pattern, StringComparison.Ordinal);

            if (match) return key;
        }
        return null;
    }

    // ─── Шаг 3: Regex по тексту выделения ────────────────────────────────────

    private static string SuggestCyan(ColoredEntity e)
    {
        var text = e.Text.Trim();
        var ctx  = e.ParagraphContext;

        // Встроенные ярлыки в тексте (юрист включил метку в само выделение)
        if (text.StartsWith("ИНН:",               StringComparison.Ordinal))          return "инн";
        if (text.StartsWith("КПП:",               StringComparison.Ordinal))          return "кпп";
        if (text.StartsWith("ОГРН:",              StringComparison.Ordinal))          return "огрн";
        if (text.StartsWith("ОГРНИП:",            StringComparison.Ordinal))          return "огрнип";
        if (text.StartsWith("Р/с:",               StringComparison.Ordinal))          return "расчетный_счет";
        if (text.StartsWith("БИК:",               StringComparison.Ordinal))          return "бик";
        if (text.StartsWith("К/с:",               StringComparison.Ordinal))          return "корр_счет";
        if (text.StartsWith("Телефон:",           StringComparison.OrdinalIgnoreCase)) return "телефон";
        if (text.StartsWith("Юридический адрес:", StringComparison.OrdinalIgnoreCase)) return "юр_адрес";
        if (text.StartsWith("Реквизиты банка:",   StringComparison.OrdinalIgnoreCase)) return "банк";
        if (text.StartsWith("E-mail:",            StringComparison.OrdinalIgnoreCase)) return "эл_почта";
        if (text.StartsWith("Email:",             StringComparison.OrdinalIgnoreCase)) return "эл_почта";

        // E-mail по символу @
        if (text.Contains('@')) return "эл_почта";

        // Дата: «21» сентября 2022 г. / от «21»... / 01.01.2000
        if (RussianDate.IsMatch(text) || NumericDate.IsMatch(text))
            return "дата_договора";

        // ФИО: три слова с заглавных (Иванов Иван Иванович)
        if (FullName.IsMatch(text))
            return "фио_подписанта";

        // Краткое ФИО: Иванов И.И./
        if (ShortName.IsMatch(text))
            return "фио_подписанта";

        // Суммы в рублях — ПЕРЕД цифровыми длинами (иначе 9 цифр → КПП)
        if (MoneyAmount.IsMatch(text)) return "сумма_договора";
        if (ContainsAny(text, "руб.", "рублей", "рубля", " руб ", "коп.", "копей"))
            return "сумма_договора";

        // Адрес по структурным признакам
        if (IsAddress(text, ctx)) return "адрес_объекта";

        // Телефон
        if (Regex.IsMatch(text, @"^[\d\s().+\-]+$") &&
            Regex.Replace(text, @"\D", "").Length >= 7)
            return "телефон";

        // ИНН / ОГРН по длине строго цифровых последовательностей
        var digitsOnly = text.Replace(" ", "").Replace("-", "");
        if (OnlyDigits.IsMatch(digitsOnly))
        {
            return digitsOnly.Length switch
            {
                10 or 12 => "инн",
                13 or 15 => "огрн",
                _        => string.Empty
            };
        }

        // Организационно-правовая форма
        if (ContainsAny(text, "ООО", "ОАО", "ЗАО", " АО ", "Общество", "ИП "))
            return "наименование_орг";

        // Банк
        if (ContainsAny(ctx, "банк") || ContainsAny(text, "банк", "Банк"))
            return "банк";

        return "наименование_орг";
    }

    private static string SuggestYellow(ColoredEntity e)
    {
        var text = e.Text.Trim();
        var ctx  = e.ParagraphContext;

        if (text.StartsWith("№") || Regex.IsMatch(text, @"\d+-[Пп]\s+от"))
            return "номер_и_дата_договора";

        if (text.StartsWith("п.", StringComparison.Ordinal) ||
            text.StartsWith("п ", StringComparison.Ordinal))
            return "пункт_договора";

        // Дата в «кавычках» + год
        if (Regex.IsMatch(text, @"«\d+»") && Regex.IsMatch(text, @"20\d{2}"))
            return "дата_соглашения";

        // Русская дата без кавычек
        if (RussianDate.IsMatch(text)) return "дата_договора";

        if (MoneyAmount.IsMatch(text) || ContainsAny(text, "руб.", "рублей", "рубля", " руб ", "коп.", "копей"))
            return ContainsAny(ctx, "задолж") ? "сумма_задолженности" : "сумма_договора";

        if (ContainsAny(ctx, "расторж", "прекращ"))    return "дата_расторжения";
        if (ContainsAny(ctx, "последн", "действи"))     return "последний_день";
        if (ContainsAny(ctx, "дата") && Regex.IsMatch(text, @"\d")) return "дата_договора";
        if (ContainsAny(ctx, "договор", "соглашен") && Regex.IsMatch(text, @"\d"))
            return "номер_и_дата_договора";

        return "номер_и_дата_договора";
    }

    private static string SuggestGreen(ColoredEntity e)
    {
        var text = e.Text.Trim();
        var ctx  = e.ParagraphContext;

        if (IsAddress(text, ctx)) return "адрес_объекта";
        return "адрес_объекта";
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsAddress(string text, string ctx)
    {
        if (Regex.IsMatch(text, @"\bул\b\.|\bпр\b\.|\bпер\b\.|\bд\b\.|\bкорп\b\.|\bстр\b\.|\bкв\b\.", RegexOptions.IgnoreCase))
            return true;
        if (Regex.IsMatch(text, @"\d+[\-–—]\s*й\s+(километр|км)", RegexOptions.IgnoreCase))
            return true;
        if (ContainsAny(text, "шоссе", "переулок", "участок", "квартал", "микрорайон"))
            return true;
        if (ContainsAny(ctx, "адрес", "объект", "монтаж", "установк", "помещ", "здани", "расположен"))
            return true;
        return false;
    }

    private static bool ContainsAny(string source, params string[] terms) =>
        terms.Any(t => source.Contains(t, StringComparison.OrdinalIgnoreCase));
}
