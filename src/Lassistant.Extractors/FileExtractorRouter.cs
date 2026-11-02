using Lassistant.Core.Config;
using Lassistant.Core.Models;

namespace Lassistant.Extractors;

public sealed class FileExtractorRouter
{
    private readonly IReadOnlyList<IFileExtractor> _extractors;

    public FileExtractorRouter(FieldMappingConfig config)
    {
        var ocrExtractor = new PassportOcrExtractor();
        _extractors =
        [
            new ExcelExtractor(config),
            new WordCardExtractor(config),
            new PdfExtractor(config, ocrExtractor),
            ocrExtractor
        ];
    }

    public ClientData ExtractFromFile(string filePath)
    {
        var extractor = _extractors.FirstOrDefault(e => e.CanHandle(filePath))
            ?? throw new NotSupportedException($"Формат файла не поддерживается: {Path.GetExtension(filePath)}");

        return extractor.Extract(filePath);
    }

    public ClientData ExtractFromFiles(IEnumerable<string> filePaths)
    {
        var merged = new ClientData();
        foreach (var path in filePaths)
        {
            var partial = ExtractFromFile(path);
            merged.MergeFrom(partial);
        }
        return merged;
    }
}
