using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Lassistant.Core.Services;

namespace Lassistant.Templates;

/// <summary>
/// Заменяет найденные NER-фрагменты в .docx на плейсхолдеры <c>{{role}}</c>.
/// Замена по тексту сущности (длинные первыми), чтобы корректно работать
/// при разбиении текста по нескольким <c>w:t</c>.
/// </summary>
public static class NerSpanToTemplateConverter
{
    /// <summary>
    /// Создаёт карту замен из исходного NER-текста. Start/End являются
    /// источником истины; отображаемое поле entity.Text не используется.
    /// </summary>
    public static void Convert(
        string sourcePath,
        string outputPath,
        ContractNerDocument document,
        IReadOnlyList<(int Start, int End, string Role)> spans)
    {
        var mappings = new List<(string Text, string Role)>(spans.Count);
        foreach (var span in spans)
        {
            if (span.Start < 0 || span.End <= span.Start || span.End > document.Text.Length)
                continue;

            mappings.Add((
                document.Text[span.Start..span.End],
                span.Role));
        }

        Convert(sourcePath, outputPath, mappings);
    }

    public static void Convert(
        string sourcePath,
        string outputPath,
        IReadOnlyList<(string Text, string Role)> mappings)
    {
        AppPaths.EnsureDirectory(outputPath);
        File.Copy(sourcePath, outputPath, overwrite: true);

        var active = mappings
            .Where(m => !string.IsNullOrWhiteSpace(m.Text) && !string.IsNullOrWhiteSpace(m.Role))
            .Select(m => (Text: m.Text, Role: m.Role.Trim()))
            .GroupBy(m => m.Text, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderByDescending(m => m.Text.Length)
            .ToList();

        if (active.Count == 0) return;

        using var doc = WordprocessingDocument.Open(outputPath, true);
        var main = doc.MainDocumentPart;
        if (main is null) return;

        ReplaceInPart(main, active);
        foreach (var header in main.HeaderParts)
            ReplaceInPart(header, active);
        foreach (var footer in main.FooterParts)
            ReplaceInPart(footer, active);

        doc.Save();
    }

    private static void ReplaceInPart(
        OpenXmlPart part,
        List<(string Text, string Role)> sorted)
    {
        if (part.RootElement is null) return;

        foreach (var (text, role) in sorted)
        {
            if (text.Contains('\n'))
                ReplaceAcrossParagraphs(part, text, role);
        }

        var singleLine = sorted.Where(item => !item.Text.Contains('\n')).ToList();
        foreach (var para in part.RootElement.Descendants<Paragraph>())
            ReplaceInParagraph(para, singleLine);
    }

    /// <summary>
    /// Фрагмент на несколько абзацев NER: первая строка становится плейсхолдером,
    /// продолжение в следующих абзацах удаляется.
    /// </summary>
    private static void ReplaceAcrossParagraphs(OpenXmlPart part, string text, string role)
    {
        if (part.RootElement is null) return;

        var lines = text.Split('\n');
        var paragraphs = part.RootElement.Descendants<Paragraph>().ToList();
        var placeholder = "{{" + role + "}}";
        for (var i = 0; i <= paragraphs.Count - lines.Length; i++)
        {
            if (!MatchesLineSequence(paragraphs, i, lines)) continue;

            if (lines[0].Length > 0)
                ReplaceSubstring(paragraphs[i], lines[0], placeholder);
            for (var line = 1; line < lines.Length; line++)
            {
                if (lines[line].Length == 0) continue;
                ReplaceSubstring(paragraphs[i + line], lines[line], "");
            }

            i += lines.Length - 1;
        }
    }

    private static bool MatchesLineSequence(List<Paragraph> paragraphs, int start, string[] lines)
    {
        for (var line = 0; line < lines.Length; line++)
        {
            if (lines[line].Length == 0) continue;
            var joined = string.Concat(paragraphs[start + line].Descendants<Text>().Select(t => t.Text ?? ""));
            if (!joined.Contains(lines[line], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static void ReplaceInParagraph(
        Paragraph para,
        List<(string Text, string Role)> sorted)
    {
        foreach (var (text, role) in sorted)
            ReplaceSubstring(para, text, "{{" + role + "}}");
    }

    private static void ReplaceSubstring(Paragraph para, string text, string replacement)
    {
        foreach (var node in para.Descendants<Text>().ToList())
        {
            if (string.IsNullOrEmpty(node.Text)) continue;
            if (node.Text.Contains(text, StringComparison.Ordinal))
                node.Text = node.Text.Replace(text, replacement, StringComparison.Ordinal);
        }

        var texts = para.Descendants<Text>().ToList();
        if (texts.Count == 0) return;

        var joined = string.Concat(texts.Select(node => node.Text ?? ""));
        if (!joined.Contains(text, StringComparison.Ordinal)) return;

        var first = texts[0];
        first.Text = joined.Replace(text, replacement, StringComparison.Ordinal);
        first.Space = SpaceProcessingModeValues.Preserve;
        for (var i = 1; i < texts.Count; i++)
            texts[i].Text = string.Empty;
    }
}
