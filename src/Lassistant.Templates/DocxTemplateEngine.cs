using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Lassistant.Core.Models;
using Lassistant.Core.Services;

namespace Lassistant.Templates;

public sealed class DocxTemplateEngine
{
    private static readonly Regex PlaceholderRegex = new(@"\{\{([^}]+)\}\}", RegexOptions.Compiled);
    private const string EmptyPlaceholderFill = "FF0000";

    public void Generate(string templatePath, string outputPath, ClientData data)
    {
        if (!File.Exists(templatePath))
            throw new FileNotFoundException("Шаблон не найден.", templatePath);

        AppPaths.EnsureDirectory(outputPath);
        File.Copy(templatePath, outputPath, overwrite: true);

        var placeholders = data.ToPlaceholderMap();
        using var document = WordprocessingDocument.Open(outputPath, true);
        ReplaceInPart(document.MainDocumentPart!, placeholders);

        foreach (var headerPart in document.MainDocumentPart!.HeaderParts)
            ReplaceInPart(headerPart, placeholders);

        foreach (var footerPart in document.MainDocumentPart.FooterParts)
            ReplaceInPart(footerPart, placeholders);

        document.Save();
    }

    private static void ReplaceInPart(OpenXmlPart part, IReadOnlyDictionary<string, string> placeholders)
    {
        if (part.RootElement is null)
            return;

        foreach (var paragraph in part.RootElement.Descendants<Paragraph>().ToList())
            ReplaceInParagraph(paragraph, placeholders);

        foreach (var text in part.RootElement.Descendants<Text>())
        {
            if (string.IsNullOrEmpty(text.Text) || !text.Text.Contains('{'))
                continue;

            if (text.Ancestors<Paragraph>().Any())
                continue;

            // Вне абзацев (редко): только подстановка заполненных; пустые оставляем как {{key}}.
            text.Text = ReplaceFilledOnly(text.Text, placeholders);
        }
    }

