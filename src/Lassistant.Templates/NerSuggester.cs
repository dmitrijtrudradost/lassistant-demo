using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Lassistant.Templates;

/// <summary>
/// Вызывает <c>scripts/ner_helper.py</c> (natasha) для уточнения ключей
/// у неуверенно распознанных сущностей.
/// Если Python или natasha не установлены — возвращает <c>null</c> без исключения.
/// </summary>
public static class NerSuggester
{
    // ─── Запрос / ответ ───────────────────────────────────────────────────────

    /// <param name="Text">Текст цветного выделения.</param>
    /// <param name="Context">Контекст абзаца.</param>
    /// <param name="Color">Цвет: "cyan" | "yellow" | "green".</param>
    public sealed record NerRequest(string Text, string Context, string Color);

    /// <param name="Key">Предложенный ключ плейсхолдера или "custom_value".</param>
    /// <param name="Error">Сообщение об ошибке если NER не сработал.</param>
    public sealed record NerResult(string Key, string? Error = null);

    // ─── Публичный API ────────────────────────────────────────────────────────

    /// <summary>
    /// Асинхронно уточняет ключи плейсхолдеров через natasha NER.
    /// </summary>
    /// <returns>
    /// Список результатов в том же порядке, что и <paramref name="requests"/>,
    /// или <c>null</c> если Python / natasha / скрипт недоступны.
    /// </returns>
    public static async Task<IReadOnlyList<NerResult>?> RefineAsync(
        IReadOnlyList<NerRequest> requests,
        CancellationToken ct = default)
    {
        if (requests.Count == 0) return [];

        var script = FindScript();
        if (script is null) return null;

        var python = FindPython();
        if (python is null) return null;

        var jsonIn = JsonSerializer.Serialize(
            requests.Select(r => new { text = r.Text, context = r.Context, color = r.Color }),
            new JsonSerializerOptions { WriteIndented = false });

        try
        {
            // UTF8Encoding(false) — UTF-8 без BOM: Python's json.loads не понимает BOM
            var utf8NoBom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

            var psi = new ProcessStartInfo(python, $"\"{script}\"")
            {
                RedirectStandardInput  = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                StandardInputEncoding  = utf8NoBom,
                StandardOutputEncoding = utf8NoBom,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            await proc.StandardInput.WriteAsync(jsonIn.AsMemory(), ct);
            proc.StandardInput.Close();

            var output = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                return null;

            var elements = JsonSerializer.Deserialize<List<JsonElement>>(output);
            if (elements is null) return null;

            return elements.Select(e => new NerResult(
                e.TryGetProperty("key",   out var k)   ? k.GetString()   ?? "custom_value" : "custom_value",
                e.TryGetProperty("error", out var err) ? err.GetString() : null
            )).ToList();
        }
        catch
        {
            return null;
        }
    }

    // ─── Вспомогательные методы ───────────────────────────────────────────────

    private static string? FindScript()
    {
        // При PublishSingleFile exe распаковывается во временную папку,
        // поэтому AppContext.BaseDirectory указывает туда, а не на папку Lassistant.exe.
        // Используем Environment.ProcessPath для получения реального пути к exe.
        var exeDir = string.IsNullOrEmpty(Environment.ProcessPath)
            ? AppContext.BaseDirectory
            : Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

        var candidates = new[]
        {
            // Publish: рядом с Lassistant.exe → scripts/ner_helper.py
            Path.Combine(exeDir,              "scripts", "ner_helper.py"),
            // Dev: bin/Debug/net8.0-windows → поднимаемся к корню проекта
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts", "ner_helper.py"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "ner_helper.py"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "scripts", "ner_helper.py"),
        };

        return candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists);
    }

    private static string? FindPython()
    {
        // Сначала пробуем команды из PATH (терминал, Conda, system-wide Python)
        foreach (var cmd in new[] { "python", "python3", "py" })
        {
            if (TryPython(cmd)) return cmd;
        }

        // Затем ищем python.exe по типичным путям установки Windows
        var searchDirs = new List<string>();

        // Пользовательская установка: AppData\Local\Programs\Python\Python3xx\
        var localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Python");
        TryAddSubdirs(searchDirs, localPrograms, "Python3*");

        // Системная установка: C:\Program Files\Python3xx\
        TryAddSubdirs(searchDirs, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Python3*");
        TryAddSubdirs(searchDirs, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Python3*");

        // C:\Python3xx\
        TryAddSubdirs(searchDirs, @"C:\", "Python3*");

        foreach (var dir in searchDirs.OrderByDescending(d => d))
        {
            var exe = Path.Combine(dir, "python.exe");
            if (File.Exists(exe) && TryPython(exe))
                return exe;
        }

        return null;
    }

    private static void TryAddSubdirs(List<string> list, string root, string pattern)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
        try
        {
            list.AddRange(Directory.EnumerateDirectories(root, pattern, SearchOption.TopDirectoryOnly));
        }
        catch { /* нет доступа или папка не существует */ }
    }

    private static bool TryPython(string cmd)
    {
        try
        {
            var psi = new ProcessStartInfo(cmd, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
