using Lassistant.Core.Models;
using Lassistant.SmokeTest;
using Lassistant.Templates;

return args.Length == 0 ? await SmokeSuite.RunAsync() : await SmokeChecks.RunAsync(args);

internal static class SmokeChecks
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length > 0 && args[0] == "--feedback-check")
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: --feedback-check <input.docx>");
                return 64;
            }

            var document = DocxPlainTextExtractor.ExtractForNer(args[1]);
            var fragment = document.Fragments.First(item => item.Text.Length >= 6);
            var before = new ContractNerEntity
            {
                Role = "номер_договора",
                Text = document.Text[fragment.Start..(fragment.Start + 3)],
                Start = fragment.Start,
                End = fragment.Start + 3,
                Score = 0.5,
                ParagraphIndex = fragment.Paragraph
            };
            var session = NerFeedbackService.Start(
                args[1],
                document,
                [before],
                ContractNerProcessingModes.Production,
                minScore: 0.05,
                autoAcceptScore: 0.3);
            NerFeedbackService.Complete(
                session,
                [
                    new NerFeedbackReview
                    {
                        Before = before,
                        AutoAcceptScore = 0.3,
                        FinalRole = before.Role,
                        FinalText = document.Text[fragment.Start..(fragment.Start + 6)],
                        FinalStart = fragment.Start,
                        FinalEnd = fragment.Start + 6,
                        IsAccepted = true,
                        CorrectionReason = "обрезан конец",
                        Comment = "regression check"
                    }
                ],
                "feedback-check-template.docx");

            var required = new[]
            {
                "metadata.json",
                "input.txt",
                "input.fragments.json",
                "before.json",
                "after.json",
                "corrections.json",
                "completion.json"
            };
            var missing = required.Where(file => !File.Exists(Path.Combine(session.DirectoryPath, file))).ToList();
            var corrections = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(session.DirectoryPath, "corrections.json")));
            var valid = missing.Count == 0 && corrections.RootElement.GetArrayLength() == 1;
            try { Directory.Delete(session.DirectoryPath, recursive: true); } catch { /* best effort */ }
            if (!valid)
            {
                Console.Error.WriteLine($"FAIL NER feedback artifacts: missing={string.Join(",", missing)}");
                return 26;
            }
            Console.WriteLine("OK NER feedback before/after artifacts");
            return 0;
        }

        if (args.Length > 0 && args[0] == "--aggregator-dedup-check")
        {
            var input = new List<ContractNerEntity>
            {
                new() { Role = "объект_наименование", Text = "Аптека", Start = 10, End = 16, Score = 0.26 },
                new() { Role = "объект_наименование", Text = "Аптека", Start = 10, End = 16, Score = 0.36 },
                new() { Role = "объект_наименование", Text = "аптека", Start = 30, End = 36, Score = 0.43 },
                new() { Role = "исполнитель_инн", Text = "7227220728", Start = 40, End = 50, Score = 0.09 },
                new() { Role = "исполнитель_инн", Text = "7227220728", Start = 40, End = 50, Score = 0.40 }
            };
            var output = NerEntityAggregator.Aggregate(input, minScore: 0.05);
            var objectTypes = output.Where(e => e.Role == "объект_наименование").ToList();
            var inns = output.Where(e => e.Role == "исполнитель_инн").ToList();
            if (objectTypes.Count != 2
                || objectTypes.Single(e => e.Start == 10).Score != 0.36
                || objectTypes.Single(e => e.Start == 30).Text != "аптека"
                || inns.Count != 1
                || inns[0].Score != 0.40)
            {
                Console.Error.WriteLine("FAIL C# aggregator exact dedup");
                return 23;
            }
            Console.WriteLine("OK C# aggregator exact dedup");
            return 0;
        }

        if (args.Length > 0 && args[0] == "--aggregate-ner")
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: --aggregate-ner <entities.json> <output.json>");
                return 64;
            }

            var entities = System.Text.Json.JsonSerializer.Deserialize<List<ContractNerEntity>>(
                File.ReadAllText(args[1], System.Text.Encoding.UTF8),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            var aggregated = NerEntityAggregator.Aggregate(entities, minScore: 0);
            File.WriteAllText(
                args[2],
                System.Text.Json.JsonSerializer.Serialize(
                    aggregated,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Console.WriteLine($"OK aggregate NER: {entities.Count} -> {aggregated.Count}");
            return 0;
        }

        if (args.Length > 0 && args[0] == "--ner-converter-check")
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: --ner-converter-check <input.docx>");
                return 64;
            }

            var document = DocxPlainTextExtractor.ExtractForNer(args[1]);
            const string contractNumber = "162-ГБР";
            var start = document.Text.IndexOf(contractNumber, StringComparison.Ordinal);
            if (start < 0)
            {
                Console.Error.WriteLine("FAIL converter check: contract number not found");
                return 20;
            }

            var output = Path.Combine(Path.GetTempPath(), "lassistant_ner_converter_" + Guid.NewGuid().ToString("N") + ".docx");
            NerSpanToTemplateConverter.Convert(
                args[1],
                output,
                document,
                [(start, start + contractNumber.Length, "номер_договора")]);
            string xml;
            using (var archive = System.IO.Compression.ZipFile.OpenRead(output))
            using (var entry = archive.GetEntry("word/document.xml")!.Open())
            using (var reader = new StreamReader(entry))
                xml = reader.ReadToEnd();
            try { File.Delete(output); } catch { /* best effort */ }
            if (!xml.Contains("{{номер_договора}}", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("FAIL converter check: placeholder not written");
                return 21;
            }

            Console.WriteLine("OK NER converter offsets");
            return 0;
        }

        if (args.Length > 0 && args[0] == "--contract-template-check")
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: --contract-template-check <input.docx>");
                return 64;
            }

            var sourcePath = args[1];
            var sourceHashBefore = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(sourcePath)));
            var document = DocxPlainTextExtractor.ExtractForNer(sourcePath);
            var entities = await ContractNerService.AnalyzeDocumentAsync(document);
            var selected = entities
                .Where(entity => entity.Start >= 0 && entity.End > entity.Start)
                .OrderByDescending(entity => entity.Score)
                .FirstOrDefault();
            if (selected is null)
            {
                Console.Error.WriteLine("FAIL production template check: no entities");
                return 24;
            }

            var output = Path.Combine(Path.GetTempPath(), "lassistant_contract_template_check.docx");
            NerSpanToTemplateConverter.Convert(
                sourcePath,
                output,
                document,
                [(selected.Start, selected.End, selected.Role)]);
            string xml;
            using (var archive = System.IO.Compression.ZipFile.OpenRead(output))
            using (var entry = archive.GetEntry("word/document.xml")!.Open())
            using (var reader = new StreamReader(entry))
                xml = reader.ReadToEnd();
            var sourceHashAfter = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(sourcePath)));
            try { File.Delete(output); } catch { /* best effort */ }

            var placeholder = "{{" + selected.Role + "}}";
            if (!xml.Contains(placeholder, StringComparison.Ordinal)
                || sourceHashBefore != sourceHashAfter)
            {
                Console.Error.WriteLine("FAIL production template check: placeholder/source integrity");
                return 25;
            }

            Console.WriteLine(
                $"OK production template: role={selected.Role}, score={selected.Score:F4}, source_unchanged=true");
            return 0;
        }

        if (args.Length > 0 && args[0] is "--contract-ner" or "--contract-ner-check")
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: --contract-ner <input.docx>");
                return 64;
            }

            var document = DocxPlainTextExtractor.ExtractForNer(args[1]);
            var entities = await ContractNerService.AnalyzeDocumentAsync(document);
            var mismatches = entities.Count(entity =>
                entity.Start < 0
                || entity.End > document.Text.Length
                || entity.End < entity.Start
                || entity.Text != document.Text[entity.Start..entity.End]);
            if (mismatches > 0)
            {
                Console.Error.WriteLine($"FAIL NER span/text invariant: mismatches={mismatches}");
                return 22;
            }

            if (args[0] == "--contract-ner-check")
            {
                Console.WriteLine($"OK NER span/text invariant: entities={entities.Count}");
                return 0;
            }

            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(entities,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        if (args.Length > 0 && args[0] == "--ner-parity")
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: --ner-parity <input.docx> <expected.txt>");
                return 64;
            }

            var document = DocxPlainTextExtractor.ExtractForNer(args[1]);
            var expected = File.ReadAllText(args[2], System.Text.Encoding.UTF8)
                .Replace("\r\n", "\n").Replace('\r', '\n');
            if (!string.Equals(document.Text, expected, StringComparison.Ordinal))
            {
                Console.Error.WriteLine(
                    $"FAIL extraction parity: expected={expected.Length}, actual={document.Text.Length}");
                return 10;
            }

            foreach (var fragment in document.Fragments)
            {
                if (fragment.Start < 0 || fragment.End < fragment.Start
                    || fragment.End > document.Text.Length
                    || document.Text[fragment.Start..fragment.End] != fragment.Text)
                {
                    Console.Error.WriteLine($"FAIL fragment offsets: paragraph={fragment.Paragraph}");
                    return 11;
                }

                if (fragment.Source == "table"
                    && fragment.Text.Contains("ИСПОЛНИТЕЛЬ", StringComparison.OrdinalIgnoreCase)
                    && fragment.Text.Contains("ЗАКАЗЧИК", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"FAIL mixed parties: paragraph={fragment.Paragraph}");
                    return 12;
                }
            }

            Console.WriteLine(
                $"OK extraction parity: chars={document.Text.Length}, fragments={document.Fragments.Count}");
            return 0;
        }

        if (args.Length > 0 && args[0] == "--docx-extract-ner")
        {
            if (args.Length < 4)
            {
                Console.Error.WriteLine("Usage: --docx-extract-ner <input.docx> <output.txt> <fragments.json>");
                return 64;
            }

            var extracted = DocxPlainTextExtractor.ExtractForNer(args[1]);
            File.WriteAllText(args[2], extracted.Text,
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.WriteAllText(args[3],
                System.Text.Json.JsonSerializer.Serialize(extracted.Fragments,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Console.WriteLine(args[2]);
            return 0;
        }

        if (args.Length > 0 && args[0] == "--docx-extract")
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: --docx-extract <input.docx> <output.txt>");
                return 64;
            }

            File.WriteAllText(args[2], DocxPlainTextExtractor.Extract(args[1]),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Console.WriteLine(args[2]);
            return 0;
        }
        Console.Error.WriteLine("Unknown smoke-test option.");
        return 64;
    }
}
