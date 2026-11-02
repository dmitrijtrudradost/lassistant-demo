using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Lassistant.Core.Services;

namespace Lassistant.Templates;

/// <summary>
/// Преобразует анонимизированный .docx с цветными выделениями
/// в шаблон: заменяет цветной текст на <c>{{placeholder_key}}</c>.
/// </summary>
public static class ColoredDocxToTemplateConverter
{
    /// <param name="sourcePath">Исходный файл (обычно *_anon.docx с цветными выделениями).</param>
    /// <param name="outputPath">Куда сохранить готовый шаблон.</param>
    /// <param name="mappings">Карта: (текст выделения, цвет) → ключ плейсхолдера.</param>
    public static void Convert(
        string sourcePath,
        string outputPath,
        IReadOnlyList<(string Text, HighlightColor Color, string Placeholder)> mappings)
    {
        AppPaths.EnsureDirectory(outputPath);
        File.Copy(sourcePath, outputPath, overwrite: true);

        var activeMappings = mappings
            .Where(m => !string.IsNullOrWhiteSpace(m.Placeholder))
            .GroupBy(m => (m.Text, m.Color))
            .Select(g => g.First())
            .ToList();

        if (activeMappings.Count == 0) return;

        using var doc = WordprocessingDocument.Open(outputPath, true);
        var main = doc.MainDocumentPart;
        if (main is null) return;

        ProcessPart(main, activeMappings);

        foreach (var header in main.HeaderParts)
            ProcessPart(header, activeMappings);

        foreach (var footer in main.FooterParts)
            ProcessPart(footer, activeMappings);

        doc.Save();
    }

    private static void ProcessPart(
        OpenXmlPart part,
        List<(string Text, HighlightColor Color, string Placeholder)> mappings)
    {
        if (part.RootElement is null) return;

        foreach (var para in part.RootElement.Descendants<Paragraph>())
        {
            ProcessParagraph(para, mappings);
        }
    }

    private static void ProcessParagraph(
        Paragraph para,
        List<(string Text, HighlightColor Color, string Placeholder)> mappings)
    {
        var runs = para.Elements<Run>().ToList();
        int i = 0;
        while (i < runs.Count)
        {
            var color = GetHighlightColor(runs[i]);
            if (color is null)
            {
                i++;
                continue;
            }

            // Собираем группу одноцветных runs
            var j = i;
            var parts = new List<string>();
            while (j < runs.Count && GetHighlightColor(runs[j]) == color)
            {
                parts.Add(string.Concat(runs[j].Elements<Text>().Select(t => t.Text ?? "")));
                j++;
            }

            var groupText = string.Concat(parts).Trim();

            // Ищем подходящий маппинг: точное совпадение или исходный текст группы
            // (если юрист уточнил EditableText, всё равно матчим по полному цветному фрагменту через Contains).
            var mapping = mappings.FirstOrDefault(m =>
                m.Color == color.Value &&
                (string.Equals(m.Text, groupText, StringComparison.Ordinal) ||
                 (!string.IsNullOrEmpty(m.Text) &&
                  groupText.Contains(m.Text, StringComparison.Ordinal))));

            if (mapping != default)
            {
                // Первый run получает плейсхолдер, остальные — очищаются
                SetRunText(runs[i], $"{{{{{mapping.Placeholder}}}}}");
                RemoveHighlight(runs[i]);

                for (var k = i + 1; k < j; k++)
                {
                    SetRunText(runs[k], "");
                    RemoveHighlight(runs[k]);
                }
            }
            else
            {
                // Нет маппинга — просто убираем подсветку, оставляем текст
                for (var k = i; k < j; k++)
                    RemoveHighlight(runs[k]);
            }

            i = j;
        }
    }

    private static void SetRunText(Run run, string text)
    {
        var texts = run.Elements<Text>().ToList();
        if (texts.Count == 0)
        {
            run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
            return;
        }

        texts[0].Text = text;
        texts[0].Space = SpaceProcessingModeValues.Preserve;
        foreach (var extra in texts.Skip(1))
            extra.Text = string.Empty;
    }

    private static void RemoveHighlight(Run run)
    {
        var rpr = run.RunProperties;
        if (rpr is null) return;
        rpr.Highlight?.Remove();
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