    private static void ReplaceInParagraph(Paragraph paragraph, IReadOnlyDictionary<string, string> placeholders)
    {
        while (true)
        {
            var textNodes = paragraph.Descendants<Text>().ToList();
            if (textNodes.Count == 0)
                return;

            var combined = string.Concat(textNodes.Select(t => t.Text ?? string.Empty));
            var nodeStarts = BuildNodeStarts(textNodes);

            Match? target = null;
            string replacement = string.Empty;
            var markEmpty = false;

            for (var match = PlaceholderRegex.Match(combined); match.Success; match = match.NextMatch())
            {
                var key = match.Groups[1].Value.Trim();
                var filled = placeholders.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);
                // Уже размеченные пустые {{key}} оставляем — иначе бесконечный цикл.
                if (!filled && IsAlreadyMarkedEmpty(textNodes, nodeStarts, match.Index))
                    continue;

                target = match;
                markEmpty = !filled;
                replacement = filled ? value! : match.Value;
                break;
            }

            if (target is null)
                return;

            ReplaceMatch(
                textNodes,
                target.Index,
                target.Index + target.Length,
                replacement,
                markEmpty);
        }
    }

    private static bool IsAlreadyMarkedEmpty(List<Text> textNodes, List<int> nodeStarts, int charIndex)
    {
        for (var i = 0; i < textNodes.Count; i++)
        {
            var nodeStart = nodeStarts[i];
            var nodeEnd = nodeStart + (textNodes[i].Text?.Length ?? 0);
            if (charIndex < nodeStart || charIndex >= nodeEnd)
                continue;

            if (textNodes[i].Parent is not Run run)
                return false;

            var fill = run.RunProperties?.GetFirstChild<Shading>()?.Fill?.Value;
            return string.Equals(fill, EmptyPlaceholderFill, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static void ReplaceMatch(
        List<Text> textNodes,
        int start,
        int end,
        string replacement,
        bool markEmpty)
    {
        var nodeStarts = BuildNodeStarts(textNodes);
        var affected = GetAffectedNodes(textNodes, nodeStarts, start, end);
        if (affected.Count == 0)
            return;

        var first = affected[0];
        var last = affected[^1];
        var firstStart = nodeStarts[textNodes.IndexOf(first)];
        var lastStart = nodeStarts[textNodes.IndexOf(last)];
        var lastLen = last.Text?.Length ?? 0;

        var combined = string.Concat(textNodes.Select(t => t.Text ?? string.Empty));
        var prefix = combined.Substring(firstStart, start - firstStart);
        var suffix = combined.Substring(end, lastStart + lastLen - end);

        if (!markEmpty)
        {
            if (affected.Count == 1)
            {
                first.Text = prefix + replacement + suffix;
            }
            else
            {
                first.Text = prefix + replacement;
                for (var i = 1; i < affected.Count - 1; i++)
                    affected[i].Text = string.Empty;
                last.Text = suffix;
            }

            first.Space = SpaceProcessingModeValues.Preserve;
            if (affected.Count > 1)
                last.Space = SpaceProcessingModeValues.Preserve;
            return;
        }

        // Пустой плейсхолдер: prefix | {{key}} с красной заливкой | suffix — отдельные Run.
        if (first.Parent is not Run firstRun)
        {
            first.Text = prefix + replacement + suffix;
            first.Space = SpaceProcessingModeValues.Preserve;
            return;
        }

        foreach (var node in affected)
            node.Text = string.Empty;

        first.Text = prefix;
        first.Space = SpaceProcessingModeValues.Preserve;

        var shadedRun = CloneRunFormatting(firstRun);
        EnsureRedFill(shadedRun);
        shadedRun.AppendChild(new Text(replacement) { Space = SpaceProcessingModeValues.Preserve });
        firstRun.InsertAfterSelf(shadedRun);

        OpenXmlElement insertAfter = shadedRun;
        if (!string.IsNullOrEmpty(suffix))
        {
            if (!ReferenceEquals(first, last) && last.Parent is Run lastRun && !ReferenceEquals(lastRun, firstRun))
            {
                last.Text = suffix;
                last.Space = SpaceProcessingModeValues.Preserve;
            }
            else
            {
                var suffixRun = CloneRunFormatting(firstRun);
                RemoveShading(suffixRun);
                suffixRun.AppendChild(new Text(suffix) { Space = SpaceProcessingModeValues.Preserve });
                insertAfter.InsertAfterSelf(suffixRun);
            }
        }
    }

    private static Run CloneRunFormatting(Run source)
    {
        var run = new Run();
        if (source.RunProperties is not null)
            run.RunProperties = (RunProperties)source.RunProperties.CloneNode(true);
        else
            run.RunProperties = new RunProperties();
        return run;
    }

    private static void EnsureRedFill(Run run)
    {
        run.RunProperties ??= new RunProperties();
        var existing = run.RunProperties.GetFirstChild<Shading>();
        existing?.Remove();
        run.RunProperties.AppendChild(new Shading
        {
            Val = ShadingPatternValues.Clear,
            Fill = EmptyPlaceholderFill,
            Color = "auto"
        });
    }

    private static void RemoveShading(Run run)
    {
        run.RunProperties?.GetFirstChild<Shading>()?.Remove();
    }

    private static List<int> BuildNodeStarts(List<Text> textNodes)
    {
        var starts = new List<int>(textNodes.Count);
        var pos = 0;
        foreach (var node in textNodes)
        {
            starts.Add(pos);
            pos += node.Text?.Length ?? 0;
        }

        return starts;
    }

    private static List<Text> GetAffectedNodes(List<Text> textNodes, List<int> nodeStarts, int start, int end)
    {
        var affected = new List<Text>();
        for (var i = 0; i < textNodes.Count; i++)
        {
            var nodeStart = nodeStarts[i];
            var nodeEnd = nodeStart + (textNodes[i].Text?.Length ?? 0);
            if (nodeEnd <= start || nodeStart >= end)
                continue;

            affected.Add(textNodes[i]);
        }

        return affected;
    }

    private static string ReplaceFilledOnly(string input, IReadOnlyDictionary<string, string> placeholders) =>
        PlaceholderRegex.Replace(input, match =>
        {
            var key = match.Groups[1].Value.Trim();
            if (placeholders.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
            return match.Value;
        });
}
