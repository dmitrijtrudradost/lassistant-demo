using Lassistant.Core.Config;
using Lassistant.Core.Models;
using UglyToad.PdfPig;

namespace Lassistant.Extractors;

public sealed class PdfExtractor : IFileExtractor
{
    private readonly LabelValueParser _parser;
    private readonly PassportOcrExtractor? _ocrExtractor;

    public PdfExtractor(FieldMappingConfig config, PassportOcrExtractor? ocrExtractor = null)
    {
        _parser = new LabelValueParser(config);
        _ocrExtractor = ocrExtractor;
    }

    public bool CanHandle(string filePath) =>
        Path.GetExtension(filePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public ClientData Extract(string filePath)
    {
        var text = ExtractTextLayer(filePath);

        if (string.IsNullOrWhiteSpace(text) && _ocrExtractor is not null)
        {
            var ocrText = _ocrExtractor.RecognizePdfPages(filePath);
            text = ocrText;
        }

        var data = _parser.ParseFromText(text);
        if (!string.IsNullOrWhiteSpace(data.Inn) || !string.IsNullOrWhiteSpace(data.Ogrnip))
            data.ClientType = ClientType.IndividualEntrepreneur;
        return data;
    }

    private static string ExtractTextLayer(string filePath)
    {
        using var document = PdfDocument.Open(filePath);
        return string.Join(Environment.NewLine,
            document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
    }
}
