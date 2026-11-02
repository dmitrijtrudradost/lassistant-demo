using DocumentFormat.OpenXml.Packaging;
using Lassistant.Core.Config;
using Lassistant.Core.Models;

namespace Lassistant.Extractors;

public sealed class WordCardExtractor : IFileExtractor
{
    private readonly LabelValueParser _parser;

    public WordCardExtractor(FieldMappingConfig config)
    {
        _parser = new LabelValueParser(config);
    }

    public bool CanHandle(string filePath) =>
        Path.GetExtension(filePath).Equals(".docx", StringComparison.OrdinalIgnoreCase);

    public ClientData Extract(string filePath)
    {
        var text = ExtractText(filePath);
        var data = _parser.ParseFromText(text);
        if (!string.IsNullOrWhiteSpace(data.Inn) || !string.IsNullOrWhiteSpace(data.Ogrnip))
            data.ClientType = ClientType.IndividualEntrepreneur;
        return data;
    }

    internal static string ExtractText(string filePath)
    {
        using var document = WordprocessingDocument.Open(filePath, false);
        var body = document.MainDocumentPart?.Document.Body;
        if (body is null)
            return string.Empty;

        return body.InnerText;
    }
}
