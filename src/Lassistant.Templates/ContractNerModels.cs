using System.Text.Json.Serialization;

namespace Lassistant.Templates;

/// <summary>Настройки локального Contract NER (config/contract-ner.json).</summary>
public sealed class ContractNerSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Путь к python.exe или пусто для автопоиска.</summary>
    [JsonPropertyName("python_path")]
    public string PythonPath { get; set; } = "";

    [JsonPropertyName("script_path")]
    public string ScriptPath { get; set; } = "scripts/contract_ner/inference_cli.py";

    [JsonPropertyName("model_path")]
    public string ModelPath { get; set; } = "models/contract-ner/ner_model_1.1";

    /// <summary>Каталог с подпапками ner_model_* для выбора в «Настройки».</summary>
    [JsonPropertyName("models_root")]
    public string ModelsRoot { get; set; } = "models/contract-ner";

    /// <summary>Версия схемы/пакета NER (например 1.1.1); для UI и документации.</summary>
    [JsonPropertyName("model_version")]
    public string ModelVersion { get; set; } = "1.1.1";

    [JsonPropertyName("min_score")]
    public double MinScore { get; set; } = 0.05;

    /// <summary>
    /// Сущности с меньшим score показываются для ревью, но не выбираются автоматически.
    /// </summary>
    [JsonPropertyName("auto_accept_score")]
    public double AutoAcceptScore { get; set; } = 0.30;

    /// <summary>
    /// production = Python postprocess + C# aggregator;
    /// python_postprocess = Python postprocess only; raw = bare NER output.
    /// </summary>
    [JsonPropertyName("processing_mode")]
    public string ProcessingMode { get; set; } = ContractNerProcessingModes.Production;

    [JsonPropertyName("timeout_seconds")]
    public int TimeoutSeconds { get; set; } = 180;

    [JsonPropertyName("apply_postprocess")]
    public bool ApplyPostprocess { get; set; } = true;
}

public static class ContractNerProcessingModes
{
    public const string Production = "production";
    public const string PythonPostprocess = "python_postprocess";
    public const string Raw = "raw";

    public static string Normalize(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            PythonPostprocess => PythonPostprocess,
            Raw => Raw,
            _ => Production
        };
}

/// <summary>Структурированный вход NER: текст модели и метаданные каждой строки.</summary>
public sealed class ContractNerDocument
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("fragments")]
    public List<ContractNerFragment> Fragments { get; set; } = [];
}

/// <summary>Происхождение одной строки текста, переданной модели.</summary>
public sealed class ContractNerFragment
{
    [JsonPropertyName("paragraph")]
    public int Paragraph { get; set; }

    [JsonPropertyName("start")]
    public int Start { get; set; }

    [JsonPropertyName("end")]
    public int End { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "body";

    [JsonPropertyName("table")]
    public string? Table { get; set; }

    [JsonPropertyName("row")]
    public int? Row { get; set; }

    [JsonPropertyName("cell")]
    public string? Cell { get; set; }

    [JsonPropertyName("column")]
    public int? Column { get; set; }
}

/// <summary>Одна сущность из Contract NER CLI.</summary>
public sealed class ContractNerEntity
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("start")]
    public int Start { get; set; }

    [JsonPropertyName("end")]
    public int End { get; set; }

    [JsonPropertyName("paragraph_index")]
    public int ParagraphIndex { get; set; }

    [JsonPropertyName("party_hint")]
    public string? PartyHint { get; set; }

    [JsonPropertyName("table")]
    public string? Table { get; set; }

    [JsonPropertyName("row")]
    public int? Row { get; set; }

    [JsonPropertyName("cell")]
    public string? Cell { get; set; }

    [JsonPropertyName("column")]
    public int? Column { get; set; }
}
