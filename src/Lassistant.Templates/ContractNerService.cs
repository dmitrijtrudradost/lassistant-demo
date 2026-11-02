using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lassistant.Core.Services;

namespace Lassistant.Templates;

/// <summary>
/// Запускает локальный Contract NER (scripts/contract_ner/inference_cli.py).
/// ПДн не отправляются в сеть — только subprocess на этой машине.
/// </summary>
public static class ContractNerService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public sealed class Availability
    {
        public bool IsAvailable { get; init; }
        public string? Reason { get; init; }
        public string? PythonPath { get; init; }
        public string? ScriptPath { get; init; }
        public string? ModelPath { get; init; }
        public ContractNerSettings Settings { get; init; } = new();
    }

    public static ContractNerSettings LoadSettings()
    {
        try
        {
            var path = AppPaths.ContractNerSettingsPath;
            if (!File.Exists(path)) return new ContractNerSettings();
            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<ContractNerSettings>(json, JsonOptions)
                   ?? new ContractNerSettings();
        }
        catch
        {
            return new ContractNerSettings();
        }
    }

    /// <summary>
    /// Сохраняет только выбранный режим, не удаляя комментарии и прочие поля JSON.
    /// </summary>
    public static void SaveProcessingMode(string processingMode)
    {
        var path = AppPaths.ContractNerSettingsPath;
        var root = File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject
            : new JsonObject();
        root ??= new JsonObject();
        root["processing_mode"] = ContractNerProcessingModes.Normalize(processingMode);
        File.WriteAllText(
            path,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            Utf8NoBom);
    }

    /// <summary>Сохраняет выбранный в UI минимальный score, сохраняя остальные поля JSON.</summary>
    public static void SaveMinScore(double minScore)
    {
        var path = AppPaths.ContractNerSettingsPath;
        var root = File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject
            : new JsonObject();
        root ??= new JsonObject();
        root["min_score"] = Math.Clamp(minScore, 0, 1);
        File.WriteAllText(
            path,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            Utf8NoBom);
    }

    /// <summary>
    /// Сохраняет ключевые поля настроек NER, не удаляя прочие ключи и комментарии JSON.
    /// </summary>
    public static void SaveSettings(ContractNerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var path = AppPaths.ContractNerSettingsPath;
        AppPaths.EnsureDirectory(path);
        var root = File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject
            : new JsonObject();
        root ??= new JsonObject();

        root["enabled"] = settings.Enabled;
        if (!string.IsNullOrWhiteSpace(settings.PythonPath))
            root["python_path"] = settings.PythonPath;
        if (!string.IsNullOrWhiteSpace(settings.ScriptPath))
            root["script_path"] = settings.ScriptPath;
        root["model_path"] = string.IsNullOrWhiteSpace(settings.ModelPath)
            ? "models/contract-ner/ner_model_1.1"
            : settings.ModelPath.Trim();
        root["models_root"] = string.IsNullOrWhiteSpace(settings.ModelsRoot)
            ? "models/contract-ner"
            : settings.ModelsRoot.Trim();
        root["model_version"] = string.IsNullOrWhiteSpace(settings.ModelVersion)
            ? "1.1.1"
            : settings.ModelVersion.Trim();
        root["min_score"] = Math.Clamp(settings.MinScore, 0, 1);
        root["auto_accept_score"] = Math.Clamp(settings.AutoAcceptScore, 0, 1);
        root["processing_mode"] = ContractNerProcessingModes.Normalize(settings.ProcessingMode);
        root["timeout_seconds"] = Math.Max(30, settings.TimeoutSeconds);
        root["apply_postprocess"] = settings.ApplyPostprocess;

        File.WriteAllText(
            path,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            Utf8NoBom);
    }

    /// <summary>Список каталогов ner_model_* в models_root (для UI).</summary>
    public static IReadOnlyList<string> ListAvailableModels()
    {
        var settings = LoadSettings();
        var exeDir = GetExeDirectory();
        var root = ResolvePath(settings.ModelsRoot, exeDir)
                   ?? ResolvePath("models/contract-ner", exeDir);
        if (root is null || !Directory.Exists(root))
            return [];

        return Directory.EnumerateDirectories(root, "ner_model_*", SearchOption.TopDirectoryOnly)
            .Where(dir => File.Exists(Path.Combine(dir, "config.json")))
            .Select(dir => Path.GetFileName(dir)!)
            .OrderByDescending(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Читает version из model_card.json выбранной модели, иначе model_version из настроек.</summary>
    public static string ResolveDisplayedModelVersion(ContractNerSettings? settings = null)
    {
        settings ??= LoadSettings();
        var exeDir = GetExeDirectory();
        var modelDir = ResolvePath(settings.ModelPath, exeDir);
        if (modelDir is not null)
        {
            var cardPath = Path.Combine(modelDir, "model_card.json");
            if (File.Exists(cardPath))
            {
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(cardPath, Encoding.UTF8)) as JsonObject;
                    var version = node?["version"]?.GetValue<string>()
                                  ?? node?["model_version"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(version))
                        return version.Trim();
                }
                catch
                {
                    /* ignore broken card */
                }
            }
        }

        return string.IsNullOrWhiteSpace(settings.ModelVersion)
            ? "—"
            : settings.ModelVersion.Trim();
    }

    public static Availability CheckAvailability()
    {
        var settings = LoadSettings();
        if (!settings.Enabled)
            return new Availability { IsAvailable = false, Reason = "Contract NER отключён в config/contract-ner.json.", Settings = settings };

        var exeDir = GetExeDirectory();
        var script = ResolvePath(settings.ScriptPath, exeDir);
        if (script is null || !File.Exists(script))
            return new Availability
            {
                IsAvailable = false,
                Reason = "Скрипт inference_cli.py не найден. Установите scripts/contract_ner.",
                Settings = settings
            };

        var model = ResolvePath(settings.ModelPath, exeDir);
        if (model is null || !Directory.Exists(model) || !File.Exists(Path.Combine(model, "config.json")))
            return new Availability
            {
                IsAvailable = false,
                Reason = "Модуль NER не установлен: нет models/contract-ner/ner_model_1.1 (или выбранный model_path).",
                ScriptPath = script,
                Settings = settings
            };

        var python = ResolvePython(settings.PythonPath, exeDir);
        if (python is null)
            return new Availability
            {
                IsAvailable = false,
                Reason = "Python не найден. Установите Python 3.11+ и зависимости из scripts/contract_ner/requirements-infer.txt.",
                ScriptPath = script,
                ModelPath = model,
                Settings = settings
            };

        return new Availability
        {
            IsAvailable = true,
            PythonPath = python,
            ScriptPath = script,
            ModelPath = model,
            Settings = settings
        };
    }

    /// <summary>
    /// Разбирает текст договора и возвращает сущности (уже с min_score фильтрацией на стороне CLI).
    /// </summary>
    public static async Task<IReadOnlyList<ContractNerEntity>> AnalyzeTextAsync(
        string documentText,
        string? processingMode = null,
        double? minScore = null,
        CancellationToken ct = default) =>
        await AnalyzeCoreAsync(documentText, structuredInput: false, processingMode, minScore, ct);

    /// <summary>
    /// Разбирает NER-документ вместе со структурой DOCX и сохраняет диагностический вход.
    /// </summary>
    public static async Task<IReadOnlyList<ContractNerEntity>> AnalyzeDocumentAsync(
        ContractNerDocument document,
        string? processingMode = null,
        double? minScore = null,
        CancellationToken ct = default)
    {
        SaveDiagnosticInput(document);
        var requestJson = JsonSerializer.Serialize(document, JsonOptions);
        return await AnalyzeCoreAsync(requestJson, structuredInput: true, processingMode, minScore, ct);
    }

    private static async Task<IReadOnlyList<ContractNerEntity>> AnalyzeCoreAsync(
        string standardInput,
        bool structuredInput,
        string? processingMode,
        double? minScore,
        CancellationToken ct)
    {
        var avail = CheckAvailability();
        if (!avail.IsAvailable)
            throw new InvalidOperationException(avail.Reason ?? "Contract NER недоступен.");

        var settings = avail.Settings;
        var mode = ContractNerProcessingModes.Normalize(processingMode ?? settings.ProcessingMode);
        var effectiveMinScore = Math.Clamp(minScore ?? settings.MinScore, 0, 1);
        var usePythonPostprocess = mode != ContractNerProcessingModes.Raw;
        var useCsharpAggregator = mode == ContractNerProcessingModes.Production;
        var args = new StringBuilder();
        args.Append('"').Append(avail.ScriptPath).Append('"');
        args.Append(structuredInput ? " --stdin-json" : " --stdin");
        args.Append(" --model \"").Append(avail.ModelPath).Append('"');
        // Порог выбирается пользователем до запуска и одинаково действует во всех режимах.
        // Значение 0 означает диагностический вывод без отсечения.
        if (effectiveMinScore > 0)
            args.Append(" --min-score ").Append(effectiveMinScore.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!usePythonPostprocess)
            args.Append(" --no-postprocess");

        var psi = new ProcessStartInfo(avail.PythonPath!, args.ToString())
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(avail.ScriptPath) ?? GetExeDirectory()
        };
        // Windows Python иначе читает stdin как cp1251+surrogateescape → ломает UTF-8 от ПЮ.
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Не удалось запустить Python для Contract NER.");

        var safeText = SanitizeForUtf8(standardInput);
        await proc.StandardInput.WriteAsync(safeText.AsMemory(), ct);
        await proc.StandardInput.FlushAsync(ct);
        proc.StandardInput.Close();

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(30, settings.TimeoutSeconds)));

        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            throw new TimeoutException(
                $"Contract NER превысил таймаут ({settings.TimeoutSeconds} с). Первый запуск может быть долгим.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (proc.ExitCode != 0)
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(stderr)
                    ? $"Contract NER завершился с кодом {proc.ExitCode}."
                    : stderr.Trim());

        if (string.IsNullOrWhiteSpace(stdout))
            return [];

        // CLI пишет только JSON-массив; на всякий случай вырезаем от [ до ]
        var start = stdout.IndexOf('[');
        var end = stdout.LastIndexOf(']');
        if (start < 0 || end <= start)
            throw new InvalidOperationException("Contract NER вернул не-JSON ответ.\n" + stderr);

        var json = stdout[start..(end + 1)];
        var list = JsonSerializer.Deserialize<List<ContractNerEntity>>(json, JsonOptions) ?? [];
        return useCsharpAggregator
            ? NerEntityAggregator.Aggregate(list, effectiveMinScore)
            : list;
    }

    private static void SaveDiagnosticInput(ContractNerDocument document)
    {
        var directory = AppPaths.ContractNerDiagnosticsDirectory;
        Directory.CreateDirectory(directory);
        AtomicWrite(Path.Combine(directory, "input.txt"), document.Text);
        AtomicWrite(
            Path.Combine(directory, "input.fragments.json"),
            JsonSerializer.Serialize(document.Fragments, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
    }

    private static void AtomicWrite(string path, string content)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, content, Utf8NoBom);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporaryPath); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Убирает одиночные UTF-16 surrogates — они не кодируются в UTF-8.
    /// </summary>
    private static string SanitizeForUtf8(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    sb.Append(c);
                    sb.Append(text[++i]);
                }
                else
                    sb.Append('\uFFFD');
            }
            else if (char.IsLowSurrogate(c))
                sb.Append('\uFFFD');
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    // ─── Paths ───────────────────────────────────────────────────────────────

    private static string GetExeDirectory()
    {
        if (!string.IsNullOrEmpty(Environment.ProcessPath))
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath);
            if (!string.IsNullOrEmpty(dir)) return dir;
        }
        return AppContext.BaseDirectory;
    }

    private static string? ResolvePath(string configured, string exeDir)
    {
        if (string.IsNullOrWhiteSpace(configured)) return null;
        if (Path.IsPathRooted(configured))
            return File.Exists(configured) || Directory.Exists(configured) ? configured : null;

        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(exeDir, configured)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured)),
            Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", "..", configured)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", configured)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", configured)),
        };

        return candidates.FirstOrDefault(p => File.Exists(p) || Directory.Exists(p));
    }

    private static string? ResolvePython(string configured, string exeDir)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (File.Exists(configured) && TryPython(configured)) return configured;
            if (TryPython(configured)) return configured;
        }

        // Локальный venv рядом с приложением / репозиторием
        var venvCandidates = new[]
        {
            Path.Combine(exeDir, "python-venv-contract-ner", "Scripts", "python.exe"),
            Path.Combine(exeDir, "..", "python-venv-contract-ner", "Scripts", "python.exe"),
            Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", "..", "python-venv-contract-ner", "Scripts", "python.exe")),
        };
        foreach (var v in venvCandidates)
        {
            if (File.Exists(v) && TryPython(v)) return v;
        }

        foreach (var cmd in new[] { "python", "python3", "py" })
        {
            if (TryPython(cmd)) return cmd;
        }

        var searchDirs = new List<string>();
        var localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Python");
        TryAddSubdirs(searchDirs, localPrograms, "Python3*");
        TryAddSubdirs(searchDirs, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Python3*");

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
        catch { /* ignore */ }
    }

    private static bool TryPython(string cmd)
    {
        try
        {
            var psi = new ProcessStartInfo(cmd, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            if (!p.WaitForExit(3000))
            {
                try { p.Kill(); } catch { /* ignore */ }
                return false;
            }
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
