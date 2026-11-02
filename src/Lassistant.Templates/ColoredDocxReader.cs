using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Lassistant.Templates;

/// <summary>
/// Цвет выделения, нанесённого юристом в Word.
/// </summary>
public enum HighlightColor
{
    Cyan,   // бирюзовый — контрагент/клиент
    Yellow, // жёлтый    — реквизиты договора
    Green   // зелёный   — объект/услуга
}

/// <summary>
/// Одно выделение: группа подряд идущих runs одного цвета в одном абзаце.
/// </summary>
/// <param name="Text">Текст самого выделения.</param>
/// <param name="Color">Цвет выделения.</param>
/// <param name="ParagraphContext">Полный текст абзаца.</param>
/// <param name="ParagraphIndex">Порядковый номер абзаца в документе.</param>
/// <param name="LabelPrefix">
/// Текст перед выделением внутри абзаца (до 80 символов, правый край).
/// Используется как «ярлык поля» для Label-First маппинга.
/// Пример: "паспорт серия " перед "11 44 444254".
/// </param>
public sealed record ColoredEntity(
    string Text,
    HighlightColor Color,
    string ParagraphContext,
    int ParagraphIndex,
    string LabelPrefix = "");

/// <summary>
/// Читает цветные выделения (cyan / yellow / green) из .docx-файла.
/// Обрабатывает основной документ, колонтитулы.
/// </summary>
public static class ColoredDocxReader
{
    public static IReadOnlyList<ColoredEntity> Read(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Файл не найден.", path);

        var result = new List<ColoredEntity>();
        using var doc = WordprocessingDocument.Open(path, false);

        var main = doc.MainDocumentPart;
        if (main is null) return result;

        var paraIndex = 0;
        ExtractFromPart(main, ref paraIndex, result);

        foreach (var header in main.HeaderParts)
            ExtractFromPart(header, ref paraIndex, result);

        foreach (var footer in main.FooterParts)
            ExtractFromPart(footer, ref paraIndex, result);

        return result;
    }

    private static void ExtractFromPart(OpenXmlPart part, ref int paraIndex, List<ColoredEntity> result)
    {
        if (part.RootElement is null) return;

        foreach (var para in part.RootElement.Descendants<Paragraph>())
        {
            ExtractFromParagraph(para, paraIndex++, result);
        }
    }

    private static void ExtractFromParagraph(Paragraph para, int paraIndex, List<ColoredEntity> result)
    {
        var allRuns = para.Elements<Run>().ToList();

        // Строим полный текст абзаца для контекста
        var paraText = string.Concat(
            allRuns.SelectMany(r => r.Elements<Text>()).Select(t => t.Text ?? ""));

        int i = 0;
        while (i < allRuns.Count)
        {
            var color = GetHighlightColor(allRuns[i]);
            if (color is null)
            {
                i++;
                continue;
            }

            // LabelPrefix: текст всех runs ДО текущего выделения (правые 80 символов)
            var prefixRaw = string.Concat(
                allRuns[..i].SelectMany(r => r.Elements<Text>()).Select(t => t.Text ?? ""));
            var labelPrefix = prefixRaw.Length > 80 ? prefixRaw[^80..] : prefixRaw;

            // Сливаем подряд идущие runs одного цвета
            var j = i;
            var parts = new List<string>();
            while (j < allRuns.Count && GetHighlightColor(allRuns[j]) == color)
            {
                var text = string.Concat(allRuns[j].Elements<Text>().Select(t => t.Text ?? ""));
                parts.Add(text);
                j++;
            }

            var combinedText = string.Concat(parts).Trim();
            if (!string.IsNullOrWhiteSpace(combinedText))
            {
                result.Add(new ColoredEntity(
                    combinedText,
                    color.Value,
                    paraText.Trim(),
                    paraIndex,
                    labelPrefix));
            }

            i = j;
        }
    }

    private static HighlightColor? GetHighlightColor(Run run)
    {
        var hl = run.RunProperties?.Highlight;
        if (hl?.Val is null) return null;

        var val = hl.Val.Value;
        if (val == HighlightColorValues.Cyan     || val == HighlightColorValues.DarkCyan)  return HighlightColor.Cyan;
        if (val == HighlightColorValues.Yellow)                                             return HighlightColor.Yellow;
        if (val == HighlightColorValues.Green    || val == HighlightColorValues.DarkGreen) return HighlightColor.Green;
        return null;
    }
}
