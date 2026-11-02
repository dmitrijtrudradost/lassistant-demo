using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Lassistant.Core.Services;

namespace Lassistant.Templates;

/// <summary>Обратная подстановка: плейсхолдеры {{роль}} → исходные значения из mapping.</summary>
public static class DocxDeAnonymizer
{
    public static void Restore(string anonymizedPath, string outputPath, AnonymizationMapping mapping)
    {
        AppPaths.EnsureDirectory(outputPath);
        File.Copy(anonymizedPath, outputPath, overwrite: true);

        var replacements = mapping.Replacements
            .GroupBy(r => r.Placeholder, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Original, StringComparer.Ordinal);

        using var document = WordprocessingDocument.Open(outputPath, true);
        ReplaceInElement(document.MainDocumentPart?.RootElement, replacements);

        foreach (var header in document.MainDocumentPart?.HeaderParts ?? [])
            ReplaceInElement(header.RootElement, replacements);

        foreach (var footer in document.MainDocumentPart?.FooterParts ?? [])
            ReplaceInElement(footer.RootElement, replacements);

        document.Save();
    }

    private static void ReplaceInElement(OpenXmlElement? root, IReadOnlyDictionary<string, string> replacements)
    {
        if (root is null)
            return;

        foreach (var paragraph in root.Descendants<Paragraph>())
        {
            foreach (var text in paragraph.Descendants<Text>())
            {
                if (string.IsNullOrEmpty(text.Text))
                    continue;

                var value = text.Text;
                foreach (var (placeholder, original) in replacements)
                    value = value.Replace(placeholder, original, StringComparison.Ordinal);

                if (!string.Equals(value, text.Text, StringComparison.Ordinal))
                    text.Text = value;
            }

            foreach (var (placeholder, original) in replacements)
            {
                while (true)
                {
                    var textNodes = paragraph.Descendants<Text>().ToList();
                    var combined = string.Concat(textNodes.Select(t => t.Text ?? string.Empty));
                    var index = combined.IndexOf(placeholder, StringComparison.Ordinal);
                    if (index < 0)
                        break;

                    DocxRunTextHelper.ReplaceSpan(textNodes, index, index + placeholder.Length, original);
                }
            }
        }
    }
}
