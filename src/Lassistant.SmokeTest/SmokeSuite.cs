using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.Security.Cryptography;

namespace Lassistant.SmokeTest;

internal static class SmokeSuite
{
    public static async Task<int> RunAsync()
    {
        var work = Directory.CreateTempSubdirectory("lassistant-smoke-").FullName;
        try
        {
            var source = Path.Combine(work, "contract.docx");
            var expectedPath = Path.Combine(work, "expected.txt");
            const string title = "ДОГОВОР № 162-ГБР";
            const string description = "Проверочный договор с вымышленными данными.";
            string[][] rows = [["ЗАКАЗЧИК", "ИСПОЛНИТЕЛЬ"], ["ООО «Демо Заказчик»", "ООО «Демо Исполнитель»"]];
            using (var doc = WordprocessingDocument.Create(source, WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body());
                var body = main.Document.Body!;
                body.Append(new Paragraph(new Run(new Text("ДОГОВОР № 162-")), new Run(new Text("ГБР"))));
                body.Append(new Paragraph(new Run(new Text(description))));
                var table = new Table();
                foreach (var row in rows)
                    table.Append(new TableRow(row.Select(value => new TableCell(new Paragraph(new Run(new Text(value)))))));
                body.Append(table);
                main.Document.Save();
            }
            File.WriteAllText(expectedPath, string.Join("\n", new[] { title, description }.Concat(rows.SelectMany(row => row))));
            var sourceHash = SHA256.HashData(File.ReadAllBytes(source));
            string[][] checks =
            [
                ["--aggregator-dedup-check"],
                ["--ner-parity", source, expectedPath],
                ["--ner-converter-check", source],
                ["--feedback-check", source]
            ];
            var failures = 0;
            Console.WriteLine("Lassistant smoke test: synthetic DOCX, no Excel/OCR or Python model required.");
            foreach (var check in checks)
            {
                try
                {
                    var result = await SmokeChecks.RunAsync(check);
                    if (result != 0) { failures++; Console.Error.WriteLine($"FAIL {check[0]}: exit={result}"); }
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.Error.WriteLine($"FAIL {check[0]}: {ex.Message}");
                }
            }
            if (!sourceHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))))
            {
                failures++;
                Console.Error.WriteLine("FAIL: source DOCX was modified");
            }
            Console.WriteLine(failures == 0 ? "ALL 4 SMOKE CHECKS PASSED; source unchanged." : $"FAIL: {failures} smoke checks failed.");
            return failures == 0 ? 0 : 1;
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }
}
