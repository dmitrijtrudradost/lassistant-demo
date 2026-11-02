using Lassistant.Extractors;

namespace Lassistant.SmokeTest;

internal static class OcrDump
{
    public static void Run(string imagePath)
    {
        var ocr = new PassportOcrExtractor();
        var text = ocr.RecognizeImage(imagePath);
        Console.WriteLine("=== RAW OCR TEXT ===");
        Console.WriteLine(text);
        Console.WriteLine("=== PARSED ===");
        var data = PassportOcrExtractor.ParsePassportText(text);
        Console.WriteLine($"FullName: [{data.FullName}] len={data.FullName.Length}");
        Console.WriteLine($"Series: {data.PassportSeries}");
        Console.WriteLine($"Number: {data.PassportNumber}");
        Console.WriteLine($"IssuedBy: {data.PassportIssuedBy}");
        Console.WriteLine($"IssueDate: {data.PassportIssueDate}");
        Console.WriteLine($"DeptCode: {data.PassportDeptCode}");
        Console.WriteLine($"Address: {data.RegistrationAddress}");
    }
}
