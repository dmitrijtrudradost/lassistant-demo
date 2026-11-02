using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.RegularExpressions;
using Lassistant.Core.Config;
using Lassistant.Core.Models;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Lassistant.Extractors;

public sealed partial class PassportOcrExtractor : IFileExtractor
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp"];

    public bool CanHandle(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return ImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    public ClientData Extract(string filePath) =>
        StaThreadRunner.Run(() =>
        {
            var text = RecognizeImage(filePath);
            return ParsePassportText(text);
        });

    public string RecognizeImage(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return RecognizeStream(stream.AsRandomAccessStream()).GetAwaiter().GetResult();
    }

    public string RecognizePdfPages(string filePath, int maxPages = 3) =>
        StaThreadRunner.Run(() => RecognizePdfPagesAsync(filePath, maxPages).GetAwaiter().GetResult());

    private static async Task<string> RecognizePdfPagesAsync(string filePath, int maxPages)
    {
        var storageFile = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(filePath)).AsTask().ConfigureAwait(false);
        var pdfDocument = await PdfDocument.LoadFromFileAsync(storageFile).AsTask().ConfigureAwait(false);
        var pageCount = Math.Min((int)pdfDocument.PageCount, maxPages);
        var parts = new List<string>();

        for (uint i = 0; i < pageCount; i++)
        {
            using var page = pdfDocument.GetPage(i);
            using var pageStream = new InMemoryRandomAccessStream();
            var options = new PdfPageRenderOptions { DestinationWidth = (uint)(page.Size.Width * 2) };
            await page.RenderToStreamAsync(pageStream, options).AsTask().ConfigureAwait(false);
            pageStream.Seek(0);

            var text = await RecognizeStream(pageStream).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(text))
                parts.Add(text);
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static async Task<string> RecognizeStream(IRandomAccessStream stream)
    {
        stream.Seek(0);
        var originalDecoder = await BitmapDecoder.CreateAsync(stream).AsTask().ConfigureAwait(false);
        using var original = await originalDecoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied).AsTask().ConfigureAwait(false);

        stream.Seek(0);
        var upscaledDecoder = await BitmapDecoder.CreateAsync(stream).AsTask().ConfigureAwait(false);
        using var upscaled = await OcrImagePreprocessor.PrepareAsync(upscaledDecoder);

        var engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("ru-RU"))
            ?? OcrEngine.TryCreateFromUserProfileLanguages();

        if (engine is null)
            throw new InvalidOperationException("OCR-движок Windows недоступен. Установите языковой пакет русского языка.");

        var originalText = await RecognizeBitmapAsync(engine, original).ConfigureAwait(false);
        var upscaledText = await RecognizeBitmapAsync(engine, upscaled).ConfigureAwait(false);

        return MergeOcrTexts(originalText, upscaledText);
    }

    private static string MergeOcrTexts(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(second))
            return first;
        if (string.IsNullOrWhiteSpace(first))
            return second;

        return $"{first}\n{second}";
    }

    private static async Task<string> RecognizeBitmapAsync(OcrEngine engine, SoftwareBitmap bitmap)
    {
        using var converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(converted).AsTask().ConfigureAwait(false);

        var sb = new StringBuilder();
        foreach (var line in result.Lines)
            sb.AppendLine(line.Text);

        return sb.Length > 0 ? sb.ToString() : result.Text;
    }

    public static ClientData ParsePassportText(string text)
    {
        var data = new ClientData();
        if (string.IsNullOrWhiteSpace(text))
            return data;

        var normalized = OcrTextNormalizer.Normalize(text);
        normalized = OcrDigitNormalizer.FixCodes(normalized);

        var passportMatch = PassportRegex().Match(normalized);
        if (passportMatch.Success)
        {
            if (passportMatch.Groups[4].Success)
            {
                data.PassportSeries = passportMatch.Groups[4].Value;
                data.PassportNumber = passportMatch.Groups[5].Value;
            }
            else
            {
                data.PassportSeries = passportMatch.Groups[1].Value + passportMatch.Groups[2].Value;
                data.PassportNumber = passportMatch.Groups[3].Value;
            }
        }

        var deptMatch = DeptCodeRegex().Match(normalized);
        if (deptMatch.Success)
            data.PassportDeptCode = deptMatch.Value;

        data.PassportIssueDate = ExtractIssueDate(normalized, deptMatch.Index);
        data.FullName = ExtractNameHeuristic(normalized)
            ?? ExtractFullNameNearGender(normalized)
            ?? ExtractFullName(normalized);
        data.PassportIssuedBy = ExtractIssuedBy(normalized, deptMatch.Index);
        data.RegistrationAddress = ExtractAddress(normalized);

        return data;
    }

    private static string ExtractIssueDate(string text, int deptCodeIndex)
    {
        if (deptCodeIndex > 0)
        {
            var nearDept = text[Math.Max(0, deptCodeIndex - 60)..deptCodeIndex];
            var partialMatches = PartialDateRegex().Matches(nearDept);
            if (partialMatches.Count > 0)
            {
                var partial = partialMatches[^1].Groups[1].Value.TrimEnd('.');
                // Неполная дата без года — не подставляем, пользователь допишет вручную.
                if (partial.Length >= 8)
                    return partial;
            }
        }

        foreach (Match date in DateRegex().Matches(text))
        {
            var start = Math.Max(0, date.Index - 25);
            var end = Math.Min(text.Length, date.Index + date.Length + 25);
            var context = text[start..end];
            if (!BirthContextRegex().IsMatch(context))
                return date.Value;
        }

        return string.Empty;
    }

    private static string? ExtractFullNameNearGender(string text)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static l => Regex.Replace(l.Trim(), @"^[^\p{L}]+", ""))
            .Select(static l => l.Trim())
            .Where(static l => l.Length >= 2)
            .ToList();

        var genderIndex = FindLastIndex(lines, static l => IsGenderLine(l));
        var deptIndex = FindLastIndex(lines, static l => DeptCodeRegex().IsMatch(OcrDigitNormalizer.FixCodes(l)));

        if (genderIndex < 0 && deptIndex < 0)
            return null;

        IEnumerable<string> candidates = genderIndex >= 0 && deptIndex >= 0 && genderIndex > deptIndex + 1
            ? lines.Skip(deptIndex + 1).Take(genderIndex - deptIndex - 1)
            : deptIndex >= 0
                ? lines.Skip(deptIndex + 1).Take(4)
                : lines.Skip(Math.Max(0, genderIndex - 3)).Take(Math.Min(3, Math.Max(0, genderIndex)));

        var parts = candidates
            .Where(static l => !IsGenderLine(l))
            .Where(static l => !IsNoiseWord(l))
            .Where(static l => l.Any(char.IsLetter))
            .Select(static l => OcrTextNormalizer.Normalize(l.ToUpperInvariant()))
            .ToList();

        return parts.Count > 0 ? NormalizeSpaces(string.Join(" ", parts)) : null;
    }

    private static string? ExtractNameHeuristic(string text)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static l => Regex.Replace(l.Trim(), @"^[^\p{L}]+", "").Trim())
            .Where(static l => l.Length >= 2)
            .ToList();

        var deptIndex = FindLastIndex(lines, static l => DeptCodeRegex().IsMatch(OcrDigitNormalizer.FixCodes(l)));
        var genderIndex = FindLastIndex(lines, static l => IsGenderLine(l));
        if (deptIndex < 0)
            return null;

        var end = genderIndex > deptIndex ? genderIndex : lines.Count;
        var parts = lines
            .Skip(deptIndex + 1)
            .Take(end - deptIndex - 1)
            .Select(static l => Regex.Replace(l, @"[^\p{L}\-]", ""))
            .Select(static l => OcrTextNormalizer.Normalize(l.ToUpperInvariant()))
            .Where(static l => l.Length >= 3 && !IsGenderLine(l) && !IsNoiseWord(l))
            .ToList();

        return parts.Count > 0 ? NormalizeSpaces(string.Join(" ", parts)) : null;
    }

    private static string ExtractFullName(string text)
    {
        var patterns = new[]
        {
            @"(?:Фамилия|ФАМИЛИЯ)\s*[:\s]*([А-ЯA-Z][а-яa-z]+(?:\s+[А-ЯA-Z][а-яa-z]+){0,2})",
            @"(?:Имя|ИМЯ)\s*[:\s]*([А-ЯA-Z][а-яa-z]+)",
            @"(?:Отчество|ОТЧЕСТВО)\s*[:\s]*([А-ЯA-Z][а-яa-z]+)",
            @"(?:Ф\.?\s*И\.?\s*О\.?)\s*[:\s]*([А-ЯA-Z][а-яa-z]+(?:\s+[А-ЯA-Z][а-яa-z]+){1,2})"
        };

        var surname = string.Empty;
        var firstName = string.Empty;
        var patronymic = string.Empty;

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
                continue;

            if (pattern.Contains("Фамилия", StringComparison.OrdinalIgnoreCase))
                surname = match.Groups[1].Value;
            else if (pattern.Contains("Имя", StringComparison.OrdinalIgnoreCase) && !pattern.Contains("Фамилия", StringComparison.OrdinalIgnoreCase))
                firstName = match.Groups[1].Value;
            else if (pattern.Contains("Отчество", StringComparison.OrdinalIgnoreCase))
                patronymic = match.Groups[1].Value;
            else
                return NormalizeSpaces(match.Groups[1].Value);
        }

        if (!string.IsNullOrWhiteSpace(surname) || !string.IsNullOrWhiteSpace(firstName) || !string.IsNullOrWhiteSpace(patronymic))
        {
            return NormalizeSpaces(string.Join(" ", new[] { surname, firstName, patronymic }
                .Where(static s => !string.IsNullOrWhiteSpace(s))));
        }

        var patronymicMatch = PatronymicRegex().Match(text);
        if (!patronymicMatch.Success)
            patronymicMatch = FuzzyPatronymicRegex().Match(text);

        if (patronymicMatch.Success)
        {
            var start = Math.Max(0, patronymicMatch.Index - 60);
            var window = text[start..(patronymicMatch.Index + patronymicMatch.Length)];
            var words = WordRegex().Matches(window)
                .Select(m => m.Value)
                .Where(w => w.Length >= 3 && !IsNoiseWord(w) && !w.Contains('-'))
                .TakeLast(1)
                .Append(patronymicMatch.Value)
                .Distinct()
                .ToList();

            if (words.Count > 0)
                return NormalizeSpaces(string.Join(" ", words));
        }

        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (FullNameLineRegex().IsMatch(trimmed) && !trimmed.Contains("Российск", StringComparison.OrdinalIgnoreCase))
                return NormalizeSpaces(trimmed);
        }

        return string.Empty;
    }

    private static string ExtractIssuedBy(string text, int deptCodeIndex)
    {
        var match = IssuedByRegex().Match(text);
        if (match.Success)
            return NormalizeSpaces(match.Groups[1].Value);

        var blockMatch = IssuedByBlockRegex().Match(text);
        if (blockMatch.Success)
            return NormalizeSpaces(OcrTextNormalizer.Normalize(blockMatch.Value));

        return string.Empty;
    }

    private static string ExtractAddress(string text)
    {
        var match = AddressRegex().Match(text);
        return match.Success ? NormalizeSpaces(match.Groups[1].Value) : string.Empty;
    }

    private static int FindLastIndex(List<string> list, Func<string, bool> predicate)
    {
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (predicate(list[i]))
                return i;
        }

        return -1;
    }

    private static bool IsGenderLine(string line)
    {
        var trimmed = line.Trim().TrimEnd('.');
        return trimmed.Equals("ЖЕН", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("МУЖ", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("XEH", StringComparison.OrdinalIgnoreCase)
            || trimmed.Length <= 6 && trimmed.Contains("ЖЕН", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNoiseWord(string word) =>
        word.Contains("РОССИ", StringComparison.OrdinalIgnoreCase)
        || word.Contains("УФМС", StringComparison.OrdinalIgnoreCase)
        || word.Contains("ОТДЕЛ", StringComparison.OrdinalIgnoreCase)
        || word.Contains("ПАСПОРТ", StringComparison.OrdinalIgnoreCase)
        || word.Contains("ОБЛАСТ", StringComparison.OrdinalIgnoreCase)
        || word.Contains("НОВОТРО", StringComparison.OrdinalIgnoreCase)
        || word.Contains("ОРЕНБУР", StringComparison.OrdinalIgnoreCase)
        || word is "Г" or "ГОР" or "ЖЕН" or "МУЖ" or "XEH"
        || DateRegex().IsMatch(word)
        || DeptCodeRegex().IsMatch(word);

    private static string NormalizeSpaces(string value) =>
        Regex.Replace(value.Trim(), @"\s{2,}", " ");

    [GeneratedRegex(@"\b(\d{2})\s?(\d{2})\s?(\d{6})\b|\b(\d{4})\s?(\d{6})\b")]
    private static partial Regex PassportRegex();

    [GeneratedRegex(@"\b\d{3}-\d{3}\b")]
    private static partial Regex DeptCodeRegex();

    [GeneratedRegex(@"\b\d{2}\.\d{2}\.\d{4}\b")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"(\d{2}\.\d{2}\.?\d{0,4})")]
    private static partial Regex PartialDateRegex();

    [GeneratedRegex(@"(?:род\.?|РОД\.?|г\.|Г\.|XEH|ЖЕН|МУЖ)", RegexOptions.IgnoreCase)]
    private static partial Regex BirthContextRegex();

    [GeneratedRegex(@"\b[А-ЯA-Z]{3,}(?:ОВНА|ОВИЧ|ЕВНА|ЕВИЧ|ИЧНА|ИНИЧ)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PatronymicRegex();

    [GeneratedRegex(@"\b[А-ЯA-Z]{4,}(?:ОВНА|ОВИЧ|ОВНА|ОВИЧ|ЕВНА|ЕВИЧ|ОБНА|ОБВА|АРОВНА|АРОВИЧ)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FuzzyPatronymicRegex();

    [GeneratedRegex(@"\b[А-ЯA-ZЁ][А-ЯA-Zа-яa-zё-]{2,}\b")]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"^[А-ЯA-ZЁ][а-яa-zё]+\s+[А-ЯA-ZЁ][а-яa-zё]+\s+[А-ЯA-ZЁ][а-яa-zё]+$")]
    private static partial Regex FullNameLineRegex();

    [GeneratedRegex(@"(?:Кем\s+выдан|Выдан|кем\s+выдан)[:\s]+(.+?)(?:\r|\n|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex IssuedByRegex();

    [GeneratedRegex(@"(?:ОТДЕЛОМ|ОТДЕЛ)\s+УФМС.{5,160}?(?=\d{2}\.\d{2}|\d{3}[-\s]\d{3})", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex IssuedByBlockRegex();

    [GeneratedRegex(@"(?:ОТДЕЛ\w*|ОТАЕД\w*|OTAED\w*|УФМС|УФМС|YQMC|ОВД|МВД|ГУ\s*МВД|МВД\s*РОССИИ)[^\d]{10,200}", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorityRegex();

    [GeneratedRegex(@"(?:Место\s+жительства|Адрес\s+регистрации|Зарегистрирован|Проживает)[:\s]+(.+?)(?:\r|\n|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AddressRegex();

    [GeneratedRegex(@"(?:ОТДЕЛ|ОТАЕД|OTAED|УФМС|YQMC|ОВД|МВД|ОРЕНБУР|OPEH|ОБЛАСТ|OBFI|НОВОТРО|HOBOT)", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorityLineRegex();

    [GeneratedRegex(@"^(?:ЖЕН\.?|МУЖ\.?)$", RegexOptions.IgnoreCase)]
    private static partial Regex GenderLineRegex();
}
