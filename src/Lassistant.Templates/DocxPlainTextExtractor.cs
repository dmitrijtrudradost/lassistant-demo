using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Lassistant.Templates;

/// <summary>
/// Извлекает плоский текст из .docx по абзацам (перевод строки = граница абзаца),
/// в том же духе, что обучение Contract NER.
/// </summary>
public static class DocxPlainTextExtractor
{
    public static string Extract(string docxPath)
    {
        if (!File.Exists(docxPath))
            throw new FileNotFoundException("Файл не найден.", docxPath);

        using var doc = WordprocessingDocument.Open(docxPath, isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return string.Empty;

        var sb = new StringBuilder(4096);
        AppendContainer(body, sb);
        return SanitizeBmp(sb.ToString().TrimEnd());
    }

    /// <summary>
    /// Извлекает текст в точности с гранулярностью обучения Contract NER:
    /// сначала абзацы body, затем таблицы; каждый абзац ячейки — отдельный фрагмент.
    /// </summary>
    public static ContractNerDocument ExtractForNer(string docxPath)
    {
        if (!File.Exists(docxPath))
            throw new FileNotFoundException("Файл не найден.", docxPath);

        using var doc = WordprocessingDocument.Open(docxPath, isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return new ContractNerDocument();

        var pending = new List<PendingFragment>();
        foreach (var paragraph in body.Elements<Paragraph>())
            pending.Add(new PendingFragment(NerParagraphText(paragraph), "body", null, null, null, null));

        var seenCells = new HashSet<OpenXmlElement>();
        var tableIndex = 0;
        foreach (var table in body.Elements<Table>())
        {
            AppendNerTable(table, tableIndex.ToString(), pending, seenCells);
            tableIndex++;
        }

        var result = new ContractNerDocument();
        var text = new StringBuilder(Math.Max(4096, pending.Sum(p => p.Text.Length + 1)));
        for (var i = 0; i < pending.Count; i++)
        {
            if (i > 0) text.Append('\n');
            var item = pending[i];
            var safeText = NormalizeNerText(item.Text);
            var start = text.Length;
            text.Append(safeText);
            result.Fragments.Add(new ContractNerFragment
            {
                Paragraph = i,
                Start = start,
                End = text.Length,
                Text = safeText,
                Source = item.Source,
                Table = item.Table,
                Row = item.Row,
                Cell = item.Cell,
                Column = item.Column
            });
        }

        result.Text = text.ToString();
        return result;
    }

    /// <summary>Заменяет одиночные surrogates на U+FFFD (безопасный UTF-8).</summary>
    private static string SanitizeBmp(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    sb.Append(c);
                    sb.Append(text[++i]);
                }
                else
                    sb.Append('\uFFFD');
            }
            else if (char.IsLowSurrogate(c))
                sb.Append('\uFFFD');
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static string NormalizeNerText(string text) =>
        SanitizeBmp(text.Replace("\r\n", "\n").Replace('\r', '\n'));

    private static void AppendNerTable(
        Table table,
        string tablePath,
        List<PendingFragment> output,
        HashSet<OpenXmlElement> seenCells)
    {
        var rowIndex = 0;
        foreach (var row in table.Elements<TableRow>())
        {
            var columnIndex = 0;
            foreach (var cell in row.Elements<TableCell>())
            {
                if (!seenCells.Add(cell))
                {
                    columnIndex++;
                    continue;
                }

                var cellPath = $"{tablePath}:{rowIndex}:{columnIndex}";
                foreach (var paragraph in cell.Elements<Paragraph>())
                {
                    output.Add(new PendingFragment(
                        NerParagraphText(paragraph),
                        "table",
                        tablePath,
                        rowIndex,
                        cellPath,
                        columnIndex));
                }

                var nestedIndex = 0;
                foreach (var nestedTable in cell.Elements<Table>())
                {
                    AppendNerTable(
                        nestedTable,
                        $"{tablePath}/{rowIndex}:{columnIndex}:{nestedIndex}",
                        output,
                        seenCells);
                    nestedIndex++;
                }

                columnIndex++;
            }
            rowIndex++;
        }
    }

    private static void AppendContainer(OpenXmlElement container, StringBuilder sb)
    {
        foreach (var child in container.ChildElements)
        {
            switch (child)
            {
                case Paragraph para:
                    sb.AppendLine(ParagraphText(para));
                    break;
                case Table table:
                    foreach (var row in table.Elements<TableRow>())
                    {
                        var cells = new List<string>();
                        foreach (var cell in row.Elements<TableCell>())
                        {
                            var nested = new StringBuilder();
                            AppendContainer(cell, nested);
                            var cellText = nested.ToString().Replace("\r", "").Replace("\n", " ").Trim();
                            cells.Add(cellText);
                        }
                        if (cells.Any(c => !string.IsNullOrWhiteSpace(c)))
                            sb.AppendLine(string.Join(" | ", cells));
                    }
                    break;
            }
        }
    }

    private static string ParagraphText(Paragraph para)
    {
        var sb = new StringBuilder();
        foreach (var child in para.ChildElements)
        {
            if (child is Run run)
                sb.Append(string.Concat(run.Elements<Text>().Select(t => t.Text ?? "")));
            else
            {
                foreach (var nested in child.Descendants<Run>())
                    sb.Append(string.Concat(nested.Elements<Text>().Select(t => t.Text ?? "")));
            }
        }
        return sb.ToString();
    }

    private static string NerParagraphText(Paragraph para)
    {
        var sb = new StringBuilder();
        foreach (var node in para.Descendants())
        {
            switch (node)
            {
                case Text text:
                    sb.Append(text.Text ?? "");
                    break;
                case TabChar:
                    sb.Append('\t');
                    break;
                case Break lineBreak
                    when lineBreak.Type is null
                         || lineBreak.Type.Value == BreakValues.TextWrapping:
                case CarriageReturn:
                    sb.Append('\n');
                    break;
            }
        }
        return sb.ToString();
    }

    private sealed record PendingFragment(
        string Text,
        string Source,
        string? Table,
        int? Row,
        string? Cell,
        int? Column);
}
